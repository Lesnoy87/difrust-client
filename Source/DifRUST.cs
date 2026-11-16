using System;
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
        new Harmony("difrust.client").PatchAll(Assembly.GetExecutingAssembly());
        RustSettings.Load();
        DifMenu.Create();
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

    public static bool Enabled = true;
    public static float NightFactorValue = DefaultNightFactor;
    public static float IndoorBoost = DefaultIndoorBoost;
    public static float NightTint = DefaultNightTint;
    public static float MoonPhases = DefaultMoonPhases;

    // CVar-имена: хост раздаёт настройки ванильным NetPackageModifyCVar.
    public const string CvarEnabled = "dr_on";
    public const string CvarNight = "dr_night";
    public const string CvarIndoor = "dr_indoor";
    public const string CvarTint = "dr_tint";
    public const string CvarMoon = "dr_moon";

    private static float nextRead;
    private static float nextSend;
    private static bool dirty;

    public static bool Synced { get; private set; }

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
            }
        }
        catch
        {
        }
    }

    public static void Save()
    {
        try
        {
            string text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "enabled={0}\nnight={1}\nindoor={2}\ntint={3}\nmoon={4}\n",
                Enabled ? "1" : "0", NightFactorValue, IndoorBoost, NightTint, MoonPhases);
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

    // Клиент берёт то, что ему прислал хост. Если сервер без мода —
    // остаются свои настройки, и мод работает сам по себе.
    public static void ReadFromLocalPlayer(EntityPlayerLocal player)
    {
        if (player == null || player.Buffs == null) return;
        if (IsHost) return;

        float now = Time.time;
        if (now < nextRead) return;
        nextRead = now + 0.2f;

        EntityBuffs buffs = player.Buffs;
        if (!buffs.HasCustomVar(CvarEnabled))
        {
            Synced = false;
            return;
        }

        Synced = true;
        Enabled = buffs.GetCustomVar(CvarEnabled) >= 0.5f;
        NightFactorValue = Mathf.Clamp(buffs.GetCustomVar(CvarNight), MinNightFactor, MaxNightFactor);
        IndoorBoost = Mathf.Clamp01(buffs.GetCustomVar(CvarIndoor));
        NightTint = Mathf.Clamp01(buffs.GetCustomVar(CvarTint));
        if (buffs.HasCustomVar(CvarMoon))
            MoonPhases = Mathf.Clamp01(buffs.GetCustomVar(CvarMoon));
    }

    // Хост раздаёт настройки всем игрокам (не чаще 4 раз в секунду).
    public static void Tick()
    {
        if (!IsHost) return;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return;

        if (dirty && Time.time >= nextSend)
        {
            dirty = false;
            nextSend = Time.time + 0.25f;
            Broadcast(world);
        }
    }

    private static void Broadcast(World world)
    {
        var players = world.Players.list;
        for (int i = 0; i < players.Count; i++)
        {
            EntityPlayer p = players[i];
            if (p == null || p.Buffs == null) continue;

            Send(p, CvarEnabled, Enabled ? 1f : 0f);
            Send(p, CvarNight, NightFactorValue);
            Send(p, CvarIndoor, IndoorBoost);
            Send(p, CvarTint, NightTint);
            Send(p, CvarMoon, MoonPhases);
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

    public static float GetFactor()
    {
        float factor = GetBaseFactor();
        if (factor >= 0.999f) return factor;

        // Под крышей подмешиваем «домашнюю» яркость: ночью на улице темно,
        // в доме — терпимо. Плавно, по доле укрытия.
        float shelter = NightIndoor.Level;
        if (shelter <= 0.01f) return factor;

        float indoor = Mathf.Lerp(factor, 1f, RustSettings.IndoorBoost);

        // Потолок: в доме ночью всё равно должно быть темнее, чем днём.
        // Иначе при 5% тьмы помещение выглядит как день и ломает демо.
        float cap = Mathf.Max(factor, 0.5f);
        indoor = Mathf.Min(indoor, cap);

        return Mathf.Lerp(factor, indoor, shelter);
    }

    public static float GetBaseFactor()
    {
        if (!RustSettings.Enabled) return 1f;

        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 1f;

        float night = RustSettings.NightFactorValue * MoonMul();
        night = Mathf.Clamp(night, 0.01f, 0.85f);

        if (Time.time < demoUntil) return night;

        float clock = GetClock();
        float factor;

        if (clock >= DuskStart && clock < NightStart)
        {
            float t = Mathf.InverseLerp(DuskStart, NightStart, clock);
            factor = Mathf.Lerp(1f, night, t);
        }
        else if (clock >= NightStart || clock < DawnStart)
        {
            factor = night;
        }
        else if (clock >= DawnStart && clock < DayStart)
        {
            float t = Mathf.InverseLerp(DawnStart, DayStart, clock);
            factor = Mathf.Lerp(night, 1f, t);
        }
        else
        {
            factor = 1f;
        }

        return factor;
    }

    // Фаза луны по номеру игрового дня: 0 — полнолуние, 4 — новолуние.
    // Считаем сами от дня мира и не спрашиваем SkyManager: у него фазы
    // могут отличаться между версиями игры.
    public static int MoonPhase()
    {
        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 0;

        int days = GameUtils.WorldTimeToDays(world.GetWorldTime());
        return ((days % 8) + 8) % 8;
    }

    // Множитель ночной яркости: полнолуние заметно светлее, новолуние темнее.
    // Раньше это был процент от тьмы, и при 6% разница была невидимой,
    // поэтому размах большой: до ×2.5 на полной и ×0.25 на новой луне.
    public static float MoonMul()
    {
        float strength = RustSettings.MoonPhases;
        if (strength <= 0.01f) return 1f;

        float c = Mathf.Cos(MoonPhase() / 8f * Mathf.PI * 2f);
        return Mathf.Max(0.25f, 1f + c * strength * 1.5f);
    }

    // Во сколько раз сейчас ярче/темнее от луны — для строки состояния.
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

    public static float Level
    {
        get
        {
            if (Time.frameCount != sampleFrame)
            {
                sampleFrame = Time.frameCount;
                raw = Sample();
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
        bool[] covered = new bool[5];
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
    private static void Postfix()
    {
        float factor = RustLighting.GetFactor();
        if (factor >= 0.999f) return;

        if (SkyManager.moonLight != null)
        {
            SkyManager.moonLight.intensity *= factor;

            // Холодный оттенок виден именно на лунном свете: ambient при 6%
            // тьмы уже почти нулевой, и умножение на синеву там не читается.
            float tint = Mathf.Clamp01(RustLighting.NightDepth() * RustSettings.NightTint);
            if (tint > 0.01f)
            {
                Color cold = new Color(0.60f, 0.76f, 1.00f, 1f);
                SkyManager.moonLight.color =
                    Color.Lerp(SkyManager.moonLight.color, cold, tint * 0.85f);
            }
        }
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
        float tintAmount = Mathf.Clamp01(RustLighting.NightDepth() * RustSettings.NightTint);
        if (tintAmount > 0.01f)
        {
            // При сильной тьме цвета уже почти нулевые — умножение на синеву
            // не видно. Поэтому добавляем холодную подсветку абсолютной величиной.
            Color floor = new Color(0.010f, 0.016f, 0.030f, 0f) * tintAmount;
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

internal static class DifMenu
{
    public static void Create()
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
    private Rect windowRect = new Rect(70f, 70f, 460f, 604f);

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

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SetCursorEnabledOverride(false, false);
        }
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
    }

    private void DrawWindow(int id)
    {
        float w = windowRect.width;
        float h = windowRect.height;

        GUI.DrawTexture(new Rect(0f, 0f, w, h), panelTex, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(0f, 0f, w, 3f), accentTex, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(0f, h - 1f, w, 1f), accentTex, ScaleMode.StretchToFill);

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

        y += 34f;

        // Пресеты в один клик.
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("PRESETS", "ПРЕСЕТЫ"), labelStyle);
        y += 22f;

        {
            float bw = (w - 36f - 8f) / 3f;
            if (GUI.Button(new Rect(18f, y, bw, 26f), RustText.T("Soft", "Мягкая"), buttonStyle))
                RustSettings.ApplyPreset(0);
            if (GUI.Button(new Rect(18f + bw + 4f, y, bw, 26f), RustText.T("Standard", "Стандарт"), buttonStyle))
                RustSettings.ApplyPreset(1);
            if (GUI.Button(new Rect(18f + (bw + 4f) * 2f, y, bw, 26f), RustText.T("Rust", "Rust"), buttonStyle))
                RustSettings.ApplyPreset(2);
        }
        y += 36f;

        GUI.enabled = RustSettings.Enabled;
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

        GUI.enabled = true;
        y += 46f;

        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T(
                "Lower is darker. 6% = almost pitch black, 30% = soft night.",
                "Меньше — темнее. 6% — почти полная тьма, 30% — мягкая ночь."),
            hintStyle);
        y += 26f;

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

        y += 46f;

        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T(
                "Moon: 0% = every night alike, 100% = full moon bright, new moon black.",
                "Луна: 0% — все ночи одинаковые, 100% — полная светлая, новая почти чёрная."),
            hintStyle);
        y += 22f;

        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T(
                "Indoors: 0% = same as outside, 100% = almost daylight.",
                "Помещения: 0% — как снаружи, 100% — почти дневной свет."),
            hintStyle);
        y += 22f;

        if (GUI.Button(new Rect(18f, y, 180f, 30f),
            RustText.T("Night now (30 s)", "Ночь сейчас (30 с)"), buttonStyle))
        {
            RustLighting.ForceNight(30f);
        }
        if (GUI.Button(new Rect(206f, y, 90f, 30f), RustText.T("Reset", "Сбросить"), buttonStyle))
        {
            RustSettings.Reset();
        }
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

        string demo = RustLighting.DemoActive
            ? string.Format(RustText.T("  ·  demo {0} s", "  ·  демо {0} с"),
                Mathf.CeilToInt(RustLighting.DemoRemaining))
            : "";

        GUI.Label(
            new Rect(18f, y, w - 36f, 40f),
            string.Format(
                RustText.T("Mode: {0}  ·  time {1}  ·  darkness {2}%  ·  moon {3} {4}{5}",
                           "Режим: {0}  ·  время {1}  ·  тьма {2}%  ·  луна {3} {4}{5}"),
                RustLighting.ModeName(),
                clockText,
                Mathf.RoundToInt(RustLighting.GetFactor() * 100f),
                RustLighting.MoonName(),
                RustLighting.MoonMulText(),
                demo),
            hintStyle
        );

        y += 34f;
        GUI.Label(new Rect(18f, y, w - 36f, 20f),
            RustText.T("NumPad6 / F8 = open menu", "NumPad6 / F8 — открыть меню"), hintStyle);

        GUI.DragWindow(new Rect(0f, 0f, w, 40f));
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
