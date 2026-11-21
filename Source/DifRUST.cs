using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
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
                "biome_amount={10}\nlunar={11}\nforce={12}\n",
                Enabled ? "1" : "0", NightFactorValue, IndoorBoost, NightTint, MoonPhases,
                WeatherOn ? "1" : "0", EyeAdapt ? "1" : "0", LightRelief ? "1" : "0",
                NvgRelief ? "1" : "0", BiomeTintOn ? "1" : "0", BiomeTintValue,
                LunarLink ? "1" : "0", ForceForClients ? "1" : "0");
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
            float seconds = factorTarget < factorCurrent ? 2.4f : 1.1f;
            factorCurrent = Mathf.MoveTowards(
                factorCurrent, factorTarget, Time.deltaTime / seconds);
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
                if (bv.isair) continue;

                covered[i] = true;
                hits++;
                break;
            }
        }

        if (!covered[0]) return 0f;

        return hits / 5f;
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
        RenderSettings.ambientSkyColor *= factor;
        RenderSettings.ambientEquatorColor *= factor;
        RenderSettings.ambientGroundColor *= factor;
        RenderSettings.ambientLight *= factor;

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

public class DifMenuBehaviour : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public static void ResetState()
    {
        IsOpen = false;
    }

    private bool open;
    private Rect windowRect = new Rect(70f, 40f, 470f, 660f);

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
