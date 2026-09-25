using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace WingCommand
{
    internal enum RadioLevel { Off, Essential, Full }

    internal enum RadioVoice { Off, FollowGame, On }

    /// <summary>What the map and HUD mark (spec WMC program §5).</summary>
    internal enum HighlightMode { Off, Wing, WingAndTargets }

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
        public ConfigEntry<bool> ShowWmc { get; }
        public ConfigEntry<HighlightMode> MapMarkers { get; }
        public ConfigEntry<float> HudX { get; }
        public ConfigEntry<float> HudY { get; }
        public ConfigEntry<KeyboardShortcut> KeyCallWingman { get; }
        public ConfigEntry<KeyboardShortcut> KeyFormUp { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextShape { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextSpacing { get; }
        public ConfigEntry<KeyboardShortcut> KeyDismiss { get; }
        public ConfigEntry<KeyboardShortcut> KeyWmcRoom { get; }
        public ConfigEntry<KeyboardShortcut> KeyApLevel { get; }
        public ConfigEntry<KeyboardShortcut> KeyApHeading { get; }
        public ConfigEntry<KeyboardShortcut> KeyApAltitude { get; }
        public ConfigEntry<KeyboardShortcut> KeyApVerticalSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApOff { get; }
        /// <summary>Spec M7 §4: joystick bindings by command name, and the button logger.</summary>
        public System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>[] Hotas { get; }
        public ConfigEntry<bool> HotasLogButtons { get; }

        /// <summary>The commands a joystick button can run (Hotas section order).</summary>
        internal static readonly string[] HotasCommands =
        {
            "CallWingman", "FormUp", "NextShape", "NextSpacing", "Dismiss", "Engage", "Disengage", "AttackTarget", "Splash",
            "ClearMySix", "BogeyDope", "Rtb", "OrbitHere", "GoHigh", "GoLow", "Level", "ApOff",
        };
        public ConfigEntry<bool> PilotProgression { get; }
        public ConfigEntry<float> RankEffect { get; }
        public ConfigEntry<bool> SandboxFreeCalls { get; }
        public ConfigEntry<OverLimitMode> OverLimit { get; }
        public ConfigEntry<bool> TakeoverOnDeath { get; }
        public ConfigEntry<float> RecruitmentCostRate { get; }
        public ConfigEntry<string> LoadoutTemplates { get; }
        public ConfigEntry<WinchesterAction> AfterWinchester { get; }
        public ConfigEntry<BingoAction> AfterBingo { get; }
        public ConfigEntry<float> FallBackRatio { get; }
        public ConfigEntry<string> Doctrine { get; }
        public ConfigEntry<RadioLevel> Radio { get; }
        public ConfigEntry<RadioVoice> RadioVoiceMode { get; }
        public ConfigEntry<bool> ContactCalls { get; }
        public ConfigEntry<string> VoicePacks { get; }
        public ConfigEntry<float> VoicePackVolume { get; }
        public ConfigEntry<bool> VerboseLogging { get; }
        public ConfigEntry<bool> DevTools { get; }
        public ConfigEntry<bool> Overlay { get; }
        public ConfigEntry<KeyboardShortcut> KeyDumpTelemetry { get; }
        public ConfigEntry<KeyboardShortcut> KeyStepTest { get; }

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

            PilotProgression = c.Bind("Squadron", "PilotProgression", true, new ConfigDescription(
                "Pilots earn XP, ranks and perks, and fly better with rank.", null, new ConfigurationManagerAttributes { Order = 85 }));
            RankEffect = c.Bind("Squadron", "RankEffect", 1f, new ConfigDescription(
                "How much rank changes how a pilot flies and fights (0 off, 1 normal, 2 double).",
                new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 84 }));
            SandboxFreeCalls = c.Bind("Squadron", "SandboxFreeCalls", false, new ConfigDescription(
                "Calls cost nothing: no allocation is charged and no stock is checked (hangar spawns still draw the " +
                "faction's own supply, as the game does).", null, new ConfigurationManagerAttributes { Order = 83 }));

            OverLimit = c.Bind("Supply", "OverLimit", OverLimitMode.Surcharge, new ConfigDescription(
                "A requisition over the faction's AI aircraft limit (the game's own; your wingmen count toward it): Surcharge costs " +
                "three times the airframe's value; MatchEnemy keeps the price but lets every enemy faction field one more AI " +
                "aircraft; RtbOne keeps the price and sends one of the faction's own AI (never a wingman) to land to make room.",
                null, new ConfigurationManagerAttributes { Order = 80 }));
            TakeoverOnDeath = c.Bind("Squadron", "TakeoverOnDeath", true, new ConfigDescription(
                "When you are shot down or eject, offer to fly on in one of your wingmen's aircraft (host or single player).",
                null, new ConfigurationManagerAttributes { Order = 82 }));
            RecruitmentCostRate = c.Bind("Squadron", "RecruitmentCostRate", 0.25f, new ConfigDescription(
                "Taking command of a faction aircraft already flying costs this share of its value, once per aircraft.",
                new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 81 }));
            AfterWinchester = c.Bind("Combat", "AfterWinchester", WinchesterAction.Rejoin, new ConfigDescription(
                "A wingman out of ammunition in a fight: Rejoin the formation, Rtb (land and return to the reserve), or Refit " +
                "(land, rearm and take off again).", null, new ConfigurationManagerAttributes { Order = 90 }));
            AfterBingo = c.Bind("Combat", "AfterBingo", BingoAction.Rtb, new ConfigDescription(
                "A wingman at bingo fuel: Rtb (land and return to the reserve) or Refit (land, refuel and take off again).",
                null, new ConfigurationManagerAttributes { Order = 89 }));
            FallBackRatio = c.Bind("Combat", "FallBackRatio", 2f, new ConfigDescription(
                "An engaged wing facing this many enemy aircraft per fighting wingman falls back into formation; Engage while " +
                "outnumbered asks you to press it again. 0 turns it off.",
                new AcceptableValueRange<float>(0f, 10f), new ConfigurationManagerAttributes { Order = 88 }));
            Doctrine = c.Bind("Combat", "Doctrine", "Reserve", new ConfigDescription(
                "What wingmen shoot at while holding formation: Reserve (hold fire), Escort (aircraft threatening you), Sweep " +
                "(targets of opportunity, long range), or a custom line guard,response,interval,spread,targets,reach. Cycle it " +
                "from the radial Combat page.", null, new ConfigurationManagerAttributes { Order = 87 }));
            Radio = c.Bind("Radio", "Level", RadioLevel.Full, new ConfigDescription(
                "Wingman radio calls: Off, Essential (emergencies, tactical and status calls) or Full (also chatter such as " +
                "touchdowns).", null, new ConfigurationManagerAttributes { Order = 80 }));
            RadioVoiceMode = c.Bind("Radio", "Voice", RadioVoice.FollowGame, new ConfigDescription(
                "Speak wingman calls with the game's text-to-speech: Off, On, or FollowGame (on when the game's chat " +
                "text-to-speech is on; its speed and volume are used either way).", null, new ConfigurationManagerAttributes { Order = 79 }));
            VoicePacks = c.Bind("Radio", "VoicePacks", "", new ConfigDescription(
                "Yappinator-format voice packs for wingman calls, comma-separated (wingman #2 uses the first, #3 the second, " +
                "round robin). Packs are folders under config/WingCommand/v1/voicepacks or Yappinator's plugins/WSOYappinator/audio. " +
                "Calls a pack has no clip for use the text-to-speech. Empty: no packs.", null, new ConfigurationManagerAttributes { Order = 77 }));
            VoicePackVolume = c.Bind("Radio", "VoicePackVolume", 0.8f, new ConfigDescription(
                "Voice pack volume.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 76 }));
            ContactCalls = c.Bind("Radio", "ContactCalls", true, new ConfigDescription(
                "Wingmen call new enemy aircraft within 40 km with bearing, range, altitude and aspect from you. Scout Ahead " +
                "reports ground contacts either way.", null, new ConfigurationManagerAttributes { Order = 78 }));
            LoadoutTemplates = c.Bind("Loadout", "SavedTemplates", "", new ConfigDescription(
                "Saved per-pylon loadout templates (airframe|id|name|store keys; records separated by semicolons). " +
                "Clear it to delete every template.", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 60 }));

            ShowHud = c.Bind("Hud", "Show", true, new ConfigDescription(
                "Show the wing strip and autopilot annunciator.", null, new ConfigurationManagerAttributes { Order = 80 }));
            HudX = c.Bind("Hud", "OffsetX", 0f, new ConfigDescription(
                "Move the wing strip right (+) or left (-), in HUD pixels.", new AcceptableValueRange<float>(-1500f, 1500f),
                new ConfigurationManagerAttributes { Order = 79 }));
            HudY = c.Bind("Hud", "OffsetY", 0f, new ConfigDescription(
                "Move the wing strip up (+) or down (-), in HUD pixels.", new AcceptableValueRange<float>(-1000f, 1000f),
                new ConfigurationManagerAttributes { Order = 78 }));
            ShowWmc = c.Bind("Wmc", "Show", true, new ConfigDescription(
                "Show the WMC panel on a map bezel button (maximized map).", null, new ConfigurationManagerAttributes { Order = 70 }));
            MapMarkers = c.Bind("Wmc", "MapMarkers", HighlightMode.WingAndTargets, new ConfigDescription(
                "Mark wingmen (element colour and badge) and, with WingAndTargets, the wing's targets on map and HUD icons.",
                null, new ConfigurationManagerAttributes { Order = 69 }));

            KeyCallWingman = Key(c, "CallWingman", "Call one wingman (Wing/CallAirframe, or your type).", 50);
            KeyFormUp = Key(c, "FormUp", "Every wingman rejoins now.", 49);
            KeyNextShape = Key(c, "NextShape", "Next formation shape in the family.", 48);
            KeyNextSpacing = Key(c, "NextSpacing", "Next spacing preset (Close, Standard, Open, Spread).", 47);
            KeyDismiss = Key(c, "Dismiss", "Release every wingman to the game's AI.", 46);
            KeyWmcRoom = Key(c, "WmcRoom", "Open or close the Wing Command room (the full-screen WMC).", 45);
            KeyApLevel = Key(c, "AutopilotLevel", "Autopilot: wings level.", 45);
            KeyApHeading = Key(c, "AutopilotHeading", "Autopilot: hold the current heading.", 44);
            KeyApAltitude = Key(c, "AutopilotAltitude", "Autopilot: hold the current altitude.", 43);
            KeyApVerticalSpeed = Key(c, "AutopilotVerticalSpeed", "Autopilot: hold the current vertical speed.", 42);
            KeyApSpeed = Key(c, "AutopilotSpeed", "Autopilot: toggle speed hold at the current speed.", 41);
            KeyApOff = Key(c, "AutopilotOff", "Autopilot: all holds off.", 40);

            Hotas = new System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>[HotasCommands.Length];
            for (int i = 0; i < HotasCommands.Length; i++)
                Hotas[i] = new System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>(HotasCommands[i],
                    c.Bind("Hotas", HotasCommands[i], "", new ConfigDescription(
                        "Joystick button for " + HotasCommands[i] + ": <device>:<button>, e.g. \"T.16000M:5\" or \"any:5\" " +
                        "(part of the joystick's name, button counted from 1). Empty: unbound. Turn on LogButtons to find them.",
                        null, new ConfigurationManagerAttributes { Order = 30 - i })));
            HotasLogButtons = c.Bind("Hotas", "LogButtons", false, new ConfigDescription(
                "Write every joystick button press to the log as \"[Hotas] <joystick>: button <n>\" (to find names and numbers).",
                null, new ConfigurationManagerAttributes { Order = 31 }));

            DevTools = c.Bind("Debug", "DevTools", false, new ConfigDescription(
                "Enable developer tools: debug overlay, telemetry recorder, step tests and calibration.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 70 }));

            Overlay = c.Bind("Debug", "Overlay", true, new ConfigDescription(
                "With DevTools on, draw each wingman's slot (green), tracked reference (yellow), velocity command (cyan) " +
                "and collision bias (red).", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 69 }));
            KeyDumpTelemetry = c.Bind("Debug", "DumpTelemetry", KeyboardShortcut.Empty, new ConfigDescription(
                "With DevTools on, write the last 120 s of wing telemetry to v1/telemetry.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true, Order = 68 }));

            KeyStepTest = c.Bind("Debug", "StepTest", KeyboardShortcut.Empty, new ConfigDescription(
                "With DevTools on, fly wingman #2 through a 38 s step test (above 1500 m; it recovers between short stick pulses) and calibrate its airframe.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 67 }));

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
