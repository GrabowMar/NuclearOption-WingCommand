using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace WingCommand
{
    /// <summary>Wing Command 1.0 settings. The first 1.0 launch archives the 0.9 file beside itself so every
    /// key starts from its 1.0 default (BepInEx only applies defaults to keys it has not seen). Data files
    /// (formations, profiles, roster) live under <see cref="DataRoot"/>.</summary>
    internal sealed class WingConfig
    {
        internal const int SchemaVersion = 1;
        private const string SchemaMarker = "SchemaVersion = 1";

        internal static string DataRoot => Path.Combine(Paths.ConfigPath, "WingCommand", "v1");

        public ConfigEntry<WingMode> Mode { get; }
        public ConfigEntry<string> DefaultFormation { get; }
        public ConfigEntry<SpacingPreset> DefaultSpacing { get; }
        public ConfigEntry<int> MaxWingmen { get; }
        public ConfigEntry<string> CallAirframe { get; }
        public ConfigEntry<bool> ShowHud { get; }
        public ConfigEntry<float> HudX { get; }
        public ConfigEntry<float> HudY { get; }
        public ConfigEntry<KeyboardShortcut> KeyCallWingman { get; }
        public ConfigEntry<KeyboardShortcut> KeyFormUp { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextShape { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextSpacing { get; }
        public ConfigEntry<KeyboardShortcut> KeyDismiss { get; }
        public ConfigEntry<KeyboardShortcut> KeyApLevel { get; }
        public ConfigEntry<KeyboardShortcut> KeyApHeading { get; }
        public ConfigEntry<KeyboardShortcut> KeyApAltitude { get; }
        public ConfigEntry<KeyboardShortcut> KeyApVerticalSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApOff { get; }
        public ConfigEntry<bool> VerboseLogging { get; }
        public ConfigEntry<bool> DevTools { get; }
        public ConfigEntry<bool> Overlay { get; }
        public ConfigEntry<KeyboardShortcut> KeyDumpTelemetry { get; }

        public WingConfig(ConfigFile c)
        {
            ArchiveLegacy(c);
            c.Bind("Meta", "SchemaVersion", SchemaVersion, new ConfigDescription(
                "Settings schema written by Wing Command. Do not edit.", null,
                new ConfigurationManagerAttributes { Browsable = false }));

            Mode = c.Bind("AI", "Mode", WingMode.Smart, new ConfigDescription(
                "Smart runs the full AI. Performance halves guidance rate and decision cadence for AI-led " +
                "wings (player-led formations always run at full rate). Applies at the next mission start.",
                null, new ConfigurationManagerAttributes { Order = 100 }));

            DefaultFormation = c.Bind("Wing", "DefaultFormation", "finger-four-right", new ConfigDescription(
                "Formation a new wing flies: an id from formations.json (for example finger-four-right, combat-spread).",
                null, new ConfigurationManagerAttributes { Order = 90 }));
            DefaultSpacing = c.Bind("Wing", "DefaultSpacing", SpacingPreset.Standard, new ConfigDescription(
                "Spacing a new wing flies: Close 40 m, Standard 80 m, Open 160 m, Spread 350 m (clamped to the shape).",
                null, new ConfigurationManagerAttributes { Order = 89 }));
            MaxWingmen = c.Bind("Wing", "MaxWingmen", 3, new ConfigDescription(
                "Most wingmen you can call (host only).", new AcceptableValueRange<int>(1, 3),
                new ConfigurationManagerAttributes { Order = 88 }));
            CallAirframe = c.Bind("Wing", "CallAirframe", "", new ConfigDescription(
                "Airframe to call, by unit name (for example FS-20). Empty calls your own type. Fixed-wing only in this build.",
                null, new ConfigurationManagerAttributes { Order = 87 }));

            ShowHud = c.Bind("Hud", "Show", true, new ConfigDescription(
                "Show the wing strip and autopilot annunciator.", null, new ConfigurationManagerAttributes { Order = 80 }));
            HudX = c.Bind("Hud", "OffsetX", 0f, new ConfigDescription(
                "Move the wing strip right (+) or left (-), in HUD pixels.", new AcceptableValueRange<float>(-1500f, 1500f),
                new ConfigurationManagerAttributes { Order = 79 }));
            HudY = c.Bind("Hud", "OffsetY", 0f, new ConfigDescription(
                "Move the wing strip up (+) or down (-), in HUD pixels.", new AcceptableValueRange<float>(-1000f, 1000f),
                new ConfigurationManagerAttributes { Order = 78 }));

            KeyCallWingman = Key(c, "CallWingman", "Call one wingman (Wing/CallAirframe, or your type).", 50);
            KeyFormUp = Key(c, "FormUp", "Every wingman rejoins now.", 49);
            KeyNextShape = Key(c, "NextShape", "Next formation shape in the family.", 48);
            KeyNextSpacing = Key(c, "NextSpacing", "Next spacing preset (Close, Standard, Open, Spread).", 47);
            KeyDismiss = Key(c, "Dismiss", "Release every wingman to the game's AI.", 46);
            KeyApLevel = Key(c, "AutopilotLevel", "Autopilot: wings level.", 45);
            KeyApHeading = Key(c, "AutopilotHeading", "Autopilot: hold the current heading.", 44);
            KeyApAltitude = Key(c, "AutopilotAltitude", "Autopilot: hold the current altitude.", 43);
            KeyApVerticalSpeed = Key(c, "AutopilotVerticalSpeed", "Autopilot: hold the current vertical speed.", 42);
            KeyApSpeed = Key(c, "AutopilotSpeed", "Autopilot: toggle speed hold at the current speed.", 41);
            KeyApOff = Key(c, "AutopilotOff", "Autopilot: all holds off.", 40);

            DevTools = c.Bind("Debug", "DevTools", false, new ConfigDescription(
                "Enable developer tools: debug overlay, telemetry recorder, step tests and calibration.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 70 }));

            Overlay = c.Bind("Debug", "Overlay", true, new ConfigDescription(
                "With DevTools on, draw each wingman's slot (green), tracked reference (yellow), velocity command (cyan) " +
                "and collision bias (red).", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 69 }));
            KeyDumpTelemetry = c.Bind("Debug", "DumpTelemetry", KeyboardShortcut.Empty, new ConfigDescription(
                "With DevTools on, write the last 120 s of wing telemetry to v1/telemetry.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true, Order = 68 }));

            c.Bind("Debug", "ExportLogs", false, new ConfigDescription(
                "Export the latest Wing Command log events from this session beside WingCommand.dll " +
                "(WingCommand-logs.txt). Safe diagnostics only; no upload.", null,
                new ConfigurationManagerAttributes
                {
                    DispName = "Export logs",
                    CustomDrawer = WingLogExport.DrawButton,
                    HideDefaultButton = true,
                    Order = 65,
                }));

            VerboseLogging = c.Bind("Debug", "VerboseLogging", false, new ConfigDescription(
                "Log decisions, state transitions and flight diagnostics. Applies immediately.",
                null, new ConfigurationManagerAttributes { DispName = "Debug action logging", Order = 60 }));

            Directory.CreateDirectory(DataRoot);
        }

        private static ConfigEntry<KeyboardShortcut> Key(ConfigFile c, string name, string what, int order) =>
            c.Bind("Keys", name, KeyboardShortcut.Empty, new ConfigDescription(what + " Unbound by default.", null,
                new ConfigurationManagerAttributes { Order = order }));

        /// <summary>Move a pre-1.0 settings file aside once, then reload the now-empty file so orphaned
        /// 0.9 keys and values cannot leak into 1.0.</summary>
        private static void ArchiveLegacy(ConfigFile c)
        {
            string path = c.ConfigFilePath;
            if (!File.Exists(path)) return;
            if (File.ReadAllText(path).Contains(SchemaMarker)) return;
            File.Copy(path, path + ".0.9.bak", overwrite: true);
            File.WriteAllText(path, string.Empty);
            c.Reload();
        }
    }
}
