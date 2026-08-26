using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Entry point. Owns configuration, applies Harmony patches, and keeps a single
    /// persistent manager object alive for the lifetime of the process.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.wingcommand";
        public const string PluginName = "WingCommand";
        public const string PluginVersion = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static Cfg Config2 { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Config2 = new Cfg(Config);

            // Resolve reflection accessors before patching: the radial patches consult
            // GameAccess.Available and quietly stand down if the game layout has moved.
            GameAccess.Initialise();

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            var go = new GameObject("WingCommandManager");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<WingCommandManager>();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }

    /// <summary>
    /// All user-tunable settings. Bound through BepInEx so they show up in
    /// ConfigurationManager (F1) without any extra UI work.
    /// </summary>
    internal class Cfg
    {
        // --- Keys ---
        public readonly ConfigEntry<KeyCode> RadialKey;
        public readonly ConfigEntry<KeyCode> QuickRejoinKey;
        public readonly ConfigEntry<KeyCode> QuickEngageKey;

        // --- Formation ---
        public readonly ConfigEntry<FormationShape> Shape;
        public readonly ConfigEntry<float> SlotSpacing;
        public readonly ConfigEntry<float> SlotStack;
        public readonly ConfigEntry<float> RecruitRange;
        public readonly ConfigEntry<int> MaxWingSize;

        // --- AI ---
        public readonly ConfigEntry<bool> AiTweakEnabled;
        public readonly ConfigEntry<float> AiSkillScale;
        public readonly ConfigEntry<float> AiBraveryScale;
        public readonly ConfigEntry<bool> MutualSupport;

        // --- Debug ---
        public readonly ConfigEntry<bool> EnableDebugActions;

        // --- UI ---
        public readonly ConfigEntry<bool> UseNativeRadial;
        public readonly ConfigEntry<bool> ShowHud;
        public readonly ConfigEntry<bool> MapCommandEnabled;
        public readonly ConfigEntry<bool> UseMfdPanel;
        public readonly ConfigEntry<bool> ShowMapPanel;
        public readonly ConfigEntry<bool> HighlightWingOnMap;
        public readonly ConfigEntry<string> WingIconColor;
        public readonly ConfigEntry<bool> VerboseLogging;

        public Cfg(ConfigFile c)
        {
            RadialKey = c.Bind("Keys", "FallbackRadialMenu", KeyCode.None,
                "Hold to open the standalone fallback radial. Only needed if the native " +
                "wheel integration is turned off or unavailable; leave unbound otherwise.");
            QuickRejoinKey = c.Bind("Keys", "QuickRejoin", KeyCode.None,
                "Optional hotkey: order the whole wing to rejoin formation.");
            QuickEngageKey = c.Bind("Keys", "QuickEngage", KeyCode.None,
                "Optional hotkey: order the whole wing to engage.");

            Shape = c.Bind("Formation", "Shape", FormationShape.EchelonRight,
                "Formation geometry used when wingmen hold station.");
            SlotSpacing = c.Bind("Formation", "SlotSpacing", 120f,
                new ConfigDescription("Lateral/longitudinal spacing between slots, in metres.",
                    new AcceptableValueRange<float>(40f, 600f)));
            SlotStack = c.Bind("Formation", "SlotStack", 20f,
                new ConfigDescription("Vertical stagger per slot, in metres. Keeps wingmen out of each other's wash.",
                    new AcceptableValueRange<float>(0f, 200f)));
            RecruitRange = c.Bind("Formation", "RecruitRange", 12000f,
                new ConfigDescription("Maximum range at which a friendly AI aircraft can be recruited into the wing.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            MaxWingSize = c.Bind("Formation", "MaxWingSize", 3,
                new ConfigDescription("Maximum number of wingmen.",
                    new AcceptableValueRange<int>(1, 8)));

            AiTweakEnabled = c.Bind("AI", "EnableAiTweak", false,
                "Scale AI pilot skill and bravery. Changes vanilla combat feel, so it is off by default.");
            AiSkillScale = c.Bind("AI", "SkillScale", 1.0f,
                new ConfigDescription("Multiplier applied to AI pilot skill (aim accuracy, missile reaction time).",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            AiBraveryScale = c.Bind("AI", "BraveryScale", 1.0f,
                new ConfigDescription("Multiplier applied to AI pilot bravery (target aggression, threat avoidance).",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            MutualSupport = c.Bind("AI", "MutualSupport", true,
                "Wingmen holding formation automatically break to engage when the leader is under missile attack.");

            UseNativeRadial = c.Bind("UI", "UseNativeRadial", true,
                "Add a nested 'Wing Command' entry to the game's own radial menu. This uses " +
                "the game's Rewired look-axis input, which is the only scheme that works " +
                "while the cursor is captured for mouse-look.");
            EnableDebugActions = c.Bind("Debug", "EnableDebugActions", false,
                "Enable the WMC debug buttons: teleport the wing into formation, and spawn " +
                "a wing of your own aircraft type. Both are cheats and are host-only.");

            ShowHud = c.Bind("UI", "ShowWingHud", true,
                "Draw the compact wing status panel in flight while you have wingmen assigned.");
            UseMfdPanel = c.Bind("UI", "UseMfdPanel", true,
                "Add a WMC screen to the cockpit MFD bezel, alongside BDF/MAP/HUD. This is " +
                "the primary wing interface; the map overlay below is only a fallback.");
            ShowMapPanel = c.Bind("UI", "ShowMapOverlayPanel", false,
                "Draw the standalone wing overlay on the maximised map. Redundant while the " +
                "WMC MFD screen is working, so off by default.");
            HighlightWingOnMap = c.Bind("UI", "HighlightWingOnMap", true,
                "Tint your wingmen's map icons so they stand out from the rest of the friendly force.");
            WingIconColor = c.Bind("UI", "WingIconColor", "#33E5FF",
                "Hex colour for wingmen's map icons. Selected members are drawn brighter.");
            MapCommandEnabled = c.Bind("UI", "MapCommands", true,
                "Enable aircraft tasking and squad groups on the maximised map.");
            VerboseLogging = c.Bind("Debug", "VerboseLogging", false,
                "Log every order and state transition to the BepInEx console.");
        }
    }

    internal enum FormationShape
    {
        EchelonRight,
        EchelonLeft,
        LineAbreast,
        Trail,
        CombatSpread,
    }
}
