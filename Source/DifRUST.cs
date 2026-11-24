using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// DifRUST — только освещение. Клиентский мод.
// 20:00–22:00 — плавное затемнение.
// 22:00–04:00 — полная тьма.
// 04:00–06:00 — плавный рассвет.
public class DifRUSTMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        // Сначала настройки и меню: даже если один патч не найдёт метод,
        // мод останется управляемым и запишет ошибку в лог.
        RustSettings.Load();
        DifMenu.Create();

        // Ночное небо 1.6.0: кубмапа + луна, мягкая связка с LunarEvents.
        DifSkyAssets.Root = Path.Combine(_modInstance.Path, "Assets", "Sky");
        DifSky.Create();

        // Диагностика неба: только чтение и лог, визуально ничего не меняет.
        DifSkyDiag.Create();

        try
        {
            Harmony harmony = new Harmony("difrust.client");
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Debug.LogError("[DifRUST] patch " + type.Name + " failed: " + e);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST] patch init failed: " + e);
        }
    }
}

internal static class RustSettings
{
    public const float DefaultNightFactor = 0.06f;
    public const float MinNightFactor = 0.02f;
    public const float MaxNightFactor = 0.60f;

    // Насколько ярче делать освещение под крышей (0 — как снаружи, 1 — как днём).
    public const float DefaultIndoorBoost = 0.75f;

    // Холодный оттенок ночи: 0 — как в ваниле, 1 — максимум синевы.
    public const float DefaultNightTint = 0.45f;

    // Фазы луны: 0 — все ночи одинаковые, 1 — сильный контраст
    // (полнолуние светлее, новолуние почти чёрное).
    public const float DefaultMoonPhases = 0.5f;

    // Биомный оттенок ночи. Палитра — как в Silent Fog.
    public const float DefaultBiomeTint = 0.55f;

    public static bool Enabled = true;
    public static float NightFactorValue = DefaultNightFactor;
    public static float IndoorBoost = DefaultIndoorBoost;
    public static float NightTint = DefaultNightTint;
    public static float MoonPhases = DefaultMoonPhases;

    // Эффекты 1.5.0.
    public static bool WeatherOn = true;
    public static bool EyeAdapt = true;
    public static bool LightRelief = true;
    public static bool NvgRelief = true;
    public static bool BiomeTintOn = true;
    public static bool LunarLink = true;
    public static bool ForceForClients = true;
    public static float BiomeTintValue = DefaultBiomeTint;

    // 1.6.0: ночное небо. Косметика клиента — с хостом не синхронизируется.
    public static bool SkyOn = true;
    public static float SkyAmount = 1f;
    public static bool SkyMoon = true; // вторая луна: каждую ночь, ярче в особую
    public static bool SkyMesh = true;

    // CVar-имена: хост раздаёт настройки ванильным NetPackageModifyCVar.
    public const string CvarEnabled = "dr_on";
    public const string CvarNight = "dr_night";
    public const string CvarIndoor = "dr_indoor";
    public const string CvarTint = "dr_tint";
    public const string CvarMoon = "dr_moon";
    public const string CvarForce = "dr_force";
    public const string CvarWeather = "dr_weather";
    public const string CvarEyes = "dr_eyes";
    public const string CvarLight = "dr_light";
    public const string CvarBiomeOn = "dr_biome";
    public const string CvarBiomeAmount = "dr_biome_amt";
    public const string CvarLunar = "dr_lunar";
    public const string CvarNvg = "dr_nvg";
    public const string CvarSeq = "dr_seq";

    private static float nextRead;
    private static float nextSend;
    private static float nextHeartbeat;
    private static float nextForce;
    private static bool dirty;

    // Отложенная запись cfg: не дёргаем диск на каждое движение ползунка.
    private static bool pendingSave;
    private static float nextSave;

    // Хост-замок: dr_seq обновляется пульсом. Если он замер — замок снимается.
    private static float hostSeq;
    private static float lockSeq;
    private static float lockSeqTime;
    private static bool lockSeqSeen;

    // Локальные настройки клиента на время замка.
    private static bool hasSnapshot;
    private static bool snapEnabled;
    private static float snapNight;
    private static float snapIndoor;
    private static float snapTint;
    private static float snapMoon;
    private static float snapBiomeAmount;
    private static bool snapWeather;
    private static bool snapEyes;
    private static bool snapLight;
    private static bool snapNvg;
    private static bool snapBiomeOn;
    private static bool snapLunar;

    public static bool Synced { get; private set; }

    // Клиент под серверным замком: меню блокируется, настройки от хоста.
    public static bool Forced { get; private set; }

    public static bool IsHost
    {
        get
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            return cm == null || cm.IsServer;
        }
    }

    private static string SettingsPath
    {
        get { return Path.Combine(Application.persistentDataPath, "DifRUST.cfg"); }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;

            string[] lines = File.ReadAllLines(SettingsPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                float num;
                if (!float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out num)) continue;

                if (key == "enabled") Enabled = num >= 0.5f;
                else if (key == "night") NightFactorValue = Mathf.Clamp(num, MinNightFactor, MaxNightFactor);
                else if (key == "indoor") IndoorBoost = Mathf.Clamp01(num);
                else if (key == "tint") NightTint = Mathf.Clamp01(num);
                else if (key == "moon") MoonPhases = Mathf.Clamp01(num);
                else if (key == "weather") WeatherOn = num >= 0.5f;
                else if (key == "eyes") EyeAdapt = num >= 0.5f;
                else if (key == "light") LightRelief = num >= 0.5f;
                else if (key == "nvg") NvgRelief = num >= 0.5f;
                else if (key == "biome") BiomeTintOn = num >= 0.5f;
                else if (key == "biome_amount") BiomeTintValue = Mathf.Clamp01(num);
                else if (key == "lunar") LunarLink = num >= 0.5f;
                else if (key == "force") ForceForClients = num >= 0.5f;
                else if (key == "sky") SkyOn = num >= 0.5f;
                else if (key == "sky_amt") SkyAmount = Mathf.Clamp(num, 0.3f, 1.5f);
                else if (key == "sky_moon") SkyMoon = num >= 0.5f;
                else if (key == "sky_mesh") SkyMesh = num >= 0.5f;
            }
        }
        catch
        {
        }
    }

    public static void Save()
    {
        if (Forced && !IsHost) return;
        pendingSave = true;
    }

    // Запись на диск не чаще раза в секунду; финальный сброс — при закрытии меню.
    private static void TickSave()
    {
        if (!pendingSave) return;
        if (Forced && !IsHost) return;
        if (Time.time < nextSave) return;

        pendingSave = false;
        nextSave = Time.time + 1f;
        WriteFile();
    }

    public static void FlushSave()
    {
        if (!pendingSave) return;
        if (Forced && !IsHost) return;

        pendingSave = false;
        WriteFile();
    }

    private static void WriteFile()
    {
        try
        {
            string text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "enabled={0}\nnight={1}\nindoor={2}\ntint={3}\nmoon={4}\n" +
                "weather={5}\neyes={6}\nlight={7}\nnvg={8}\nbiome={9}\n" +
                "biome_amount={10}\nlunar={11}\nforce={12}\n" +
                "sky={13}\nsky_amt={14}\nsky_moon={15}\nsky_mesh={16}\n",
                Enabled ? "1" : "0", NightFactorValue, IndoorBoost, NightTint, MoonPhases,
                WeatherOn ? "1" : "0", EyeAdapt ? "1" : "0", LightRelief ? "1" : "0",
                NvgRelief ? "1" : "0", BiomeTintOn ? "1" : "0", BiomeTintValue,
                LunarLink ? "1" : "0", ForceForClients ? "1" : "0",
                SkyOn ? "1" : "0", SkyAmount, SkyMoon ? "1" : "0",
                SkyMesh ? "1" : "0");
            File.WriteAllText(SettingsPath, text);
        }
        catch
        {
        }
    }

    public static void Reset()
    {
        Enabled = true;
        NightFactorValue = DefaultNightFactor;
        IndoorBoost = DefaultIndoorBoost;
        NightTint = DefaultNightTint;
        MoonPhases = DefaultMoonPhases;
        WeatherOn = true;
        EyeAdapt = true;
        LightRelief = true;
        NvgRelief = true;
        BiomeTintOn = true;
        BiomeTintValue = DefaultBiomeTint;
        LunarLink = true;
        ForceForClients = true;
        SkyOn = true;
        SkyAmount = 1f;
        SkyMoon = true;
        SkyMesh = true;
        MarkDirty();
        Save();
    }

    // Пресеты: сила тьмы, яркость в помещениях, холодный оттенок.
    // 0 — мягкая ночь, 1 — стандарт, 2 — жёсткий Rust.
    public static void ApplyPreset(int index)
    {
        if (index == 0)
        {
            NightFactorValue = 0.30f;
            IndoorBoost = 0.80f;
            NightTint = 0.35f;
        }
        else if (index == 1)
        {
            NightFactorValue = 0.06f;
            IndoorBoost = 0.75f;
            NightTint = 0.45f;
        }
        else
        {
            NightFactorValue = 0.02f;
            IndoorBoost = 0.45f;
            NightTint = 0.65f;
        }

        MarkDirty();
        Save();
    }

    public static void MarkDirty()
    {
        dirty = true;
    }

    private static void CaptureLocal()
    {
        if (hasSnapshot) return;

        hasSnapshot = true;
        snapEnabled = Enabled;
        snapNight = NightFactorValue;
        snapIndoor = IndoorBoost;
        snapTint = NightTint;
        snapMoon = MoonPhases;
        snapBiomeAmount = BiomeTintValue;
        snapWeather = WeatherOn;
        snapEyes = EyeAdapt;
        snapLight = LightRelief;
        snapNvg = NvgRelief;
        snapBiomeOn = BiomeTintOn;
        snapLunar = LunarLink;
    }

    private static void RestoreLocal()
    {
        if (!hasSnapshot) return;

        hasSnapshot = false;
        Enabled = snapEnabled;
        NightFactorValue = snapNight;
        IndoorBoost = snapIndoor;
        NightTint = snapTint;
        MoonPhases = snapMoon;
        BiomeTintValue = snapBiomeAmount;
        WeatherOn = snapWeather;
        EyeAdapt = snapEyes;
        LightRelief = snapLight;
        NvgRelief = snapNvg;
        BiomeTintOn = snapBiomeOn;
        LunarLink = snapLunar;
        MarkDirty();
    }

    // Клиент берёт то, что ему прислал хост, но только под серверным замком.
    // Без замка каждый крутит под свой монитор.
    public static void ReadFromLocalPlayer(EntityPlayerLocal player)
    {
        if (player == null || player.Buffs == null) return;

        if (IsHost)
        {
            // В своей игре замка нет: возвращаем локальные настройки.
            if (hasSnapshot) RestoreLocal();
            Synced = false;
            Forced = false;
            return;
        }

        float now = Time.time;
        if (now < nextRead) return;
        nextRead = now + 0.2f;

        EntityBuffs buffs = player.Buffs;
        if (!buffs.HasCustomVar(CvarEnabled))
        {
            if (hasSnapshot) RestoreLocal();
            Synced = false;
            Forced = false;
            return;
        }

        Synced = true;
        bool force = buffs.HasCustomVar(CvarForce)
            && buffs.GetCustomVar(CvarForce) >= 0.5f;

        if (force)
        {
            // Замок должен подтверждаться пульсом (dr_seq). Если хост
            // перестал слать настройки, через 6 секунд замок снимается.
            if (!buffs.HasCustomVar(CvarSeq))
            {
                // Старый хост без dr_seq: не держим клиента вечно.
                force = false;
            }
            else
            {
                float seq = buffs.GetCustomVar(CvarSeq);
                if (!lockSeqSeen || Mathf.Abs(seq - lockSeq) > 0.001f)
                {
                    lockSeq = seq;
                    lockSeqTime = now;
                    lockSeqSeen = true;
                }
                if (now - lockSeqTime > 6f) force = false;
            }
        }

        if (!force)
        {
            if (hasSnapshot) RestoreLocal();
            Forced = false;
            lockSeqSeen = false;
            return;
        }

        if (!hasSnapshot) CaptureLocal();
        Forced = true;

        Enabled = buffs.GetCustomVar(CvarEnabled) >= 0.5f;
        NightFactorValue = Mathf.Clamp(buffs.GetCustomVar(CvarNight), MinNightFactor, MaxNightFactor);
        IndoorBoost = Mathf.Clamp01(buffs.GetCustomVar(CvarIndoor));
        NightTint = Mathf.Clamp01(buffs.GetCustomVar(CvarTint));
        if (buffs.HasCustomVar(CvarMoon))
            MoonPhases = Mathf.Clamp01(buffs.GetCustomVar(CvarMoon));
        if (buffs.HasCustomVar(CvarWeather))
            WeatherOn = buffs.GetCustomVar(CvarWeather) >= 0.5f;
        if (buffs.HasCustomVar(CvarEyes))
            EyeAdapt = buffs.GetCustomVar(CvarEyes) >= 0.5f;
        if (buffs.HasCustomVar(CvarLight))
            LightRelief = buffs.GetCustomVar(CvarLight) >= 0.5f;
        if (buffs.HasCustomVar(CvarNvg))
            NvgRelief = buffs.GetCustomVar(CvarNvg) >= 0.5f;
        if (buffs.HasCustomVar(CvarBiomeOn))
            BiomeTintOn = buffs.GetCustomVar(CvarBiomeOn) >= 0.5f;
        if (buffs.HasCustomVar(CvarBiomeAmount))
            BiomeTintValue = Mathf.Clamp01(buffs.GetCustomVar(CvarBiomeAmount));
        if (buffs.HasCustomVar(CvarLunar))
            LunarLink = buffs.GetCustomVar(CvarLunar) >= 0.5f;
    }

    // Хост раздаёт настройки всем игрокам.
    public static void Tick()
    {
        TickSave();
        if (!IsHost) return;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return;

        if (dirty && Time.time >= nextSend)
        {
            dirty = false;
            nextSend = Time.time + 0.25f;
            Broadcast(world);
            return;
        }

        // Новые игроки получают настройки и без правок в меню.
        if (Time.time >= nextHeartbeat)
        {
            nextHeartbeat = Time.time + 5f;
            Broadcast(world);
        }

        // Серверный замок: короткий heartbeat, чтобы клиент не мог
        // выключить затемнение дольше ~1.5 с.
        if (ForceForClients && Enabled && Time.time >= nextForce)
        {
            nextForce = Time.time + 1.5f;
            BroadcastForce(world);
        }
    }

    private static void BroadcastForce(World world)
    {
        hostSeq += 1f;

        var players = world.Players.list;
        for (int i = 0; i < players.Count; i++)
        {
            EntityPlayer p = players[i];
            if (p == null || p.Buffs == null) continue;

            Send(p, CvarSeq, hostSeq);
            Send(p, CvarForce, 1f);
            Send(p, CvarEnabled, 1f);
            Send(p, CvarNight, NightFactorValue);
            Send(p, CvarIndoor, IndoorBoost);
            Send(p, CvarTint, NightTint);
            Send(p, CvarMoon, MoonPhases);
            Send(p, CvarWeather, WeatherOn ? 1f : 0f);
            Send(p, CvarEyes, EyeAdapt ? 1f : 0f);
            Send(p, CvarLight, LightRelief ? 1f : 0f);
            Send(p, CvarNvg, NvgRelief ? 1f : 0f);
            Send(p, CvarBiomeOn, BiomeTintOn ? 1f : 0f);
            Send(p, CvarBiomeAmount, BiomeTintValue);
            Send(p, CvarLunar, LunarLink ? 1f : 0f);
        }
    }

    private static void Broadcast(World world)
    {
        hostSeq += 1f;

        var players = world.Players.list;
        for (int i = 0; i < players.Count; i++)
        {
            EntityPlayer p = players[i];
            if (p == null || p.Buffs == null) continue;

            Send(p, CvarSeq, hostSeq);
            Send(p, CvarForce, (ForceForClients && Enabled) ? 1f : 0f);
            Send(p, CvarEnabled, Enabled ? 1f : 0f);
            Send(p, CvarNight, NightFactorValue);
            Send(p, CvarIndoor, IndoorBoost);
            Send(p, CvarTint, NightTint);
            Send(p, CvarMoon, MoonPhases);
            Send(p, CvarWeather, WeatherOn ? 1f : 0f);
            Send(p, CvarEyes, EyeAdapt ? 1f : 0f);
            Send(p, CvarLight, LightRelief ? 1f : 0f);
            Send(p, CvarNvg, NvgRelief ? 1f : 0f);
            Send(p, CvarBiomeOn, BiomeTintOn ? 1f : 0f);
            Send(p, CvarBiomeAmount, BiomeTintValue);
            Send(p, CvarLunar, LunarLink ? 1f : 0f);
        }
    }

    private static void Send(EntityPlayer player, string name, float value)
    {
        player.Buffs.SetCustomVar(name, value, false, CVarOperation.set, false);
        player.Buffs.SetCustomVarNetwork(name, value, CVarOperation.set);
    }
}

// Локализация меню: английский по умолчанию, русский при русском языке игры.
internal static class RustText
{
    private static bool? isRussian;

    public static bool Russian
    {
        get
        {
            if (!isRussian.HasValue)
            {
                isRussian = DetectRussian();
            }
            return isRussian.Value;
        }
    }

    public static string T(string en, string ru)
    {
        return Russian ? ru : en;
    }

    private static bool DetectRussian()
    {
        try
        {
            string value = ReadLanguage();
            if (!string.IsNullOrEmpty(value))
            {
                value = value.Trim().ToLowerInvariant();
                if (value.StartsWith("ru") || value.Contains("russ")) return true;
                if (value.StartsWith("en") || value.Contains("english")) return false;
                return false;
            }
        }
        catch
        {
        }

        try
        {
            return Application.systemLanguage == SystemLanguage.Russian;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadLanguage()
    {
        try
        {
            Type loc = FindType("Localization");
            if (loc != null)
            {
                PropertyInfo prop = loc.GetProperty("ActiveLanguage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null)
                {
                    string s = prop.GetValue(null, null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }

                FieldInfo field = loc.GetField("ActiveLanguage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    string s = field.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }

                MethodInfo method = loc.GetMethod("GetCurrentGameLanguage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method != null && method.GetParameters().Length == 0)
                {
                    string s = method.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }

            Type prefs = FindType("GamePrefs");
            Type prefEnum = FindType("EnumGamePrefs");
            if (prefs != null && prefEnum != null)
            {
                object key = null;
                try { key = Enum.Parse(prefEnum, "OptionsLanguage"); }
                catch { }

                if (key != null)
                {
                    MethodInfo getter = prefs.GetMethod("GetString",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { prefEnum }, null);
                    if (getter != null)
                    {
                        string s = getter.Invoke(null, new object[] { key }) as string;
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static Type FindType(string name)
    {
        Type t = Type.GetType(name + ", Assembly-CSharp");
        if (t != null) return t;

        try
        {
            Assembly asm = Assembly.GetAssembly(typeof(GameManager));
            if (asm != null) return asm.GetType(name);
        }
        catch
        {
        }

        return null;
    }
}

internal static class RustLighting
{
    private const float DuskStart = 20f;  // 20:00
    private const float NightStart = 22f; // 22:00
    private const float DawnStart = 4f;   // 04:00
    private const float DayStart = 6f;    // 06:00

    private static float demoUntil;

    // Сглаженные настройки: пресет меняется не щелчком, а за ~1 секунду.
    private static float smoothNight = -1f;
    private static float smoothIndoor = -1f;
    private static float smoothTint = -1f;
    private static float smoothMoon = -1f;
    private static float smoothMoonMul = -1f;
    private static float smoothBiome = -1f;
    private static float smoothWeather = -1f;

    private static int updateFrame = -1;
    private static float factorTarget = 1f;
    private static float factorCurrent = 1f;
    private static bool initialized;
    private static World lastWorld;

    // Кэш фазы LunarEvents, чтобы не дёргать баффы каждый кадр.
    private static int lunarPhase = -1;
    private static float nextLunarCheck;

    public static void ForceNight(float seconds)
    {
        demoUntil = Time.time + seconds;
    }

    public static bool DemoActive
    {
        get { return Time.time < demoUntil; }
    }

    public static float DemoRemaining
    {
        get { return Mathf.Max(0f, demoUntil - Time.time); }
    }

    // Часы мира: 0..24.
    public static float GetClock()
    {
        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return -1f;

        ulong time = world.GetWorldTime();
        int hours = GameUtils.WorldTimeToHours(time);
        int minutes = GameUtils.WorldTimeToMinutes(time);
        return hours + minutes / 60f;
    }

    // Глубина ночи 0..1 — для холодного оттенка.
    public static float NightDepth()
    {
        // Демо-ночь днём: часы показывают день, но по свету это ночь.
        if (DemoActive) return 1f;

        float clock = GetClock();
        if (clock < 0f) return 0f;

        if (clock >= DuskStart && clock < NightStart)
            return Mathf.InverseLerp(DuskStart, NightStart, clock);
        if (clock >= NightStart || clock < DawnStart) return 1f;
        if (clock >= DawnStart && clock < DayStart)
            return 1f - Mathf.InverseLerp(DawnStart, DayStart, clock);
        return 0f;
    }

    private static EntityPlayerLocal LocalPlayer
    {
        get
        {
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            return world != null ? world.GetPrimaryPlayer() : null;
        }
    }

    // Один расчёт на кадр: сглаживаем настройки, считаем цель,
    // затем адаптируем «глаза» (медленнее в темноту, быстрее в свет).
    private static void UpdateSmooth()
    {
        if (initialized && Time.frameCount == updateFrame) return;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (!ReferenceEquals(world, lastWorld))
        {
            lastWorld = world;
            initialized = false;
            lunarPhase = -1;
        }

        updateFrame = Time.frameCount;
        bool first = !initialized;

        float k = first ? 1f : Mathf.Clamp01(Time.deltaTime * 1.2f);
        smoothNight = Mathf.Lerp(smoothNight, RustSettings.NightFactorValue, k);
        smoothIndoor = Mathf.Lerp(smoothIndoor, RustSettings.IndoorBoost, k);
        smoothTint = Mathf.Lerp(smoothTint, RustSettings.NightTint, k);
        smoothMoon = Mathf.Lerp(smoothMoon, RustSettings.MoonPhases, k);
        smoothBiome = Mathf.Lerp(smoothBiome,
            RustSettings.BiomeTintOn ? RustSettings.BiomeTintValue : 0f, k);

        // Фаза LunarEvents приходит с ночным баффом и может смениться
        // одним шагом. Сглаживаем сам множитель, а не только ползунок.
        float moonTarget = MoonMul(smoothMoon);
        smoothMoonMul = first ? moonTarget
            : Mathf.Lerp(smoothMoonMul, moonTarget, Mathf.Clamp01(Time.deltaTime * 1.2f));

        LightRelief.Tick(LocalPlayer);

        float weather = WeatherMul();
        smoothWeather = first ? weather
            : Mathf.Lerp(smoothWeather, weather, Mathf.Clamp01(Time.deltaTime / 3f));

        factorTarget = ComputeTarget();

        if (first || !RustSettings.EyeAdapt)
        {
            factorCurrent = factorTarget;
        }
        else
        {
            // В темноту глаза привыкают 2.4 с, к свету — 1.1 с.
            // Экспоненциальное приближение, а не линейное: линейный шаг
            // съедал почти всю видимую разницу за первые 0.4 с, и переход
            // читался как мгновенное переключение.
            float seconds = factorTarget < factorCurrent ? 2.4f : 1.1f;
            float blend = 1f - Mathf.Exp(-Time.deltaTime / seconds);
            factorCurrent = Mathf.Lerp(factorCurrent, factorTarget, blend);
            if (Mathf.Abs(factorCurrent - factorTarget) < 0.002f)
                factorCurrent = factorTarget;
        }

        initialized = true;
    }

    public static float GetFactor()
    {
        UpdateSmooth();
        return factorCurrent;
    }

    // Целевая яркость: расписание + погода + луна + помещение + ПНВ/свет.
    private static float ComputeTarget()
    {
        float factor = ComputeScheduleFactor();
        if (factor >= 0.999f) return factor;

        // Под крышей подмешиваем «домашнюю» яркость.
        float shelter = NightIndoor.Level;
        if (shelter > 0.01f)
        {
            float indoor = Mathf.Lerp(factor, 1f, smoothIndoor);
            float cap = Mathf.Max(factor, 0.5f);
            indoor = Mathf.Min(indoor, cap);
            factor = Mathf.Lerp(factor, indoor, shelter);
        }

        // ПНВ и локальный свет: не даём тьме съесть спасательный свет.
        float relief = LightRelief.Floor;
        if (relief > factor) factor = relief;

        return Mathf.Clamp(factor, 0.02f, 1f);
    }

    private static float ComputeScheduleFactor()
    {
        if (!RustSettings.Enabled) return 1f;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 1f;

        float night = smoothNight * smoothMoonMul * smoothWeather;
        night = Mathf.Clamp(night, 0.02f, 0.85f);

        if (Time.time < demoUntil) return night;

        float clock = GetClock();

        if (clock >= DuskStart && clock < NightStart)
        {
            float t = Mathf.InverseLerp(DuskStart, NightStart, clock);
            return Mathf.Lerp(1f, night, t);
        }
        if (clock >= NightStart || clock < DawnStart) return night;
        if (clock >= DawnStart && clock < DayStart)
        {
            float t = Mathf.InverseLerp(DawnStart, DayStart, clock);
            return Mathf.Lerp(night, 1f, t);
        }
        return 1f;
    }

    // Для меню/диагностики: расписание без помещения и адаптации.
    public static float GetBaseFactor()
    {
        UpdateSmooth();
        return factorTarget;
    }

    // Погода: облака и осадки только ослабляют луну, саму погоду не меняем.
    private static float WeatherMul()
    {
        if (!RustSettings.WeatherOn) return 1f;

        try
        {
            WeatherManager wm = WeatherManager.Instance;
            if (wm == null) return 1f;

            float cloud = Mathf.Clamp01(wm.GetCurrentCloudThicknessPercent());
            float rain = Mathf.Clamp01(wm.GetCurrentRainfallPercent());
            float snow = Mathf.Clamp01(wm.GetCurrentSnowfallPercent());
            float precip = Mathf.Max(rain, snow);

            // Ясно — 1.0, плотная облачность — ~0.73, ливень/гроза — 0.55.
            float dark = Mathf.Clamp01(cloud * 0.60f + precip * 0.50f);
            return Mathf.Lerp(1f, 0.55f, dark);
        }
        catch
        {
            return 1f;
        }
    }

    public static string WeatherName()
    {
        if (!RustSettings.WeatherOn) return RustText.T("off", "выкл");

        try
        {
            WeatherManager wm = WeatherManager.Instance;
            if (wm == null) return "—";

            float cloud = Mathf.Clamp01(wm.GetCurrentCloudThicknessPercent());
            float rain = Mathf.Clamp01(wm.GetCurrentRainfallPercent());
            float snow = Mathf.Clamp01(wm.GetCurrentSnowfallPercent());

            if (rain > 0.35f) return RustText.T("rain", "дождь");
            if (snow > 0.35f) return RustText.T("snow", "снег");
            if (cloud > 0.75f) return RustText.T("overcast", "пасмурно");
            if (cloud > 0.45f) return RustText.T("cloudy", "облачно");
            return RustText.T("clear", "ясно");
        }
        catch
        {
            return "—";
        }
    }

    // Фаза луны: сначала LunarEvents, если он есть, потом day % 8.
    public static int MoonPhase()
    {
        if (RustSettings.LunarLink)
        {
            if (Time.time >= nextLunarCheck)
            {
                nextLunarCheck = Time.time + 0.3f;
                lunarPhase = LunarEventsPhase(LocalPlayer);
            }
            if (lunarPhase >= 0) return lunarPhase;
        }

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 0;

        int days = GameUtils.WorldTimeToDays(world.GetWorldTime());
        return ((days % 8) + 8) % 8;
    }

    // Ночные базовые баффы LunarEvents = индекс фазы 0..7.
    private static readonly string[] LunarPhaseBuffs =
    {
        "lunar_xp_up",
        "lunar_speed_up",
        "lunar_damage_up",
        "lunar_crit_up",
        "lunar_xp_down",
        "lunar_speed_down",
        "lunar_damage_down",
        "lunar_crit_down",
    };

    private static int LunarEventsPhase(EntityPlayerLocal player)
    {
        if (player == null || player.Buffs == null) return -1;
        for (int i = 0; i < LunarPhaseBuffs.Length; i++)
        {
            if (player.Buffs.HasBuff(LunarPhaseBuffs[i])) return i;
        }
        return -1;
    }

    // Множитель ночной яркости: полнолуние заметно светлее, новолуние темнее.
    public static float MoonMul()
    {
        return MoonMul(RustSettings.MoonPhases);
    }

    private static float MoonMul(float strength)
    {
        if (strength <= 0.01f) return 1f;

        float c = Mathf.Cos(MoonPhase() / 8f * Mathf.PI * 2f);
        return Mathf.Max(0.25f, 1f + c * strength * 1.5f);
    }

    public static string MoonMulText()
    {
        return "×" + MoonMul().ToString("0.00",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string MoonName()
    {
        int p = MoonPhase();
        if (p == 0) return RustText.T("full", "полная");
        if (p == 4) return RustText.T("new", "новая");
        return p < 4 ? RustText.T("waning", "убывает") : RustText.T("waxing", "растёт");
    }

    // Название текущего режима для меню.
    public static string ModeName()
    {
        if (!RustSettings.Enabled) return RustText.T("off", "выключено");
        if (DemoActive) return RustText.T("demo night", "демо-ночь");

        float clock = GetClock();
        if (clock < 0f) return "—";

        if (clock >= DuskStart && clock < NightStart) return RustText.T("dusk", "закат");
        if (clock >= NightStart || clock < DawnStart) return RustText.T("night", "ночь");
        if (clock >= DawnStart && clock < DayStart) return RustText.T("dawn", "рассвет");
        return RustText.T("day", "день");
    }

    // Сколько игрового времени осталось до 06:00 (только ночью/на рассвете).
    public static string DawnText()
    {
        float clock = GetClock();
        if (clock < 0f) return null;

        // Только ночь и рассвет: 22:00–06:00. Днём и на закате не показываем.
        if (!(clock >= NightStart || clock < DayStart)) return null;

        float remain = clock >= NightStart ? 24f - clock + DayStart : DayStart - clock;
        if (remain < 0f) remain = 0f;

        int h = Mathf.FloorToInt(remain);
        int m = Mathf.FloorToInt((remain - h) * 60f);
        return string.Format("{0:00}:{1:00}", h, m);
    }

    // ===== Биомный цветокор: палитра один в один как в Silent Fog =====

    public static bool BiomePalette(out Color color)
    {
        color = Color.white;

        EntityPlayerLocal player = LocalPlayer;
        if (player == null || player.biomeStandingOn == null) return false;

        string name = player.biomeStandingOn.m_sBiomeName;
        if (string.IsNullOrEmpty(name)) return false;

        switch (name.ToLowerInvariant())
        {
            case "snow":
                color = new Color(0.62f, 0.78f, 1.00f, 1f);
                return true;
            case "desert":
                color = new Color(1.00f, 0.62f, 0.34f, 1f);
                return true;
            case "radiated":
                color = new Color(0.62f, 0.90f, 0.26f, 1f);
                return true;
            case "wasteland":
            case "wasteland_hub":
            case "city_wasteland":
                color = new Color(0.86f, 0.58f, 0.28f, 1f);
                return true;
            case "burnt_forest":
                color = new Color(0.52f, 0.46f, 0.38f, 1f);
                return true;
            default:
                return false; // лес, равнины, город — не трогаем
        }
    }

    public static string BiomeName()
    {
        EntityPlayerLocal player = LocalPlayer;
        if (player == null || player.biomeStandingOn == null) return "—";

        string name = player.biomeStandingOn.m_sBiomeName;
        if (string.IsNullOrEmpty(name)) return "—";

        switch (name.ToLowerInvariant())
        {
            case "snow": return RustText.T("snow", "снег");
            case "desert": return RustText.T("desert", "пустыня");
            case "radiated": return RustText.T("radiated", "радиация");
            case "wasteland":
            case "wasteland_hub":
            case "city_wasteland": return RustText.T("wasteland", "пустошь");
            case "burnt_forest": return RustText.T("burnt", "горелый лес");
            case "pine_forest": return RustText.T("pine forest", "сосновый лес");
            case "forest": return RustText.T("forest", "лес");
            case "plains": return RustText.T("plains", "равнины");
            case "city": return RustText.T("city", "город");
            default: return name;
        }
    }

    // Подгоняем палитру под яркость текущего света: меняется оттенок,
    // а не яркость. Так туман и свет в биоме совпадают по цвету.
    public static Color KeepLuma(Color palette, Color source)
    {
        float src = Luma(source);
        float dst = Luma(palette);
        if (dst < 0.001f) return palette;

        Color scaled = palette * (src / dst);
        scaled.a = 1f;
        return scaled;
    }

    public static float Luma(Color c)
    {
        return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }

    public static Color ColdTint()
    {
        return new Color(0.60f, 0.76f, 1.00f, 1f);
    }

    public static float TintAmount()
    {
        return Mathf.Clamp01(NightDepth() * smoothTint);
    }

    public static float BiomeAmount()
    {
        return Mathf.Clamp01(NightDepth() * smoothBiome);
    }
}

// «Мы под крышей» — своя проверка, без зависимости от Silent Fog.
// 5 точек: центр и четыре смещения по 0.35 м, блоки на 2..8 м над ногами.
// Если над самим игроком открытое небо — считаем, что мы на улице.
internal static class NightIndoor
{
    private static int sampleFrame = -1;
    private static float raw;
    private static float smooth = -1f;
    private static int smoothFrame = -1;
    private static float nextSample;
    private static readonly bool[] covered = new bool[5];

    public static float Level
    {
        get
        {
            if (Time.frameCount != sampleFrame)
            {
                sampleFrame = Time.frameCount;
                if (Time.time >= nextSample)
                {
                    nextSample = Time.time + 0.2f;
                    raw = Sample();
                }
            }

            if (Time.frameCount == smoothFrame) return smooth;
            smoothFrame = Time.frameCount;

            if (smooth < 0f) smooth = raw;
            else smooth = Mathf.Lerp(smooth, raw, Mathf.Clamp01(Time.deltaTime * 3f));

            return smooth;
        }
    }

    private static float Sample()
    {
        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
        if (player == null) return 0f;

        // Сейф-зона торговца — тоже «внутри», даже если здание дырявое.
        try
        {
            if (world.IsWithinTraderArea(World.worldToBlockPos(player.position))) return 1f;
        }
        catch
        {
        }

        Vector3 pos = player.position;
        for (int i = 0; i < covered.Length; i++) covered[i] = false;
        int hits = 0;

        for (int i = 0; i < 5; i++)
        {
            float ox = 0f;
            float oz = 0f;
            if (i == 1) ox = 0.35f;
            else if (i == 2) ox = -0.35f;
            else if (i == 3) oz = 0.35f;
            else if (i == 4) oz = -0.35f;

            Vector3i p = World.worldToBlockPos(
                new Vector3(pos.x + ox, pos.y + 0.2f, pos.z + oz));

            for (int dy = 2; dy <= 8; dy++)
            {
                BlockValue bv = world.GetBlock(p.x, p.y + dy, p.z);
                if (bv.isair || bv.Block == null) continue;
                if (bv.Block is BlockPlant) continue;   // крона/куст — не крыша

                covered[i] = true;
                hits++;
                break;
            }
        }

        if (!covered[0]) return 0f;

        // Крыша без стен — это навес, балкон или лес, а не помещение.
        // Раньше проверялись только лучи вверх, и «ярче в помещениях»
        // работало на улице под любой кроной.
        Vector3i eye = World.worldToBlockPos(pos + new Vector3(0f, 1.2f, 0f));
        int walls = Walls(world, eye);
        if (walls <= 1) return 0f;

        float enclosure = walls == 2 ? 0.5f : (walls == 3 ? 0.8f : 1f);
        return (hits / 5f) * enclosure;
    }

    // Сколько из четырёх сторон (С/Ю/З/В) перекрыто твёрдым блоком
    // в пределах 10 блоков на уровне глаз.
    private static int Walls(World world, Vector3i p)
    {
        int n = 0;

        for (int d = 0; d < 4; d++)
        {
            int dx = d == 0 ? 1 : (d == 1 ? -1 : 0);
            int dz = d == 2 ? 1 : (d == 3 ? -1 : 0);

            for (int r = 1; r <= 10; r++)
            {
                BlockValue bv = world.GetBlock(p.x + dx * r, p.y, p.z + dz * r);
                if (bv.isair || bv.Block == null) continue;
                if (bv.Block is BlockPlant) continue;

                n++;
                break;
            }
        }

        return n;
    }
}

// ПНВ, фонарь, костёр/факел рядом. Без жёстких ссылок на другие моды.
internal static class LightRelief
{
    private static int frame = -1;
    private static float nextScan;
    private static bool nvg;
    private static bool held;
    private static bool nearby;

    // Насколько поднять пол тьмы. ПНВ — сильнее, локальный свет — мягче.
    public static float Floor
    {
        get
        {
            if (nvg) return 0.35f;
            if (held || nearby) return 0.14f;
            return 0f;
        }
    }

    public static bool NvgActive { get { return nvg; } }
    public static bool HeldLight { get { return held; } }

    public static void Tick(EntityPlayerLocal player)
    {
        if (Time.frameCount == frame) return;
        frame = Time.frameCount;

        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.35f;

        nvg = false;
        held = false;
        nearby = false;

        if (player == null) return;

        try
        {
            nvg = HasNvg(player);
            held = HasHeldLight(player);
            nearby = HasNearbyLight(player);
        }
        catch
        {
        }
    }

    private static bool HasNvg(EntityPlayerLocal player)
    {
        if (!RustSettings.NvgRelief) return false;

        if (player.Buffs != null)
        {
            List<BuffValue> buffs = player.Buffs.ActiveBuffs;
            if (buffs != null)
            {
                for (int i = 0; i < buffs.Count; i++)
                {
                    BuffValue bv = buffs[i];
                    if (bv == null) continue;
                    if (NameLooksLikeNvg(bv.BuffName)) return true;
                }
            }
        }

        // Некоторые моды делают ПНВ предметом в руке или в экипировке.
        ItemClass held = HeldItem(player);
        if (held != null && NameLooksLikeNvg(held.Name)) return true;

        try
        {
            if (player.equipment != null)
            {
                int count = player.equipment.GetSlotCount();
                for (int i = 0; i < count; i++)
                {
                    ItemValue iv = player.equipment.GetSlotItem(i);
                    if (iv == null || iv.ItemClass == null) continue;
                    if (NameLooksLikeNvg(iv.ItemClass.Name)) return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool NameLooksLikeNvg(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        name = name.ToLowerInvariant();
        return name.Contains("nightvision") || name.Contains("night_vision")
            || name.Contains("nvg") || name.Contains("goggles");
    }

    private static ItemClass HeldItem(EntityPlayerLocal player)
    {
        if (player.inventory == null) return null;
        return player.inventory.holdingItem;
    }

    private static bool HasHeldLight(EntityPlayerLocal player)
    {
        if (!RustSettings.LightRelief) return false;
        if (player.inventory == null) return false;

        ItemClass item = player.inventory.holdingItem;
        if (item == null) return false;

        try
        {
            if (item.IsLightSource()) return true;
        }
        catch
        {
        }

        string name = item.Name;
        if (string.IsNullOrEmpty(name)) return false;
        name = name.ToLowerInvariant();
        return name.Contains("flashlight") || name.Contains("torch")
            || name.Contains("lantern");
    }

    private static bool HasNearbyLight(EntityPlayerLocal player)
    {
        if (!RustSettings.LightRelief) return false;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return false;

        Vector3i c = World.worldToBlockPos(player.position);

        // Грубая сетка 5x4x5 вокруг игрока: костёр, факел, фонарь, лампа.
        for (int dx = -4; dx <= 4; dx += 2)
        {
            for (int dz = -4; dz <= 4; dz += 2)
            {
                for (int dy = -1; dy <= 2; dy++)
                {
                    Vector3i p = new Vector3i(c.x + dx, c.y + dy, c.z + dz);
                    BlockValue bv = world.GetBlock(p.x, p.y, p.z);
                    if (bv.isair || bv.Block == null) continue;

                    try
                    {
                        if (bv.Block.GetLightValue(bv) > 0) return true;
                    }
                    catch
                    {
                    }

                    string name = bv.Block.GetBlockName();
                    if (string.IsNullOrEmpty(name)) continue;
                    name = name.ToLowerInvariant();
                    if (name.Contains("lightoak")) continue; // «светлый дуб», не свет
                    if (name.Contains("nolight")) continue;  // выключенная вывеска
                    if (name.Contains("campfire") || name.Contains("forge")
                        || name.Contains("fireplace")
                        || name.Contains("torch") || name.Contains("lantern")
                        || name.Contains("candle") || name.Contains("lamp")
                        || name.Contains("light") || name.Contains("ember"))
                        return true;
                }
            }
        }

        return false;
    }
}

// Ночная яркость луны/ambient.
[HarmonyPatch(typeof(SkyManager), "GetMoonAmbientScale")]
internal static class MoonLightPatch
{
    private static void Postfix(ref float __result)
    {
        // AmbientLightPatch уже умножает итоговые цвета на factor.
        // Здесь берём sqrt, чтобы ночь не затемнялась дважды (factor * factor).
        __result *= Mathf.Sqrt(RustLighting.GetFactor());
    }
}

// Солнце/луна: на 03:00 ambient уже нулевой, поэтому затемняем сам
// направленный свет. Без этого ночь остаётся лунно-серой.
[HarmonyPatch(typeof(SkyManager), "SetSunIntensity")]
internal static class SunIntensityPatch
{
    private static void Prefix(ref float __0)
    {
        float factor = RustLighting.GetFactor();
        if (factor < 0.999f) __0 *= factor;
    }
}

[HarmonyPatch(typeof(SkyManager), "UpdateSunMoonAngles")]
internal static class MoonIntensityPatch
{
    private static float lastIntensity;
    private static bool hasLast;

    private static void Postfix()
    {
        if (SkyManager.moonLight == null) return;

        // Ваниль каждый вызов переписывает intensity и color. Если значение
        // не изменилось с прошлого нашего применения, значит метод вышел
        // раньше — повторно умножать нельзя, иначе свет накопится.
        float incoming = SkyManager.moonLight.intensity;
        if (hasLast && Mathf.Approximately(incoming, lastIntensity)) return;

        float factor = RustLighting.GetFactor();
        if (factor >= 0.999f)
        {
            hasLast = false;
            return;
        }

        SkyManager.moonLight.intensity *= factor;

        float tint = RustLighting.TintAmount();
        if (tint > 0.01f)
        {
            SkyManager.moonLight.color = Color.Lerp(
                SkyManager.moonLight.color, RustLighting.ColdTint(), tint * 0.85f);
        }

        // Биомный оттенок: та же палитра, что у Silent Fog.
        Color palette;
        float biome = RustLighting.BiomeAmount();
        if (biome > 0.01f && RustLighting.BiomePalette(out palette))
        {
            Color nightPalette = RustLighting.KeepLuma(
                palette, SkyManager.moonLight.color);
            SkyManager.moonLight.color = Color.Lerp(
                SkyManager.moonLight.color, nightPalette, biome * 0.85f);
        }

        lastIntensity = SkyManager.moonLight.intensity;
        hasLast = true;
    }
}

// Основное затемнение ambient-цветов. Работает поверх ванильного расчёта.
[HarmonyPatch(typeof(WorldEnvironment), "AmbientSpectrumFrameUpdate")]
internal static class AmbientLightPatch
{
    private static AmbientMode originalMode;
    private static bool hasOriginalMode;

    private static void Postfix()
    {
        GameManager gm = GameManager.Instance;
        EntityPlayerLocal player = gm != null && gm.World != null
            ? gm.World.GetPrimaryPlayer()
            : null;

        RustSettings.ReadFromLocalPlayer(player);
        RustSettings.Tick();

        // Сырые ванильные цвета — до нашего затемнения. Нужны и для пола,
        // и для диагностики в меню.
        Color rawSky = RenderSettings.ambientSkyColor;
        Color rawEquator = RenderSettings.ambientEquatorColor;
        Color rawGround = RenderSettings.ambientGroundColor;
        Color rawLight = RenderSettings.ambientLight;
        AmbientProbe.Set(rawSky, rawEquator, rawGround);

        float factor = RustLighting.GetFactor();

        if (factor >= 0.999f)
        {
            if (hasOriginalMode)
            {
                RenderSettings.ambientMode = originalMode;
                hasOriginalMode = false;
            }
            return;
        }

        if (!hasOriginalMode)
        {
            originalMode = RenderSettings.ambientMode;
            hasOriginalMode = true;
        }

        // Если сцена была в Skybox-режиме, цвета ambient игнорируются.
        // На ночь принудительно включаем Trilight, чтобы затемнение работало.
        RenderSettings.ambientMode = AmbientMode.Trilight;

        // Пол: каждый канал не ниже доли самого яркого канала сцены,
        // умноженной на тот же factor. Так канал, обнулённый чужим
        // конфигом (ProjectZ ставил ambientGroundScale = .2,0), всё равно
        // следует за ползунком, но остаётся темнее неба.
        float peak = Mathf.Max(Peak(rawSky),
            Mathf.Max(Peak(rawEquator), Peak(rawGround)));
        float lift = peak * factor * FloorShare;

        RenderSettings.ambientSkyColor = Darken(rawSky, factor, lift);
        RenderSettings.ambientEquatorColor = Darken(rawEquator, factor, lift);
        RenderSettings.ambientGroundColor = Darken(rawGround, factor, lift);
        RenderSettings.ambientLight = Darken(rawLight, factor, lift);

        // Ночной оттенок: чем глубже ночь, тем холоднее свет.
        float tintAmount = RustLighting.TintAmount();
        float biome = RustLighting.BiomeAmount();

        if (tintAmount > 0.01f || biome > 0.01f)
        {
            // При сильной тьме цвета уже почти нулевые — добавляем подсветку
            // абсолютной величиной. Холод и биом считаем раздельно, чтобы
            // биомный оттенок работал и при выключенном холодном.
            Color floor = new Color(0.010f, 0.016f, 0.030f, 0f) * tintAmount;

            Color palette;
            if (biome > 0.01f && RustLighting.BiomePalette(out palette))
            {
                // Та же яркость, но оттенок биома — как у тумана Silent Fog.
                float l = RustLighting.Luma(palette);
                if (l > 0.001f)
                {
                    Color biomeHue = palette * (0.020f / l) * biome;
                    // Биом главнее холодного оттенка, но не выключает его совсем.
                    floor = Color.Lerp(floor, biomeHue, 0.75f);
                }
            }

            floor.a = 0f;
            RenderSettings.ambientLight += floor;
            RenderSettings.ambientSkyColor += floor * 1.4f;
            RenderSettings.ambientEquatorColor += floor;
        }
    }

    // Доля самого яркого канала, ниже которой канал не опускается.
    // 0.25 — земля остаётся заметно темнее неба, но реагирует на ползунок.
    private const float FloorShare = 0.25f;

    private static float Peak(Color c)
    {
        return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }

    private static Color Darken(Color raw, float factor, float lift)
    {
        Color c = raw * factor;
        c.r = Mathf.Max(c.r, lift);
        c.g = Mathf.Max(c.g, lift);
        c.b = Mathf.Max(c.b, lift);
        c.a = raw.a;
        return c;
    }
}

// Сырые ванильные каналы ambient — для диагностики в меню.
internal static class AmbientProbe
{
    public static float Sky;
    public static float Equator;
    public static float Ground;

    public static void Set(Color sky, Color equator, Color ground)
    {
        Sky = Mathf.Max(sky.r, Mathf.Max(sky.g, sky.b));
        Equator = Mathf.Max(equator.r, Mathf.Max(equator.g, equator.b));
        Ground = Mathf.Max(ground.r, Mathf.Max(ground.g, ground.b));
    }
}

// Пока открыто меню DifRUST, не пускаем ввод в игрока.
[HarmonyPatch(typeof(PlayerMoveController), "Update")]
internal static class DifInputBlockPatch
{
    private static bool Prefix()
    {
        if (!DifMenuBehaviour.IsOpen) return true;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.World == null)
        {
            DifMenuBehaviour.ResetState();
            return true;
        }

        return false;
    }
}

// Другой мод, который тоже затемняет ночь: предупреждаем в меню.
internal static class RustConflicts
{
    private static string name;
    private static float nextCheck;

    public static string Name
    {
        get
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 5f;
                name = Detect();
            }
            return name;
        }
    }

    private static string Detect()
    {
        try
        {
            string root = ModManager.ModsBasePath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

            string[] dirs = Directory.GetDirectories(root);
            for (int i = 0; i < dirs.Length; i++)
            {
                string folder = Path.GetFileName(dirs[i]);
                if (string.IsNullOrEmpty(folder)) continue;

                string n = folder.ToLowerInvariant();
                if (n.Contains("difrust") || n.StartsWith("0_tfp")) continue;

                if (n.Contains("rustnight") || n.Contains("darknight")
                    || n.Contains("nightmod") || n.Contains("nightclient")
                    || n.Contains("nightterror") || n.Contains("darker")
                    || n.Contains("darkness"))
                    return folder;
            }
        }
        catch
        {
        }

        return null;
    }
}

internal static class DifMenu
{    public static void Create()
    {
        try
        {
            GameObject go = new GameObject("DifRUSTMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<DifMenuBehaviour>();
        }
        catch (Exception e)
        {
            Debug.Log("[DifRUST] menu create failed: " + e.Message);
        }
    }
}

// Ночное небо 1.6.0: кубмапа Млечного Пути + своя луна.
// Луна — мягкая связка с LunarEvents: без него остаётся ванильная,
// небо при этом работает само по себе.
internal static class DifSky
{
    // Ванильная яркость кубмапы; наша калибруется этим же числом.
    public const float BaseStrength = 0.32f;

    // Вторая луна: та же сфера, что и ванильная, но чуть меньше и по своей
    // орбите — выше и в стороне, чтобы не сливаться с ванильной.
    private const float MoonDistance = 45000f;
    private const float SecondMoonScale = 0.58f;
    private const float SecondMoonLift = 0.50f;    // ~28,6° к зениту
    private const float SecondMoonAzimuth = 0.35f; // ~20° по азимуту

    public static Cubemap Cube;
    public static Texture2D Moon;

    public static bool LunarEventsPresent { get; private set; }
    public static bool LunarApiAvailable { get { return lunarSpecial != null; } }
    public static string SecondMoonSpecial { get; private set; }
    public static bool SecondMoonActive
    {
        get { return !string.IsNullOrEmpty(SecondMoonSpecial); }
    }
    public static bool CubeReady { get { return Cube != null; } }
    public static bool SkyActive { get { return appliedSky; } }
    public static bool SkyYielded { get { return yieldedSky; } }

    private static Material atmo;
    private static Texture origCube;
    private static float origStrength;
    private static bool haveCube;
    private static bool appliedSky;
    private static bool yieldedSky;

    private static bool lunarChecked;
    private static float nextLunarCheck;
    private static MethodInfo lunarSpecial;
    private static bool lunarApiWarned;

    private static Transform moonSource;
    private static Transform secondMoon;
    private static Renderer secondRenderer;
    private static Material secondMoonMat;
    private static int moonNightDay = int.MinValue;
    private static string moonSpecialLogged;
    private static float nextMoonNightCheck;

    public static void Create()
    {
        try
        {
            GameObject go = new GameObject("DifRUSTSky");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<DifSkyBehaviour>();
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] create failed: " + e);
        }
    }

    public static void Tick()
    {
        try
        {
            if (!lunarChecked || Time.unscaledTime >= nextLunarCheck)
            {
                nextLunarCheck = Time.unscaledTime + 5f;
                lunarChecked = true;
                LunarEventsPresent = DetectLunarEvents();

                // LunarEvents мог загрузиться позже нас: ищем API, пока нет.
                if (lunarSpecial == null) DetectLunarApi();
            }

            TickCube();
            TickSecondMoon();
            DifSkyLayers.Tick();
            DifSkyMesh.Tick();
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] tick failed: " + e);
            Restore();
        }
    }

    private static void TickCube()
    {
        Material m = SkyManager.atmosphereMtrl;
        if (m == null) return;

        if (!ReferenceEquals(m, atmo))
        {
            atmo = m;
            origCube = m.GetTexture("_OuterSpaceCube");
            origStrength = m.GetFloat("_StarStrength");
            haveCube = true;
            appliedSky = false;
            yieldedSky = false;
        }
        if (!haveCube) return;

        Texture cur = m.GetTexture("_OuterSpaceCube");

        // Чужой мод поставил свою кубмапу — уступаем, не воюем.
        if (cur != null && !ReferenceEquals(cur, Cube)
            && !ReferenceEquals(cur, origCube))
        {
            if (!yieldedSky)
                Debug.Log("[DifRUST][sky] cubemap changed by another mod, yielding");
            yieldedSky = true;
            return;
        }

        bool want = RustSettings.SkyOn && Cube != null && !yieldedSky;

        if (want)
        {
            if (!ReferenceEquals(cur, Cube))
                m.SetTexture("_OuterSpaceCube", Cube);
            m.SetFloat("_StarStrength",
                BaseStrength * RustSettings.SkyAmount);
            appliedSky = true;
        }
        else if (appliedSky)
        {
            m.SetTexture("_OuterSpaceCube", origCube);
            m.SetFloat("_StarStrength", origStrength);
            appliedSky = false;
        }
    }

    // Вторая луна — отдельный клон ванильного спрайта. Ванильную луну
    // не трогаем: своя текстура, свой материал, своя позиция на небе.
    private static void TickSecondMoon()
    {
        if (Time.unscaledTime >= nextMoonNightCheck)
        {
            nextMoonNightCheck = Time.unscaledTime + 5f;
            UpdateSecondMoonNight();
        }

        bool want = RustSettings.SkyOn && RustSettings.SkyMoon && Moon != null;

        if (!want)
        {
            HideSecondMoon();
            return;
        }

        Transform src = SkyManager.moonSpriteT;
        if (src == null)
        {
            HideSecondMoon();
            return;
        }

        EnsureSecondMoon(src);
        if (secondMoon == null || secondRenderer == null) return;

        // Берём настоящий луч от камеры к ванильной луне: forward квада
        // смотрит от центра мира, а не от игрока, и на краю карты врёт.
        Camera cam = SkyManager.mainCamera;
        Vector3 camPos;
        Vector3 dir;

        if (cam != null)
        {
            camPos = cam.transform.position;
            dir = (src.position - camPos).normalized;
        }
        else
        {
            dir = src.forward;
            camPos = src.position - dir * MoonDistance;
        }

        // Вторую луну поднимаем к зениту на 28,6° и уводим на 20° в сторону:
        // она восходит раньше ванильной и никогда с ней не сливается.
        Vector3 up = Vector3.up;
        Vector3 tangent = up - dir * Vector3.Dot(up, dir);
        if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
        tangent.Normalize();

        Vector3 dir2 = (dir * Mathf.Cos(SecondMoonLift)
            + tangent * Mathf.Sin(SecondMoonLift)).normalized;
        dir2 = Quaternion.AngleAxis(SecondMoonAzimuth, up) * dir2;

        if (dir2.y < 0.05f)
        {
            HideSecondMoon();
            return;
        }

        secondMoon.position = camPos + dir2 * MoonDistance;
        secondMoon.rotation = Quaternion.LookRotation(secondMoon.position, Vector3.up);
        secondMoon.localScale = src.localScale * SecondMoonScale;
        if (secondMoonMat != null)
            secondMoonMat.SetColor("_Color", SecondMoonTint());
        secondRenderer.enabled = true;
    }

    private static void EnsureSecondMoon(Transform src)
    {
        if (secondMoon != null && ReferenceEquals(moonSource, src)) return;
        if (SkyManager.moonSpriteMat == null) return;

        DestroySecondMoon();
        moonSource = src;

        try
        {
            GameObject go = UnityEngine.Object.Instantiate(src.gameObject);
            go.name = "DifRUSTSecondMoon";
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(true);
            go.transform.SetParent(src.parent, true);

            secondMoon = go.transform;
            secondRenderer = go.GetComponent<Renderer>();
            if (secondRenderer == null)
            {
                DestroySecondMoon();
                return;
            }

            secondRenderer.shadowCastingMode = ShadowCastingMode.Off;
            secondRenderer.receiveShadows = false;
            secondRenderer.enabled = false;
            // У клона может остаться MaterialPropertyBlock от ванильного
            // рендерера — он перебил бы наш _ColorMap/_LightDir.
            secondRenderer.SetPropertyBlock(null);

            secondMoonMat = new Material(SkyManager.moonSpriteMat);
            secondMoonMat.hideFlags = HideFlags.HideAndDontSave;
            secondMoonMat.SetTexture("_ColorMap", Moon);
            // Шейдер луны рисует фазу через _LightDir/_Color. Копия не
            // получает обновления от SkyManager, поэтому фиксируем полную
            // фазу: вторая луна всегда золотая, а не «чёрная дыра».
            secondMoonMat.SetVector("_LightDir", new Vector3(0f, 0f, 1f));
            secondMoonMat.SetColor("_Color", Color.white);
            secondRenderer.sharedMaterial = secondMoonMat;

            Debug.Log("[DifRUST][sky] second moon created (full phase)");
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] second moon failed: " + e);
            DestroySecondMoon();
        }
    }

    private static void HideSecondMoon()
    {
        if (secondRenderer != null) secondRenderer.enabled = false;
    }

    private static void UpdateSecondMoonNight()
    {
        string special = null;

        try
        {
            World w = GameManager.Instance != null
                ? GameManager.Instance.World : null;

            if (w != null && lunarSpecial != null)
            {
                float clock = RustLighting.GetClock();
                int day = GameUtils.WorldTimeToDays(w.GetWorldTime());
                int nightDay = clock >= 0f && clock < 4f ? day - 1 : day;

                object res = lunarSpecial.Invoke(
                    null, new object[] { w.Seed, nightDay });
                special = res as string;

                if (nightDay != moonNightDay
                    || !string.Equals(special, moonSpecialLogged))
                {
                    moonNightDay = nightDay;
                    moonSpecialLogged = special;
                    Debug.Log("[DifRUST][sky] second moon night " + nightDay
                        + ": " + (string.IsNullOrEmpty(special)
                            ? "ordinary" : special));
                }
            }
        }
        catch (Exception e)
        {
            if (!lunarApiWarned)
            {
                lunarApiWarned = true;
                Debug.LogWarning("[DifRUST][sky] LunarEvents API call failed: "
                    + e.Message);
            }
        }

        SecondMoonSpecial = string.IsNullOrEmpty(special) ? null : special;
    }

    public static string SpecialMoonName()
    {
        switch (SecondMoonSpecial)
        {
            case "lunar_harvest": return RustText.T("Harvest Moon", "Урожайная луна");
            case "lunar_craft": return RustText.T("Efficient Moon", "Эффективная луна");
            case "lunar_trade": return RustText.T("Golden Moon", "Золотая луна");
            case "lunar_travel": return RustText.T("Traveler's Moon", "Луна путешественника");
            case "lunar_poverty": return RustText.T("Poverty Moon", "Луна бедности");
            case "lunar_lazy": return RustText.T("Lazy Moon", "Ленивая луна");
            case "lunar_wild": return RustText.T("Wild Moon", "Дикая луна");
            case "lunar_fatal": return RustText.T("Fatal Moon", "Фатальная луна");
            default: return SecondMoonSpecial ?? "";
        }
    }

    // Яркость и оттенок второй луны. В обычную ночь — спокойный холодный
    // свет (но всё равно ярче ванильной), в особую — разгорается и теплеет;
    // у каждой особой луны свой характер.
    private static Color SecondMoonTint()
    {
        switch (SecondMoonSpecial)
        {
            case "lunar_harvest": return new Color(1.12f, 1.02f, 0.82f);
            case "lunar_craft": return new Color(1.04f, 1.03f, 0.95f);
            case "lunar_trade": return new Color(1.16f, 1.04f, 0.78f);
            case "lunar_travel": return new Color(0.92f, 0.99f, 1.12f);
            case "lunar_poverty": return new Color(0.82f, 0.82f, 0.86f);
            case "lunar_lazy": return new Color(0.92f, 0.86f, 0.78f);
            case "lunar_wild": return new Color(1.14f, 0.90f, 0.70f);
            case "lunar_fatal": return new Color(1.12f, 0.58f, 0.50f);
            default: return new Color(0.86f, 0.90f, 1.00f);
        }
    }

    // Переключение на слоистый вариант: фоновая кубмапа становится ровной,
    // а сияние и звёзды рисуются отдельными сферами.
    public static void UseLayeredSky()
    {
        if (DifSkyAssets.BaseCube == null) return;

        Cube = DifSkyAssets.BaseCube;
        appliedSky = true;

        Material m = SkyManager.atmosphereMtrl;
        if (m != null)
            m.SetTexture("_OuterSpaceCube", Cube);

        if (DifSkyAssets.FallbackCube != null
            && !ReferenceEquals(DifSkyAssets.FallbackCube, Cube))
        {
            UnityEngine.Object.Destroy(DifSkyAssets.FallbackCube);
            DifSkyAssets.FallbackCube = null;
        }
    }

    public static void Restore()
    {
        try
        {
            if (atmo != null && haveCube)
            {
                atmo.SetTexture("_OuterSpaceCube", origCube);
                atmo.SetFloat("_StarStrength", origStrength);
            }
        }
        catch
        {
        }

        DifSkyLayers.Restore();
        DifSkyAssets.DestroyAssets();
        appliedSky = false;
        DestroySecondMoon();
        DifSkyMesh.Restore();
        Cube = null;
        Moon = null;
    }

    private static void DestroySecondMoon()
    {
        try
        {
            if (secondMoon != null)
                UnityEngine.Object.Destroy(secondMoon.gameObject);
            if (secondMoonMat != null)
                UnityEngine.Object.Destroy(secondMoonMat);
        }
        catch
        {
        }

        secondMoon = null;
        moonSource = null;
        secondRenderer = null;
        secondMoonMat = null;
    }

    private static bool DetectLunarEvents()
    {
        try
        {
            return ModManager.ModLoaded("LunarEvents");
        }
        catch
        {
            return false;
        }
    }

    // Не жёсткая зависимость: ищем публичный LunarEventsApi по имени.
    // Старый LunarEvents без API просто не даёт второй луны.
    private static void DetectLunarApi()
    {
        try
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type t = asms[i].GetType("LunarEventsApi", false);
                if (t == null) continue;

                MethodInfo m = t.GetMethod("SpecialForNight",
                    BindingFlags.Public | BindingFlags.Static);
                if (m == null || m.ReturnType != typeof(string)) continue;

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 2
                    || ps[0].ParameterType != typeof(int)
                    || ps[1].ParameterType != typeof(int)) continue;

                lunarSpecial = m;
                Debug.Log("[DifRUST][sky] LunarEvents API found in "
                    + asms[i].GetName().Name);
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DifRUST][sky] LunarEvents API scan failed: "
                + e.Message);
        }
    }
}

internal static class DifSkyAssets
{
    public static string Root;

    public static Cubemap GlowCube;
    public static Cubemap StarCube;
    public static Cubemap BaseCube;
    public static Cubemap FallbackCube;

    private static readonly string[] Faces =
        { "px", "nx", "py", "ny", "pz", "nz" };

    public static IEnumerator LoadAll()
    {
        if (string.IsNullOrEmpty(Root))
        {
            Debug.Log("[DifRUST][sky] assets path is empty, sky disabled");
            yield break;
        }

        yield return LoadMoon();

        // Runtime-слои на сфере атмосферы конфликтуют с её композитом
        // (ZTest Always / отдельный sky-проход): полоса и звёзды уезжают
        // под горизонт. Пока используем стабильную запечённую кубмапу.
        bool wantLayers = false;
        if (wantLayers)
        {
            yield return LoadLayerCube("glow", 1024);
            yield return LoadLayerCube("star", 2048);

            if (GlowCube != null && StarCube != null)
                BaseCube = CreateBaseCube();
        }

        // Полная кубмапа — запасной вариант и сразу даёт небо.
        yield return LoadCube();

        DifSkyLayers.Ready = wantLayers && GlowCube != null && StarCube != null
            && BaseCube != null;
        if (DifSkyLayers.Ready) DifSkyLayers.CreateMaterials();

        Debug.Log("[DifRUST][sky] assets: cube=" + (DifSky.Cube != null)
            + " moon=" + (DifSky.Moon != null)
            + " layers=" + DifSkyLayers.Active
            + " glow=" + (GlowCube != null)
            + " stars=" + (StarCube != null)
            + " lunar=" + DifSky.LunarEventsPresent);
    }

    private static IEnumerator LoadLayerCube(string prefix, int expected)
    {
        Cubemap cube = null;
        Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

        for (int f = 0; f < 6; f++)
        {
            string file = Path.Combine(Root, prefix + "_" + Faces[f] + ".png");
            byte[] bytes = null;
            try { bytes = File.ReadAllBytes(file); } catch { }

            if (bytes == null || !tmp.LoadImage(bytes, false))
            {
                Debug.Log("[DifRUST][sky] layer face missing: " + file);
                if (cube != null) UnityEngine.Object.Destroy(cube);
                UnityEngine.Object.Destroy(tmp);
                yield break;
            }

            int n = tmp.width;
            if (cube == null)
            {
                if (n != tmp.height || n < 64 || n > 2048)
                {
                    Debug.Log("[DifRUST][sky] bad layer face size: " + n);
                    UnityEngine.Object.Destroy(tmp);
                    yield break;
                }

                cube = new Cubemap(n, GraphicsFormat.R8G8B8A8_SRGB,
                    TextureCreationFlags.MipChain);
            }

            Color32[] src = tmp.GetPixels32();
            Color32[] dst = new Color32[n * n];
            for (int y = 0; y < n; y++)
                Array.Copy(src, y * n, dst, (n - 1 - y) * n, n);
            cube.SetPixelData(dst, 0, (CubemapFace)f);

            yield return null;
        }

        UnityEngine.Object.Destroy(tmp);
        cube.filterMode = FilterMode.Trilinear;
        cube.wrapMode = TextureWrapMode.Clamp;
        cube.anisoLevel = 1;
        cube.hideFlags = HideFlags.HideAndDontSave;
        cube.Apply(true, true);

        if (prefix == "glow") GlowCube = cube;
        else StarCube = cube;
    }

    private static Cubemap CreateBaseCube()
    {
        Cubemap cube = new Cubemap(64,
            GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.MipChain);
        Color32 col = new Color32(16, 24, 48, 255);
        Color32[] px = new Color32[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = col;

        for (int f = 0; f < 6; f++)
            cube.SetPixelData(px, 0, (CubemapFace)f);

        cube.filterMode = FilterMode.Trilinear;
        cube.wrapMode = TextureWrapMode.Clamp;
        cube.anisoLevel = 1;
        cube.hideFlags = HideFlags.HideAndDontSave;
        cube.Apply(true, true);
        return cube;
    }

    private static IEnumerator LoadCube()
    {
        Cubemap cube = null;
        Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

        for (int f = 0; f < 6; f++)
        {
            string file = Path.Combine(Root, "mw_" + Faces[f] + ".png");
            byte[] bytes = null;
            try { bytes = File.ReadAllBytes(file); } catch { }

            if (bytes == null || !tmp.LoadImage(bytes, false))
            {
                Debug.Log("[DifRUST][sky] cube face missing: " + file);
                if (cube != null) UnityEngine.Object.Destroy(cube);
                UnityEngine.Object.Destroy(tmp);
                yield break;
            }

            int n = tmp.width;
            if (cube == null)
            {
                if (n != tmp.height || n < 64 || n > 2048)
                {
                    Debug.Log("[DifRUST][sky] bad cube face size: " + n);
                    UnityEngine.Object.Destroy(tmp);
                    yield break;
                }

                cube = new Cubemap(n, GraphicsFormat.R8G8B8A8_SRGB,
                    TextureCreationFlags.MipChain);
            }

            Color32[] src = tmp.GetPixels32();
            Color32[] dst = new Color32[n * n];
            for (int y = 0; y < n; y++)
                Array.Copy(src, y * n, dst, (n - 1 - y) * n, n);
            cube.SetPixelData(dst, 0, (CubemapFace)f);

            yield return null;
        }

        UnityEngine.Object.Destroy(tmp);
        cube.filterMode = FilterMode.Trilinear;
        cube.wrapMode = TextureWrapMode.Clamp;
        cube.anisoLevel = 1;
        cube.hideFlags = HideFlags.HideAndDontSave;
        cube.Apply(true, true);

        FallbackCube = cube;
        DifSky.Cube = cube;
    }

    private static IEnumerator LoadMoon()
    {
        string file = Path.Combine(Root, "moon.png");
        byte[] bytes = null;
        try { bytes = File.ReadAllBytes(file); } catch { }

        Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (bytes == null || !tmp.LoadImage(bytes, false))
        {
            Debug.Log("[DifRUST][sky] moon missing: " + file);
            UnityEngine.Object.Destroy(tmp);
            yield break;
        }

        int w = tmp.width;
        int h = tmp.height;
        Color32[] px = tmp.GetPixels32();
        UnityEngine.Object.Destroy(tmp);

        Texture2D moon = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
        moon.SetPixels32(px);
        moon.filterMode = FilterMode.Trilinear;
        moon.wrapMode = TextureWrapMode.Clamp;
        moon.anisoLevel = 4;
        moon.hideFlags = HideFlags.HideAndDontSave;
        moon.Apply(true, true);
        DifSky.Moon = moon;

        yield return null;
    }

    public static void DestroyAssets()
    {
        if (GlowCube != null) UnityEngine.Object.Destroy(GlowCube);
        if (StarCube != null) UnityEngine.Object.Destroy(StarCube);
        if (BaseCube != null) UnityEngine.Object.Destroy(BaseCube);
        if (FallbackCube != null) UnityEngine.Object.Destroy(FallbackCube);
        if (DifSky.Moon != null) UnityEngine.Object.Destroy(DifSky.Moon);

        GlowCube = null;
        StarCube = null;
        BaseCube = null;
        FallbackCube = null;
        DifSkyLayers.Ready = false;
    }
}

internal static class DifSkyLayers
{
    private const float GlowIntensity = 0.45f;
    private const float StarIntensity = 0.45f;

    public static bool Ready;
    public static bool Active { get; private set; }
    public static Material GlowMaterial { get; private set; }
    public static Material StarMaterial { get; private set; }

    private static Shader cachedShader;
    private static AssetBundle skyBundle;

    public static Shader FindShader()
    {
        if (cachedShader != null) return cachedShader;

        string bundlePath = Path.Combine(DifSkyAssets.Root, "difrustsky");
        if (!File.Exists(bundlePath))
        {
            Debug.LogWarning("[DifRUST][sky] sky bundle missing: " + bundlePath);
            return null;
        }

        try
        {
            skyBundle = AssetBundle.LoadFromFile(bundlePath);
            if (skyBundle == null)
            {
                Debug.LogWarning("[DifRUST][sky] sky bundle failed to load");
                return null;
            }

            Shader[] shaders = skyBundle.LoadAllAssets<Shader>();
            if (shaders == null || shaders.Length == 0)
            {
                Debug.LogWarning("[DifRUST][sky] sky bundle has no shaders");
                return null;
            }

            cachedShader = shaders[0];
            Debug.Log("[DifRUST][sky] layer shader: " + cachedShader.name);
            return cachedShader;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DifRUST][sky] sky bundle error: " + e.Message);
            return null;
        }
    }

    public static void CreateMaterials()
    {
        if (Active) return;
        if (DifSkyAssets.GlowCube == null || DifSkyAssets.StarCube == null)
            return;

        Shader sh = FindShader();
        if (sh == null)
        {
            Ready = false;
            return;
        }

        try
        {
            GlowMaterial = MakeMaterial(sh, DifSkyAssets.GlowCube);
            StarMaterial = MakeMaterial(sh, DifSkyAssets.StarCube);
            Active = true;
            Debug.Log("[DifRUST][sky] layer materials ready: " + sh.name);
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] layer materials failed: " + e);
            Restore();
            Ready = false;
        }
    }

    private static Material MakeMaterial(Shader sh, Cubemap cube)
    {
        Material m = new Material(sh);
        m.hideFlags = HideFlags.HideAndDontSave;
        m.SetTexture("_MainTex", cube);
        if (m.HasProperty("_TintColor"))
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.renderQueue = 1000;
        return m;
    }

    public static void Tick()
    {
        if (!Active) return;

        float night = Mathf.Clamp01(
            (RustLighting.NightDepth() - 0.35f) / 0.65f);
        float weather = Mathf.Clamp01(SkyManager.starIntensity);
        float amount = Mathf.Clamp(RustSettings.SkyAmount, 0.3f, 1.5f);
        float k = RustSettings.SkyOn ? night * weather * amount : 0f;

        // Fragment = 2 * _TintColor * cube; RGB задаёт яркость,
        // alpha 0.5 нужна для Blend SrcAlpha One.
        float g = 0.5f * GlowIntensity * k;
        float s = 0.5f * StarIntensity * k;

        if (GlowMaterial != null)
            GlowMaterial.SetColor("_TintColor", new Color(g, g, g, 0.5f));
        if (StarMaterial != null)
            StarMaterial.SetColor("_TintColor", new Color(s, s, s, 0.5f));
    }

    public static void Restore()
    {
        if (GlowMaterial != null) UnityEngine.Object.Destroy(GlowMaterial);
        if (StarMaterial != null) UnityEngine.Object.Destroy(StarMaterial);
        GlowMaterial = null;
        StarMaterial = null;
        Active = false;
    }
}

// Плотная сфера неба: ванильная сетка низкополигональная, и в прицеле
// кубмапа растягивается по крупным треугольникам.
internal static class DifSkyMesh
{
    private static Mesh plainMesh;
    private static Mesh layeredMesh;
    private static MeshFilter filter;
    private static Renderer renderer;
    private static Mesh original;
    private static Material[] originalMats;
    private static bool applied;
    private static bool yielded;

    public static void Tick()
    {
        Transform t = SkyManager.atmosphereSphere;
        if (t == null) return;

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null) return;

        if (!ReferenceEquals(mf, filter))
        {
            filter = mf;
            original = mf.sharedMesh;
            renderer = t.GetComponent<Renderer>();
            originalMats = renderer != null ? renderer.sharedMaterials : null;
            applied = false;
            yielded = false;
        }

        if (!RustSettings.SkyOn || !RustSettings.SkyMesh)
        {
            Restore();
            return;
        }

        if (yielded) return;

        Mesh cur = mf.sharedMesh;
        if (cur != null && cur != plainMesh && cur != layeredMesh && cur != original)
        {
            yielded = true;
            Debug.Log("[DifRUST][sky] atmosphere mesh changed by another mod, yielding");
            return;
        }

        if (original != null && original.vertexCount > 8000)
            return;

        if (DifSkyLayers.Active && !DifSky.SkyYielded
            && !ReferenceEquals(DifSky.Cube, DifSkyAssets.BaseCube))
            DifSky.UseLayeredSky();

        bool layers = DifSkyLayers.Active && DifSky.SkyActive;

        if (layers)
        {
            if (layeredMesh == null) layeredMesh = Build(128, 64, 3);
            if (mf.sharedMesh != layeredMesh) mf.sharedMesh = layeredMesh;

            Material atmo = SkyManager.atmosphereMtrl;
            if (atmo == null && originalMats != null && originalMats.Length > 0)
                atmo = originalMats[0];

            if (renderer != null && atmo != null
                && DifSkyLayers.GlowMaterial != null
                && DifSkyLayers.StarMaterial != null)
            {
                renderer.sharedMaterials = new Material[]
                {
                    atmo,
                    DifSkyLayers.GlowMaterial,
                    DifSkyLayers.StarMaterial
                };
            }

            applied = true;
        }
        else
        {
            if (plainMesh == null) plainMesh = Build(128, 64, 1);
            if (mf.sharedMesh != plainMesh) mf.sharedMesh = plainMesh;

            if (renderer != null)
            {
                Material atmo = SkyManager.atmosphereMtrl;
                if (atmo != null) renderer.sharedMaterial = atmo;
                else if (originalMats != null) renderer.sharedMaterials = originalMats;
            }

            applied = true;
        }
    }

    public static void Restore()
    {
        try
        {
            if (filter != null && original != null && applied)
                filter.sharedMesh = original;
            if (renderer != null && originalMats != null && applied)
                renderer.sharedMaterials = originalMats;
        }
        catch
        {
        }

        applied = false;
    }

    private static Mesh Build(int lon, int lat, int submeshes)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var front = new List<int>();
        var back = new List<int>();

        for (int y = 0; y <= lat; y++)
        {
            float v = (float)y / lat;
            float theta = v * Mathf.PI;

            for (int x = 0; x <= lon; x++)
            {
                float u = (float)x / lon;
                float phi = u * Mathf.PI * 2f;

                verts.Add(new Vector3(
                    Mathf.Sin(theta) * Mathf.Cos(phi),
                    Mathf.Cos(theta),
                    Mathf.Sin(theta) * Mathf.Sin(phi)));
                uvs.Add(new Vector2(u, 1f - v));
            }
        }

        for (int y = 0; y < lat; y++)
        {
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x;
                int b = a + 1;
                int c = a + lon + 1;
                int d = c + 1;

                front.Add(a); front.Add(c); front.Add(b);
                front.Add(b); front.Add(c); front.Add(d);

                back.Add(a); back.Add(b); back.Add(c);
                back.Add(b); back.Add(d); back.Add(c);
            }
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = submeshes;

        var atmoTris = new List<int>(front);
        atmoTris.AddRange(back);
        mesh.SetTriangles(atmoTris, 0);

        // Слои не нуждаются в двух сторонах: шейдер слоёв Cull Off,
        // а двойной набор удвоил бы яркость.
        for (int s = 1; s < submeshes; s++)
            mesh.SetTriangles(front, s);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.hideFlags = HideFlags.HideAndDontSave;
        return mesh;
    }
}

public class DifSkyBehaviour : MonoBehaviour
{
    private void Start()
    {
        StartCoroutine(DifSkyAssets.LoadAll());
    }

    private void LateUpdate()
    {
        DifSky.Tick();
    }

    private void OnDestroy()
    {
        DifSky.Restore();
    }
}

// Диагностика ночного неба — шаг 1 плана `SKY-PLAN.md`.
// Только чтение: материалы, шейдеры, текстуры, направления солнца/луны,
// камера. Визуально мод не меняется.
// Пишет `DifRUST-sky.log` в persistentDataPath и одну строку в лог игры.
// Срез снимается при входе в мир, в 00:00 и 12:00 игрового времени,
// а также вручную по NumPad9 (чтобы поймать любой ракурс луны).
internal static class DifSkyDiag
{
    public const string Version = "1.7.0";

    private static string filePath;
    private static bool fileReady;

    public static void Create()
    {
        try
        {
            GameObject go = new GameObject("DifRUSTSkyDiag");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<DifSkyDiagBehaviour>();
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] diag create failed: " + e);
        }
    }

    private static string FilePath
    {
        get
        {
            if (filePath == null)
            {
                try
                {
                    filePath = Path.Combine(Application.persistentDataPath,
                        "DifRUST-sky.log");
                }
                catch
                {
                    filePath = "DifRUST-sky.log";
                }
            }
            return filePath;
        }
    }

    // true — срез записан, можно отмечать слот как снятый.
    public static bool Dump(string tag)
    {
        SkyManager sm = SkyManager.skyManager;
        if (sm == null || SkyManager.moonSpriteMat == null
            || SkyManager.moonSpriteT == null)
            return false;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        ulong wt = world != null ? world.GetWorldTime() : 0UL;
        int day = world != null ? GameUtils.WorldTimeToDays(wt) : -1;
        int hour = world != null ? GameUtils.WorldTimeToHours(wt) : -1;
        int minute = world != null ? GameUtils.WorldTimeToMinutes(wt) : -1;

        StringBuilder sb = new StringBuilder(24 * 1024);
        sb.AppendLine();
        sb.AppendLine("################ " + tag + " ################");
        sb.AppendLine("real: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + "  uptime=" + Fmt(Time.realtimeSinceStartup) + "s  frame=" + Time.frameCount);
        sb.AppendLine("time: day " + day + "  "
            + hour.ToString("00") + ":" + minute.ToString("00")
            + "  raw=" + wt
            + "  clock=" + Fmt(RustLighting.GetClock())
            + "  nightDepth=" + Fmt(RustLighting.NightDepth())
            + "  factor=" + Fmt(RustLighting.GetFactor())
            + "  dayLength=" + DayLength() + " min");

        sb.AppendLine();
        sb.AppendLine("--- directions");
        Vector3 moonDir = SkyManager.GetMoonDirection();
        Vector3 sunDir = SkyManager.GetSunDirection();
        sb.AppendLine("  moonDir=" + Vec(moonDir) + "  len=" + Fmt(moonDir.magnitude)
            + "  altitude=" + Fmt(moonDir.y));
        sb.AppendLine("  sunDir=" + Vec(sunDir) + "  altitude=" + Fmt(sunDir.y));
        sb.AppendLine("  sunMoonDirV=" + Vec(SkyManager.sunMoonDirV));
        sb.AppendLine("  moonBright=" + Fmt(SkyManager.moonBright)
            + "  GetMoonBrightness=" + Fmt(SkyManager.GetMoonBrightness())
            + "  moonLightColor=" + Col(SkyManager.GetMoonLightColor()));
        if (SkyManager.moonLight != null)
        {
            sb.AppendLine("  moonLight: intensity="
                + Fmt(SkyManager.moonLight.intensity)
                + "  color=" + Col(SkyManager.moonLight.color)
                + "  rot=" + Quat(SkyManager.moonLight.transform.rotation));
        }
        if (SkyManager.sMoonPhases != null)
            sb.AppendLine("  sMoonPhases[" + SkyManager.sMoonPhases.Length + "]="
                + Floats(SkyManager.sMoonPhases));
        if (SkyManager.sMoonBrights != null)
            sb.AppendLine("  sMoonBrights[" + SkyManager.sMoonBrights.Length + "]="
                + Floats(SkyManager.sMoonBrights));

        sb.AppendLine();
        sb.AppendLine("--- camera");
        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("  <null>");
        }
        else
        {
            sb.AppendLine("  " + cam.name + "  instance=" + cam.GetInstanceID());
            sb.AppendLine("  near=" + Fmt(cam.nearClipPlane)
                + "  far=" + Fmt(cam.farClipPlane)
                + "  fov=" + Fmt(cam.fieldOfView)
                + "  aspect=" + Fmt(cam.aspect));
            sb.AppendLine("  clearFlags=" + cam.clearFlags
                + "  depthTextureMode=" + cam.depthTextureMode
                + "  renderingPath=" + cam.actualRenderingPath
                + "  hdr=" + cam.allowHDR + "  msaa=" + cam.allowMSAA
                + "  " + cam.pixelWidth + "x" + cam.pixelHeight);
            sb.AppendLine("  pos=" + Vec(cam.transform.position)
                + "  forward=" + Vec(cam.transform.forward));
        }

        sb.AppendLine();
        sb.AppendLine("--- render settings");
        Material skybox = RenderSettings.skybox;
        sb.AppendLine("  skybox=" + (skybox != null
            ? skybox.name + " / " + ShaderName(skybox)
            : "<null>")
            + "  instance=" + (skybox != null ? skybox.GetInstanceID() : 0));
        sb.AppendLine("  ambientMode=" + RenderSettings.ambientMode
            + "  fog=" + RenderSettings.fog
            + "  fogDensity=" + Fmt(RenderSettings.fogDensity)
            + "  fogColor=" + Col(RenderSettings.fogColor));

        sb.AppendLine();
        sb.AppendLine("--- all cameras");
        Camera[] cams = Camera.allCameras;
        sb.AppendLine("  count=" + Camera.allCamerasCount);
        for (int i = 0; i < cams.Length; i++)
        {
            Camera c = cams[i];
            if (c == null) continue;
            sb.AppendLine("  [" + i + "] " + c.name
                + "  tag=" + c.tag
                + "  enabled=" + c.enabled
                + "  depth=" + Fmt(c.depth)
                + "  near=" + Fmt(c.nearClipPlane)
                + "  far=" + Fmt(c.farClipPlane)
                + "  fov=" + Fmt(c.fieldOfView)
                + "  clear=" + c.clearFlags
                + "  cullingMask=0x" + c.cullingMask.ToString("X8")
                + "  target=" + (c.targetTexture != null ? c.targetTexture.name : "<screen>")
                + "  rect=" + c.rect
                + "  depthTex=" + c.depthTextureMode);
            sb.AppendLine("      layers: " + LayerNames(c.cullingMask));
            sb.AppendLine("      pos=" + Vec(c.transform.position));
        }

        sb.AppendLine();
        sb.AppendLine("--- quality / graphics");
        sb.AppendLine("  colorSpace=" + QualitySettings.activeColorSpace
            + "  api=" + SystemInfo.graphicsDeviceType
            + "  gpu=" + SystemInfo.graphicsDeviceName);
        Material atmoM = SkyManager.atmosphereMtrl;
        Cubemap vanillaCube = atmoM != null
            ? atmoM.GetTexture("_OuterSpaceCube") as Cubemap : null;
        sb.AppendLine("  vanillaCube=" + CubeInfo(vanillaCube));
        sb.AppendLine("  atmosphereEuler=" + (SkyManager.atmosphereSphere != null
                ? Vec(SkyManager.atmosphereSphere.eulerAngles) : "<null>")
            + "  cloudsEuler=" + (SkyManager.cloudsSphere != null
                ? Vec(SkyManager.cloudsSphere.eulerAngles) : "<null>"));
        if (cam != null)
        {
            Component[] comps = cam.GetComponents<Component>();
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(comps[i].GetType().Name);
            }
            sb.AppendLine("  cameraComponents=" + names);
        }

        sb.AppendLine();
        sb.AppendLine("=== SKY SYSTEM");
        Transform root = SkyManager.parent != null
            ? SkyManager.parent.transform : null;
        DumpTransform(sb, "  ", root);
        if (root != null)
        {
            sb.AppendLine("  hierarchy:");
            DumpHierarchy(sb, root, 2, 3);
        }
        sb.AppendLine("  starIntensity=" + Fmt(SkyManager.starIntensity)
            + "  SkyColor=" + Col(SkyManager.SkyColor)
            + "  fogColor=" + Col(SkyManager.fogColor)
            + "  dayPercent=" + Fmt(SkyManager.dayPercent)
            + "  worldRotation=" + Fmt(SkyManager.worldRotation)
            + "  cloudTransition=" + Fmt(SkyManager.cloudTransition));
        sb.AppendLine("  cloudMainTex=" + TexInfo(SkyManager.cloudMainTex)
            + "  cloudBlendTex=" + TexInfo(SkyManager.cloudBlendTex));
        sb.AppendLine("  atmosphereMesh=" + MeshInfo(SkyManager.atmosphereSphere)
            + "  cloudsMesh=" + MeshInfo(SkyManager.cloudsSphere));
        sb.AppendLine("  atmosphereSphere=" + (SkyManager.atmosphereSphere != null
            ? SkyManager.atmosphereSphere.name + "  instance="
                + SkyManager.atmosphereSphere.GetInstanceID()
            : "<null>"));
        sb.AppendLine("  cloudsSphere=" + (SkyManager.cloudsSphere != null
            ? SkyManager.cloudsSphere.name + "  instance="
                + SkyManager.cloudsSphere.GetInstanceID()
            : "<null>"));
        sb.AppendLine("  mainCamera=" + (SkyManager.mainCamera != null
            ? SkyManager.mainCamera.name + "  far="
                + Fmt(SkyManager.mainCamera.farClipPlane)
            : "<null>"));

        DumpMaterial(sb, "SkyManager.atmosphereMtrl", SkyManager.atmosphereMtrl);
        DumpMaterial(sb, "SkyManager.cloudsSphereMtrl", SkyManager.cloudsSphereMtrl);

        sb.AppendLine();
        sb.AppendLine("=== MOON SPRITE DIRECT");
        DumpTransform(sb, "  ", SkyManager.moonSpriteT);
        DumpRenderer(sb, "  ", SkyManager.moonSpriteT);
        DumpMesh(sb, "  ", SkyManager.moonSpriteT);
        sb.AppendLine("  layer=" + LayerOf(SkyManager.moonSpriteT));
        DumpMaterial(sb, "SkyManager.moonSpriteMat", SkyManager.moonSpriteMat);

        sb.AppendLine();
        sb.AppendLine("=== END " + tag);

        string text = sb.ToString();
        if (!Write(text))
        {
            Debug.Log(text);
        }
        Debug.Log("[DifRUST][sky] dump " + tag + " -> " + FilePath
            + "  (day " + day + " " + hour.ToString("00") + ":" + minute.ToString("00") + ")");
        return true;
    }

    private static bool Write(string text)
    {
        try
        {
            if (!fileReady)
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                StringBuilder head = new StringBuilder();
                head.AppendLine("=== DifRUST sky diagnostics " + Version + " ===");
                head.AppendLine("session: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                head.AppendLine("unity: " + Application.unityVersion
                    + "  game: " + Application.version
                    + "  platform: " + Application.platform);
                head.AppendLine();

                File.WriteAllText(FilePath, head.ToString());
                fileReady = true;
            }

            File.AppendAllText(FilePath, text);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[DifRUST][sky] log write failed: " + e);
            return false;
        }
    }

    // Строка пробы: в тот же файл и в лог игры.
    public static void LogLine(string line)
    {
        float clock = RustLighting.GetClock();
        string text = "probe " + (clock < 0f ? "--:--" : clock.ToString("0.00"))
            + "  " + line + Environment.NewLine;

        if (!Write(text))
            Debug.Log("[DifRUST][sky] " + line.Trim());
        else
            Debug.Log("[DifRUST][sky] probe: " + line);
    }

    private static void DumpMesh(StringBuilder sb, string pad, Transform t)
    {
        if (t == null) return;

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            sb.AppendLine(pad + "mesh: <none>");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        sb.AppendLine(pad + "mesh=" + mesh.name
            + "  verts=" + mesh.vertexCount
            + "  subMeshes=" + mesh.subMeshCount
            + "  bounds=" + Vec(mesh.bounds.size));
    }

    private static string LayerOf(Transform t)
    {
        if (t == null) return "<null>";

        int layer = t.gameObject.layer;
        string name = LayerMask.LayerToName(layer);
        return layer + (string.IsNullOrEmpty(name) ? "" : " (" + name + ")");
    }

    private static string LayerNames(int mask)
    {
        StringBuilder s = new StringBuilder();

        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;

            string name = LayerMask.LayerToName(i);
            if (s.Length > 0) s.Append(", ");
            s.Append(i).Append(string.IsNullOrEmpty(name) ? "" : ":" + name);
        }

        return s.Length > 0 ? s.ToString() : "<none>";
    }

    private static void DumpHierarchy(StringBuilder sb, Transform t, int depth, int maxDepth)
    {
        if (t == null || depth > maxDepth + 1) return;

        string pad = new string(' ', depth * 2);
        sb.AppendLine(pad + "- " + t.name
            + "  active=" + t.gameObject.activeSelf
            + "  layer=" + t.gameObject.layer
            + "  localPos=" + Vec(t.localPosition)
            + "  localScale=" + Vec(t.localScale));

        Renderer r = t.GetComponent<Renderer>();
        if (r != null)
        {
            sb.AppendLine(pad + "    renderer=" + r.GetType().Name
                + "  enabled=" + r.enabled
                + "  mat=" + (r.sharedMaterial != null
                    ? r.sharedMaterial.name + " / " + ShaderName(r.sharedMaterial)
                    : "<null>"));
        }

        for (int i = 0; i < t.childCount; i++)
            DumpHierarchy(sb, t.GetChild(i), depth + 1, maxDepth);
    }

    private static void DumpTransform(StringBuilder sb, string pad, Transform t)
    {
        if (t == null)
        {
            sb.AppendLine(pad + "transform: <null>");
            return;
        }

        sb.AppendLine(pad + "transform: " + t.name
            + "  instance=" + t.GetInstanceID());
        sb.AppendLine(pad + "  worldPos=" + Vec(t.position)
            + "  localPos=" + Vec(t.localPosition));
        sb.AppendLine(pad + "  worldScale=" + Vec(t.lossyScale)
            + "  localScale=" + Vec(t.localScale));
        sb.AppendLine(pad + "  euler=" + Vec(t.eulerAngles)
            + "  forward=" + Vec(t.forward));
        sb.AppendLine(pad + "  parent=" + (t.parent != null ? t.parent.name : "<none>")
            + "  children=" + t.childCount
            + "  activeInHierarchy=" + t.gameObject.activeInHierarchy);
    }

    private static void DumpRenderer(StringBuilder sb, string pad, Transform t)
    {
        if (t == null) return;

        Renderer r = t.GetComponent<Renderer>();
        if (r == null)
        {
            sb.AppendLine(pad + "renderer: <none>");
            return;
        }

        sb.AppendLine(pad + "renderer: " + r.GetType().Name
            + "  enabled=" + r.enabled
            + "  castShadows=" + r.shadowCastingMode
            + "  receiveShadows=" + r.receiveShadows);
        sb.AppendLine(pad + "  sharedMaterial=" + (r.sharedMaterial != null
            ? r.sharedMaterial.name + " / " + ShaderName(r.sharedMaterial)
            : "<null>"));

        Bounds b = r.bounds;
        sb.AppendLine(pad + "  boundsCenter=" + Vec(b.center)
            + "  boundsSize=" + Vec(b.size));
    }

    private static void DumpMaterial(StringBuilder sb, string label, Material m)
    {
        sb.AppendLine("--- material: " + label);
        if (m == null)
        {
            sb.AppendLine("  <null>");
            return;
        }

        Shader sh = m.shader;
        sb.AppendLine("  name=" + m.name + "  instance=" + m.GetInstanceID()
            + "  renderQueue=" + m.renderQueue
            + "  passes=" + m.passCount);
        sb.AppendLine("  shader=" + (sh != null ? sh.name : "<null>")
            + (sh != null
                ? "  queue=" + sh.renderQueue + "  supported=" + sh.isSupported
                : ""));

        string[] kw = m.shaderKeywords;
        if (kw != null && kw.Length > 0)
            sb.AppendLine("  keywords=" + string.Join(", ", kw));

        if (sh == null) return;

        int count = 0;
        try
        {
            count = sh.GetPropertyCount();
        }
        catch (Exception e)
        {
            sb.AppendLine("  GetPropertyCount failed: " + e.Message);
        }

        if (count == 0)
        {
            sb.AppendLine("  exposed properties: 0");
            return;
        }

        sb.AppendLine("  properties (" + count + "):");

        for (int i = 0; i < count; i++)
        {
            string p;
            try
            {
                p = sh.GetPropertyName(i);
            }
            catch
            {
                continue;
            }

            try
            {
                if (m.HasTexture(p))
                {
                    sb.AppendLine("    " + p + " = tex " + TexInfo(m.GetTexture(p)));
                }
                else if (m.HasColor(p))
                {
                    sb.AppendLine("    " + p + " = " + Col(m.GetColor(p)));
                }
                else if (m.HasVector(p))
                {
                    sb.AppendLine("    " + p + " = " + Vec4(m.GetVector(p)));
                }
                else if (m.HasFloat(p))
                {
                    sb.AppendLine("    " + p + " = " + Fmt(m.GetFloat(p)));
                }
                else
                {
                    sb.AppendLine("    " + p + " = <unset>");
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("    " + p + " = <read failed: " + e.Message + ">");
            }
        }
    }

    private static string TexInfo(Texture t)
    {
        if (t == null) return "<null>";

        StringBuilder s = new StringBuilder();
        s.Append(t.name).Append(" [").Append(t.GetType().Name).Append("] ");
        s.Append(t.width).Append("x").Append(t.height);

        Texture2D t2 = t as Texture2D;
        if (t2 != null)
        {
            s.Append(" fmt=").Append(t2.format);
            s.Append(" mips=").Append(t2.mipmapCount);
        }

        s.Append(" wrap=").Append(t.wrapMode);
        s.Append(" filter=").Append(t.filterMode);
        s.Append(" aniso=").Append(t.anisoLevel);
        return s.ToString();
    }

    private static string ShaderName(Material m)
    {
        if (m == null || m.shader == null) return "<null>";
        return m.shader.name;
    }

    private static string CubeInfo(Cubemap c)
    {
        if (c == null) return "<null>";

        return c.name + "  " + c.width + "x" + c.height
            + "  fmt=" + c.format
            + "  mips=" + c.mipmapCount
            + "  readable=" + c.isReadable
            + "  filter=" + c.filterMode
            + "  wrap=" + c.wrapMode
            + "  aniso=" + c.anisoLevel
            + "  dim=" + c.dimension;
    }

    private static string MeshInfo(Transform t)
    {
        if (t == null) return "<null>";

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "<none>";

        Mesh m = mf.sharedMesh;
        return m.name + "  verts=" + m.vertexCount
            + "  tris=" + (m.triangles.Length / 3);
    }

    private static string DayLength()
    {
        try
        {
            return GamePrefs.GetInt(EnumGamePrefs.DayNightLength).ToString();
        }
        catch
        {
            return "?";
        }
    }

    private static string Fmt(float v)
    {
        if (float.IsNaN(v)) return "NaN";
        if (float.IsPositiveInfinity(v)) return "+inf";
        if (float.IsNegativeInfinity(v)) return "-inf";
        return v.ToString("0.####",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Vec(Vector3 v)
    {
        return "(" + Fmt(v.x) + ", " + Fmt(v.y) + ", " + Fmt(v.z) + ")";
    }

    private static string Vec4(Vector4 v)
    {
        return "(" + Fmt(v.x) + ", " + Fmt(v.y) + ", "
            + Fmt(v.z) + ", " + Fmt(v.w) + ")";
    }

    private static string Col(Color c)
    {
        return "(" + Fmt(c.r) + ", " + Fmt(c.g) + ", "
            + Fmt(c.b) + ", " + Fmt(c.a) + ")";
    }

    private static string Quat(Quaternion q)
    {
        return "euler" + Vec(q.eulerAngles);
    }

    private static string Floats(float[] a)
    {
        if (a == null) return "<null>";

        StringBuilder s = new StringBuilder();
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) s.Append(", ");
            s.Append(Fmt(a[i]));
        }
        return s.ToString();
    }
}

public class DifSkyDiagBehaviour : MonoBehaviour
{
    private bool startupDone;
    private int lastDay = -1;
    private int lastSlot = -1;
    private float nextTry;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Keypad9))
        {
            if (!DifSkyDiag.Dump("manual"))
                Debug.LogWarning("[DifRUST][sky] manual dump skipped: sky not ready");
        }

        if (Time.time < nextTry) return;
        nextTry = Time.time + 1f;

        GameManager gm = GameManager.Instance;
        World world = gm != null ? gm.World : null;
        if (world == null) return;

        ulong wt = world.GetWorldTime();
        int day = GameUtils.WorldTimeToDays(wt);
        int hour = GameUtils.WorldTimeToHours(wt);

        if (!startupDone)
        {
            // Ждём игрока: срез на экране загрузки бесполезен.
            if (world.GetPrimaryPlayer() == null) return;

            startupDone = DifSkyDiag.Dump("startup");

            // Если вошли ровно в полдень/полночь, не дублируем срез.
            if (startupDone && (hour == 0 || hour == 12))
            {
                lastDay = day;
                lastSlot = hour;
            }
            return;
        }

        if (hour != 0 && hour != 12) return;
        if (day == lastDay && hour == lastSlot) return;

        if (DifSkyDiag.Dump(hour == 0 ? "midnight" : "noon"))
        {
            lastDay = day;
            lastSlot = hour;
        }
    }
}

public class DifMenuBehaviour : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public static void ResetState()
    {
        IsOpen = false;
    }

    private bool open;
    private Rect windowRect = new Rect(70f, 40f, 470f, 825f);

    private Texture2D panelTex;
    private Texture2D accentTex;
    private Texture2D dimTex;
    private Texture2D rowTex;

    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle hintStyle;
    private GUIStyle valueStyle;
    private GUIStyle buttonStyle;
    private GUIStyle toggleStyle;
    private GUIStyle warnStyle;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Keypad6) || Input.GetKeyDown(KeyCode.F8))
        {
            Toggle();
        }

        if (!open) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.World == null)
        {
            Close();
            return;
        }

        if (!gm.GetCursorEnabledOverride())
        {
            gm.SetCursorEnabledOverride(true, true);
        }

        EntityPlayerLocal player = gm.World.GetPrimaryPlayer();
        if (player != null)
        {
            player.moveDirection = Vector3.zero;
        }
    }

    private void Toggle()
    {
        if (open)
        {
            Close();
            return;
        }

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.World == null) return;

        open = true;
        IsOpen = true;
        gm.SetCursorEnabledOverride(true, true);
    }

    private void Close()
    {
        open = false;
        IsOpen = false;

        RustSettings.FlushSave();

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SetCursorEnabledOverride(false, false);
        }
    }

    private void OnApplicationQuit()
    {
        RustSettings.FlushSave();
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (!open) return;

        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            dimTex,
            ScaleMode.StretchToFill
        );

        windowRect = GUI.Window(0x6D52, windowRect, DrawWindow, GUIContent.none, GUIStyle.none);

        // Не даём утащить окно за край экрана.
        windowRect.x = Mathf.Clamp(windowRect.x, 0f,
            Mathf.Max(0f, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0f,
            Mathf.Max(0f, Screen.height - windowRect.height));
    }

    private void DrawWindow(int id)
    {
        float w = windowRect.width;
        float h = windowRect.height;

        GUI.DrawTexture(new Rect(0f, 0f, w, h), panelTex, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(0f, 0f, w, 3f), accentTex, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(0f, h - 1f, w, 1f), accentTex, ScaleMode.StretchToFill);

        bool locked = RustSettings.Forced;

        float y = 12f;
        GUI.Label(new Rect(18f, y, w - 70f, 26f), "DIFRUST", titleStyle);

        if (GUI.Button(new Rect(w - 38f, 10f, 26f, 24f), "x", buttonStyle))
        {
            Close();
            return;
        }

        y += 32f;
        GUI.DrawTexture(new Rect(18f, y, w - 36f, 1f), rowTex, ScaleMode.StretchToFill);
        y += 14f;

        GUI.enabled = !locked;
        {
            bool was = RustSettings.Enabled;
            RustSettings.Enabled = GUI.Toggle(
                new Rect(18f, y, w - 36f, 24f),
                RustSettings.Enabled,
                RustText.T("  Darkening enabled", "  Затемнение включено"),
                toggleStyle
            );
            if (was != RustSettings.Enabled) { RustSettings.MarkDirty(); RustSettings.Save(); }
        }
        GUI.enabled = true;

        y += 34f;

        // Пресеты в один клик.
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("PRESETS", "ПРЕСЕТЫ"), labelStyle);
        y += 22f;

        GUI.enabled = RustSettings.Enabled && !locked;
        {
            float bw = (w - 36f - 8f) / 3f;
            if (GUI.Button(new Rect(18f, y, bw, 26f), RustText.T("Soft", "Мягкая"), buttonStyle))
                RustSettings.ApplyPreset(0);
            if (GUI.Button(new Rect(18f + bw + 4f, y, bw, 26f), RustText.T("Standard", "Стандарт"), buttonStyle))
                RustSettings.ApplyPreset(1);
            if (GUI.Button(new Rect(18f + (bw + 4f) * 2f, y, bw, 26f), RustText.T("Rust", "Rust"), buttonStyle))
                RustSettings.ApplyPreset(2);
        }
        GUI.enabled = true;
        y += 36f;

        // Тумблеры эффектов 1.5.0: без новых ползунков.
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("EFFECTS", "ЭФФЕКТЫ"), labelStyle);
        y += 22f;

        float colW = (w - 36f - 8f) / 2f;
        GUI.enabled = RustSettings.Enabled && !locked;

        ToggleAt(18f, y, colW, ref RustSettings.WeatherOn,
            RustText.T("  Weather", "  Погода"));
        ToggleAt(18f + colW + 8f, y, colW, ref RustSettings.EyeAdapt,
            RustText.T("  Eye adapt", "  Глаза"));

        y += 24f;
        ToggleAt(18f, y, colW, ref RustSettings.LightRelief,
            RustText.T("  Flashlight/fire", "  Фонарь/огонь"));
        ToggleAt(18f + colW + 8f, y, colW, ref RustSettings.BiomeTintOn,
            RustText.T("  Biome tint", "  Биомы"));

        y += 24f;
        ToggleAt(18f, y, colW, ref RustSettings.LunarLink,
            RustText.T("  LunarEvents", "  LunarEvents"));
        ToggleAt(18f + colW + 8f, y, colW, ref RustSettings.NvgRelief,
            RustText.T("  NVG", "  ПНВ"));
        GUI.enabled = true;

        y += 26f;

        // Ночное небо 1.6.0: косметика клиента, без хоста и без замка.
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("NIGHT SKY", "НОЧНОЕ НЕБО"), labelStyle);
        y += 22f;

        ToggleAt(18f, y, colW, ref RustSettings.SkyOn,
            RustText.T("  Milky Way", "  Млечный Путь"));

        ToggleAt(18f + colW + 8f, y, colW, ref RustSettings.SkyMoon,
            RustText.T("  Second moon", "  Вторая луна"));
        y += 24f;

        GUI.Label(new Rect(18f, y, w - 130f, 20f),
            RustText.T("Sky brightness", "Яркость неба"), labelStyle);
        GUI.Label(new Rect(w - 110f, y, 92f, 20f),
            Mathf.RoundToInt(RustSettings.SkyAmount * 100f) + "%", valueStyle);

        float skyAmount = RustSettings.SkyAmount;
        float newSkyAmount = GUI.HorizontalSlider(
            new Rect(18f, y + 22f, w - 36f, 18f),
            skyAmount, 0.3f, 1.5f);

        if (Mathf.Abs(newSkyAmount - skyAmount) > 0.001f)
        {
            RustSettings.SkyAmount = Mathf.Clamp(newSkyAmount, 0.3f, 1.5f);
            RustSettings.MarkDirty();
            RustSettings.Save();
        }

        y += 46f;

        if (RustSettings.SkyMoon)
        {
            string moonHint;

            if (DifSky.SecondMoonActive)
            {
                moonHint = string.Format(RustText.T(
                    "Tonight: {0}. The second moon burns brighter.",
                    "Сегодня: {0}. Вторая луна разгорается ярче."),
                    DifSky.SpecialMoonName());
            }
            else if (DifSky.LunarApiAvailable)
            {
                moonHint = RustText.T(
                    "Ordinary night: the second moon is calm and dimmer.",
                    "Обычная ночь: вторая луна спокойная и тусклее.");
            }
            else
            {
                moonHint = RustText.T(
                    "LunarEvents is not installed: the second moon stays calm.",
                    "LunarEvents не установлен: вторая луна остаётся спокойной.");
            }

            GUI.Label(new Rect(18f, y, w - 36f, 20f), moonHint, hintStyle);
            y += 20f;
        }
        else
        {
            GUI.Label(new Rect(18f, y, w - 36f, 20f),
                RustText.T("Every night; brighter on special LunarEvents nights.",
                           "Каждую ночь; ярче в особые ночи LunarEvents."),
                hintStyle);
            y += 20f;
        }

        string conflict = RustConflicts.Name;
        if (!string.IsNullOrEmpty(conflict))
        {
            GUI.Label(new Rect(18f, y, w - 36f, 20f),
                string.Format(RustText.T(
                    "Conflict: {0} also darkens nights, effects stack.",
                    "Конфликт: {0} тоже затемняет ночь, эффекты складываются."),
                    conflict),
                warnStyle);
        }
        y += 22f;

        // Серверный замок: только хост решает, применять ли настройки всем.
        if (RustSettings.IsHost)
        {
            GUI.enabled = RustSettings.Enabled;
            bool wasForce = RustSettings.ForceForClients;
            RustSettings.ForceForClients = GUI.Toggle(
                new Rect(18f, y, w - 36f, 24f),
                RustSettings.ForceForClients,
                RustText.T("  Host lock: same night for all players",
                           "  Замок хоста: одна ночь для всех"),
                toggleStyle);
            if (wasForce != RustSettings.ForceForClients)
            {
                RustSettings.MarkDirty();
                RustSettings.Save();
            }
            GUI.enabled = true;
        }
        else if (locked)
        {
            GUI.Label(new Rect(18f, y, w - 36f, 22f),
                RustText.T("Host controls these settings.",
                           "Настройки задаёт хост."), labelStyle);
        }

        y += 30f;

        GUI.enabled = RustSettings.Enabled && !locked;
        GUI.Label(new Rect(18f, y, w - 130f, 20f), RustText.T("Darkness", "Сила тьмы"), labelStyle);
        GUI.Label(new Rect(w - 110f, y, 92f, 20f),
            Mathf.RoundToInt(RustSettings.NightFactorValue * 100f) + "%", valueStyle);

        float value = RustSettings.NightFactorValue;
        float newValue = GUI.HorizontalSlider(
            new Rect(18f, y + 22f, w - 36f, 18f),
            value,
            RustSettings.MinNightFactor,
            RustSettings.MaxNightFactor);

        if (Mathf.Abs(newValue - value) > 0.0005f)
        {
            RustSettings.NightFactorValue = newValue;
            RustSettings.MarkDirty();
            RustSettings.Save();
        }

        y += 46f;

        // Ярче в помещениях.
        GUI.Label(new Rect(18f, y, w - 130f, 20f),
            RustText.T("Brighter indoors", "Ярче в помещениях"), labelStyle);
        GUI.Label(new Rect(w - 110f, y, 92f, 20f),
            Mathf.RoundToInt(RustSettings.IndoorBoost * 100f) + "%", valueStyle);

        float indoor = RustSettings.IndoorBoost;
        float newIndoor = GUI.HorizontalSlider(
            new Rect(18f, y + 22f, w - 36f, 18f), indoor, 0f, 1f);

        if (Mathf.Abs(newIndoor - indoor) > 0.001f)
        {
            RustSettings.IndoorBoost = Mathf.Clamp01(newIndoor);
            RustSettings.MarkDirty();
            RustSettings.Save();
        }

        y += 46f;

        // Холодный оттенок ночи.
        GUI.Label(new Rect(18f, y, w - 130f, 20f),
            RustText.T("Cold night tint", "Холодный оттенок"), labelStyle);
        GUI.Label(new Rect(w - 110f, y, 92f, 20f),
            Mathf.RoundToInt(RustSettings.NightTint * 100f) + "%", valueStyle);

        float tint = RustSettings.NightTint;
        float newTint = GUI.HorizontalSlider(
            new Rect(18f, y + 22f, w - 36f, 18f), tint, 0f, 1f);

        if (Mathf.Abs(newTint - tint) > 0.001f)
        {
            RustSettings.NightTint = Mathf.Clamp01(newTint);
            RustSettings.MarkDirty();
            RustSettings.Save();
        }

        y += 46f;

        // Фазы луны.
        GUI.Label(new Rect(18f, y, w - 150f, 20f),
            RustText.T("Moon phases", "Фазы луны"), labelStyle);
        GUI.Label(new Rect(w - 110f, y, 92f, 20f),
            Mathf.RoundToInt(RustSettings.MoonPhases * 100f) + "%", valueStyle);

        float moon = RustSettings.MoonPhases;
        float newMoon = GUI.HorizontalSlider(
            new Rect(18f, y + 22f, w - 36f, 18f), moon, 0f, 1f);

        if (Mathf.Abs(newMoon - moon) > 0.001f)
        {
            RustSettings.MoonPhases = Mathf.Clamp01(newMoon);
            RustSettings.MarkDirty();
            RustSettings.Save();
        }

        GUI.enabled = true;
        y += 46f;

        GUI.Label(new Rect(18f, y, w - 36f, 40f),
            RustText.T(
                "Lower is darker. 6% = almost pitch black, 30% = soft night. " +
                "Presets and settings blend smoothly.",
                "Меньше — темнее. 6% — почти полная тьма, 30% — мягкая ночь. " +
                "Пресеты и настройки меняются плавно."),
            hintStyle);
        y += 42f;

        if (GUI.Button(new Rect(18f, y, 180f, 30f),
            RustText.T("Night now (30 s)", "Ночь сейчас (30 с)"), buttonStyle))
        {
            RustLighting.ForceNight(30f);
        }

        GUI.enabled = !locked;
        if (GUI.Button(new Rect(206f, y, 90f, 30f), RustText.T("Reset", "Сбросить"), buttonStyle))
        {
            RustSettings.Reset();
        }
        GUI.enabled = true;

        if (GUI.Button(new Rect(304f, y, w - 322f, 30f), RustText.T("Close", "Закрыть"), buttonStyle))
        {
            Close();
            return;
        }

        y += 40f;
        GUI.DrawTexture(new Rect(18f, y, w - 36f, 1f), rowTex, ScaleMode.StretchToFill);
        y += 10f;

        float clock = RustLighting.GetClock();
        string clockText = clock < 0f
            ? "—"
            : string.Format("{0:00}:{1:00}", Mathf.FloorToInt(clock),
                Mathf.FloorToInt((clock - Mathf.Floor(clock)) * 60f));

        string dawn = RustLighting.DawnText();
        string dawnText = string.IsNullOrEmpty(dawn)
            ? ""
            : RustText.T("  ·  dawn in " + dawn, "  ·  до рассвета " + dawn);

        string demo = RustLighting.DemoActive
            ? string.Format(RustText.T("  ·  demo {0} s", "  ·  демо {0} с"),
                Mathf.CeilToInt(RustLighting.DemoRemaining))
            : "";

        string lockText = locked
            ? RustText.T("  ·  host lock", "  ·  замок хоста")
            : "";

        GUI.Label(
            new Rect(18f, y, w - 36f, 20f),
            string.Format(
                RustText.T("Mode: {0}  ·  time {1}  ·  darkness {2}%{3}{4}",
                           "Режим: {0}  ·  время {1}  ·  тьма {2}%{3}{4}"),
                RustLighting.ModeName(),
                clockText,
                Mathf.RoundToInt(RustLighting.GetFactor() * 100f),
                dawnText,
                demo),
            hintStyle
        );

        y += 20f;

        GUI.Label(
            new Rect(18f, y, w - 36f, 20f),
            string.Format(
                RustText.T("Moon: {0} {1}  ·  weather: {2}  ·  biome: {3}{4}",
                           "Луна: {0} {1}  ·  погода: {2}  ·  биом: {3}{4}"),
                RustLighting.MoonName(),
                RustLighting.MoonMulText(),
                RustLighting.WeatherName(),
                RustLighting.BiomeName(),
                lockText),
            hintStyle
        );

        y += 20f;
        GUI.Label(
            new Rect(18f, y, w - 36f, 20f),
            string.Format(
                RustText.T("Ambient: sky {0:0.00}  ·  equator {1:0.00}  ·  ground {2:0.00}",
                           "Ambient: небо {0:0.00}  ·  экватор {1:0.00}  ·  земля {2:0.00}"),
                AmbientProbe.Sky, AmbientProbe.Equator, AmbientProbe.Ground),
            hintStyle
        );

        y += 22f;
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("NumPad6 / F8 = open menu", "NumPad6 / F8 — открыть меню"), hintStyle);

        GUI.DragWindow(new Rect(0f, 0f, w, 40f));
    }

    private void ToggleAt(float x, float y, float width, ref bool value, string label)
    {
        bool was = value;
        value = GUI.Toggle(new Rect(x, y, width, 22f), value, label, toggleStyle);
        if (was != value)
        {
            RustSettings.MarkDirty();
            RustSettings.Save();
        }
    }

    private void EnsureStyles()
    {
        if (panelTex != null) return;

        panelTex = SolidTexture(new Color(0.05f, 0.05f, 0.07f, 0.97f));
        accentTex = SolidTexture(new Color(0.72f, 0.55f, 0.42f, 1f));
        rowTex = SolidTexture(new Color(0.3f, 0.27f, 0.26f, 0.9f));
        dimTex = SolidTexture(new Color(0f, 0f, 0f, 0.5f));

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.85f, 0.75f) }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = new Color(0.9f, 0.88f, 0.86f) }
        };

        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            normal = { textColor = new Color(0.66f, 0.62f, 0.6f) }
        };

        warnStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.62f, 0.42f) }
        };

        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.85f, 0.75f) }
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            normal = { textColor = new Color(0.92f, 0.9f, 0.88f) },
            hover = { textColor = Color.white },
            active = { textColor = Color.white }
        };

        toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 13,
            normal = { textColor = new Color(0.9f, 0.88f, 0.86f) },
            onNormal = { textColor = new Color(0.9f, 0.88f, 0.86f) },
            hover = { textColor = Color.white },
            onHover = { textColor = Color.white }
        };
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, color);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        return tex;
    }
}
