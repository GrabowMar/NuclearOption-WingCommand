using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Reflection;
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
        public const string PluginVersion = "0.3.0";

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
            WingHudTint.Initialise();

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            ReportPatches();

            var go = new GameObject("WingCommandManager");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<WingCommandManager>();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            // Log what is actually in force, not what the defaults say.
            //
            // BepInEx only applies a default to a *new* key: an existing config file keeps
            // its own value forever. Changing a default in code therefore does nothing for
            // anyone who has already run the mod, which silently left tuning changes
            // unapplied and a feature enabled long after it was supposedly turned off.
            Logger.LogInfo(
                "Effective tuning: " +
                $"ClosureAuthority={Config2.ClosureAuthority.Value} " +
                $"ClosureDamping={Config2.ClosureDamping.Value} " +
                $"ThrottleGain={Config2.ThrottleGain.Value} " +
                $"ThrottleBaseline={Config2.ThrottleBaseline.Value} " +
                $"CaptureDistance={Config2.CaptureDistance.Value} " +
                $"BankMatching={Config2.BankMatching.Value} " +
                $"WidenUnderThreat={Config2.WidenUnderThreat.Value} " +
                $"RotaryLookAheadSeconds={Config2.RotaryLookAheadSeconds.Value} " +
                $"RotaryMinLookAhead={Config2.RotaryMinLookAhead.Value} " +
                $"RotarySpacingScale={Config2.RotarySpacingScale.Value}");

            if (Config2.ThrottleBaseline.Value < 0.99f)
            {
                Logger.LogWarning(
                    "ThrottleBaseline is below 1. cruiseThrottle is a feed-forward term, so " +
                    "this makes wingmen settle permanently slower than commanded and fall " +
                    "behind. Set it to 1 unless you are deliberately experimenting.");
            }
        }

        /// <summary>
        /// Report what Harmony actually patched.
        ///
        /// A patch class with no class-level <c>[HarmonyPatch]</c> is skipped in total
        /// silence: <c>PatchClassProcessor</c> returns before it looks at a single method
        /// attribute. Map tinting shipped that way and did nothing for weeks, with no
        /// error anywhere to say so. Listing the patched methods at startup turns that
        /// failure from invisible into a line in the log.
        /// </summary>
        private void ReportPatches()
        {
            var patched = new List<MethodBase>(harmony.GetPatchedMethods());
            var names = new List<string>(patched.Count);
            foreach (MethodBase m in patched)
            {
                if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
            }

            names.Sort(System.StringComparer.Ordinal);
            Logger.LogInfo($"Harmony patched {names.Count} method(s): {string.Join(", ", names.ToArray())}");

            // Named so a future game update that moves one of these is reported as a
            // missing patch rather than as a feature that silently stopped working.
            string[] expected =
            {
                "MapIcon.UpdateColor",
                "HUDUnitMarker.UpdateColor",
                "AIPilotCombatModes.EnterState",
            };

            foreach (string want in expected)
            {
                if (!names.Contains(want))
                    Logger.LogWarning($"Expected Harmony patch missing: {want}");
            }
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
        public readonly ConfigEntry<float> SlowingRadius;
        public readonly ConfigEntry<float> ClosureAuthority;
        public readonly ConfigEntry<float> ClosureDamping;
        public readonly ConfigEntry<float> ThrottleGain;
        public readonly ConfigEntry<float> ThrottleBaseline;
        public readonly ConfigEntry<float> SeparationRadius;
        public readonly ConfigEntry<float> SeparationStrength;
        public readonly ConfigEntry<float> CaptureDistance;
        public readonly ConfigEntry<float> StationLookAhead;
        public readonly ConfigEntry<float> StationMaxCorrection;
        public readonly ConfigEntry<float> StationDeadband;
        public readonly ConfigEntry<float> StationBank;
        public readonly ConfigEntry<float> StationDamping;
        public readonly ConfigEntry<float> RotarySpacingScale;
        public readonly ConfigEntry<float> RotaryHoverSpeed;
        public readonly ConfigEntry<float> RotaryCrossGain;
        public readonly ConfigEntry<float> RotaryMaxCross;
        public readonly ConfigEntry<float> RotaryStationDistance;
        public readonly ConfigEntry<float> RotaryLookAheadSeconds;
        public readonly ConfigEntry<float> RotaryMinLookAhead;
        public readonly ConfigEntry<float> AvoidanceSmoothing;
        public readonly ConfigEntry<float> RejoinStagger;
        public readonly ConfigEntry<bool> WidenUnderThreat;
        public readonly ConfigEntry<float> ThreatSpacingScale;
        public readonly ConfigEntry<bool> BankMatching;
        public readonly ConfigEntry<float> BankMatchStrength;

        // --- AI ---
        public readonly ConfigEntry<bool> AiTweakEnabled;
        public readonly ConfigEntry<float> AiSkillScale;
        public readonly ConfigEntry<float> AiBraveryScale;
        public readonly ConfigEntry<bool> MutualSupport;

        // --- Engagement ---
        public readonly ConfigEntry<WingPosture> DefaultPosture;
        public readonly ConfigEntry<bool> MissileDefence;
        public readonly ConfigEntry<float> DefensiveEngageRange;
        public readonly ConfigEntry<float> AggressiveEngageRange;
        public readonly ConfigEntry<float> LeashRadius;
        public readonly ConfigEntry<float> MirrorWindowSeconds;
        public readonly ConfigEntry<float> FireInterval;
        public readonly ConfigEntry<bool> AutoReturnOnEmpty;
        public readonly ConfigEntry<float> BingoFuel;

        // --- Station keeping (safety) ---
        public readonly ConfigEntry<float> PathCutLookAhead;
        public readonly ConfigEntry<float> PathCutRadius;
        public readonly ConfigEntry<float> PathCutStrength;

        // --- Comms ---
        public readonly ConfigEntry<bool> RadioChatter;

        // --- Capability ---
        public readonly ConfigEntry<bool> ReportUnableToKeepUp;
        public readonly ConfigEntry<float> UnableDistance;
        public readonly ConfigEntry<float> UnableSeconds;
        public readonly ConfigEntry<bool> WarnOnSlowRecruit;

        // --- Debug ---
        public readonly ConfigEntry<bool> EnableDebugActions;

        // --- UI ---
        public readonly ConfigEntry<bool> UseNativeRadial;
        public readonly ConfigEntry<bool> ShowHud;
        public readonly ConfigEntry<HudCorner> HudCorner;
        public readonly ConfigEntry<bool> MapCommandEnabled;
        public readonly ConfigEntry<bool> UseMfdPanel;
        public readonly ConfigEntry<bool> ShowMapPanel;
        public readonly ConfigEntry<bool> HighlightWingOnMap;
        public readonly ConfigEntry<bool> HighlightWingOnHud;
        public readonly ConfigEntry<bool> HighlightWingTargets;
        public readonly ConfigEntry<string> WingIconColor;
        public readonly ConfigEntry<string> WingTargetColor;
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

            SlowingRadius = c.Bind("Formation", "SlowingRadius", 300f,
                new ConfigDescription(
                    "Arrival distance, in metres. Closure speed ramps down inside this radius " +
                    "instead of being proportional to error. Smaller settles harder but risks " +
                    "hunting; larger is gentler but slower to close.",
                    new AcceptableValueRange<float>(50f, 1500f)));
            ClosureAuthority = c.Bind("Formation", "ClosureAuthority", 0.9f,
                new ConfigDescription(
                    "How much faster or slower than the leader a wingman may fly while closing, " +
                    "as a fraction of the leader's speed. Higher closes gaps faster; the damping " +
                    "term below is what keeps that from overshooting.",
                    new AcceptableValueRange<float>(0.1f, 2f)));
            ClosureDamping = c.Bind("Formation", "ClosureDamping", 0.4f,
                new ConfigDescription(
                    "Arrests closure early by subtracting the closing rate, the way a pilot pulls " +
                    "power before arriving rather than at the slot. Raise if wingmen overshoot " +
                    "fore-and-aft, lower if they are slow to close.",
                    new AcceptableValueRange<float>(0f, 4f)));
            ThrottleGain = c.Bind("Formation", "ThrottleGain", 0.12f,
                new ConfigDescription("Throttle applied per m/s of speed error.",
                    new AcceptableValueRange<float>(0.02f, 0.4f)));
            ThrottleBaseline = c.Bind("Formation", "ThrottleBaseline", 1.0f,
                new ConfigDescription(
                    "Resting throttle as a fraction of the airframe's cruise setting. Leave at " +
                    "1: cruiseThrottle is the feed-forward term, the power needed to hold " +
                    "cruise, so anything below 1 makes a wingman settle permanently slower " +
                    "than commanded and fall steadily behind.",
                    new AcceptableValueRange<float>(0.3f, 1f)));
            SeparationRadius = c.Bind("Formation", "SeparationRadius", 90f,
                new ConfigDescription("Distance at which wingmen start pushing away from each other.",
                    new AcceptableValueRange<float>(0f, 400f)));
            SeparationStrength = c.Bind("Formation", "SeparationStrength", 12f,
                new ConfigDescription("Strength of that push, in metres of slot displacement.",
                    new AcceptableValueRange<float>(0f, 100f)));

            CaptureDistance = c.Bind("Formation", "CaptureDistance", 500f,
                new ConfigDescription(
                    "Inside this distance a wingman stops chasing the slot and flies parallel " +
                    "to you with small corrections. Raising it makes them settle sooner and " +
                    "more gently; lowering it makes them chase harder for longer.",
                    new AcceptableValueRange<float>(100f, 2000f)));
            StationLookAhead = c.Bind("Formation", "StationLookAhead", 1200f,
                new ConfigDescription(
                    "How far ahead a settled wingman aims. Larger is calmer, because the same " +
                    "positional correction becomes a smaller steering angle.",
                    new AcceptableValueRange<float>(300f, 4000f)));
            StationMaxCorrection = c.Bind("Formation", "StationMaxCorrection", 220f,
                new ConfigDescription(
                    "Largest sideways correction a settled wingman will command. Together with " +
                    "the look-ahead this caps how sharply it can manoeuvre while in formation.",
                    new AcceptableValueRange<float>(20f, 1000f)));
            StationDeadband = c.Bind("Formation", "StationDeadband", 8f,
                new ConfigDescription("Slot error below which a wingman stops correcting at all.",
                    new AcceptableValueRange<float>(0f, 60f)));
            StationBank = c.Bind("Formation", "StationBank", 45f,
                new ConfigDescription(
                    "Bank angle a settled wingman is allowed. This is the main authority knob: " +
                    "the autopilot ignores the effort parameter at these values.",
                    new AcceptableValueRange<float>(10f, 180f)));
            RotarySpacingScale = c.Bind("Formation", "RotarySpacingScale", 0.55f,
                new ConfigDescription(
                    "Slot spacing multiplier for helicopter formations, which fly far closer " +
                    "together than jets.",
                    new AcceptableValueRange<float>(0.2f, 2f)));
            RotaryLookAheadSeconds = c.Bind("Formation", "RotaryLookAheadSeconds", 20f,
                new ConfigDescription(
                    "Seconds of travel a settled helicopter aims ahead. Twenty is not " +
                    "arbitrary: AutopilotHelo sets collective from " +
                    "0.5 + distance*0.001 - speed*0.02, so a look-ahead of 20 x speed makes " +
                    "those terms cancel and collective rest at hover, which is what lets a " +
                    "helicopter sustain any speed. Lower it and they cannot hold pace.",
                    new AcceptableValueRange<float>(5f, 60f)));
            RotaryMinLookAhead = c.Bind("Formation", "RotaryMinLookAhead", 600f,
                new ConfigDescription(
                    "Floor for that look-ahead, in metres. AutopilotHelo places its own " +
                    "forward-flight waypoint at least 600 m out, so aiming nearer than that " +
                    "puts the waypoint beyond the slot and the aircraft circles it.",
                    new AcceptableValueRange<float>(200f, 2000f)));
            RotaryHoverSpeed = c.Bind("Formation", "RotaryHoverSpeed", 25f,
                new ConfigDescription(
                    "Leader speed below which helicopters hold their slot as a point using " +
                    "Autopilot.Hover, and above which they fly the leader's heading instead. " +
                    "Hover is precise but tilts at most about seventeen degrees, so it cannot " +
                    "cruise; the two regimes fly genuinely differently.",
                    new AcceptableValueRange<float>(0f, 80f)));
            RotaryCrossGain = c.Bind("Formation", "RotaryCrossGain", 2.5f,
                new ConfigDescription("How hard a cruising helicopter steers onto its slot line.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            RotaryMaxCross = c.Bind("Formation", "RotaryMaxCross", 250f,
                new ConfigDescription("Cap on that sideways correction, in metres.",
                    new AcceptableValueRange<float>(30f, 1000f)));
            RotaryStationDistance = c.Bind("Formation", "RotaryStationDistance", 200f,
                new ConfigDescription(
                    "Slot error below which a cruising helicopter holds the leader's heading " +
                    "rather than pointing where it is going.",
                    new AcceptableValueRange<float>(20f, 1000f)));
            StationDamping = c.Bind("Formation", "StationDamping", 1.6f,
                new ConfigDescription(
                    "Damps the station-keeping correction against drift rate. Without it the " +
                    "correction overshoots and reverses, which shows up as a slow left-right " +
                    "rocking. Raise it if they still rock, lower it if they feel sluggish.",
                    new AcceptableValueRange<float>(0f, 6f)));
            RejoinStagger = c.Bind("Formation", "RejoinStagger", 1.2f,
                new ConfigDescription(
                    "Seconds of delay per slot when rejoining, so the flight arrives in sequence " +
                    "instead of converging on you all at once. Zero restores the old behaviour.",
                    new AcceptableValueRange<float>(0f, 6f)));
            WidenUnderThreat = c.Bind("Formation", "WidenUnderThreat", false,
                "Open the formation up when the wing is Aggressive or you are being shot at, " +
                "and close it again when clear. A tight formation is easy to shoot at and " +
                "leaves nobody room to manoeuvre.");
            ThreatSpacingScale = c.Bind("Formation", "ThreatSpacingScale", 2.2f,
                new ConfigDescription("How far the formation opens under threat.",
                    new AcceptableValueRange<float>(1f, 5f)));
            BankMatching = c.Bind("Formation", "BankMatching", false,
                "Wingmen roll with you once settled instead of staying wings-level. Cosmetic, " +
                "but wings-level wingmen through a banked turn are the clearest giveaway that " +
                "a formation is simulated rather than flown. Off by default: an earlier version " +
                "of this drove wingmen inverted into the ground, so it needs to earn trust.");
            BankMatchStrength = c.Bind("Formation", "BankMatchStrength", 0.35f,
                new ConfigDescription(
                    "How much of the roll command bank matching may take from the autopilot. " +
                    "High values fight it and make wingmen wallow.",
                    new AcceptableValueRange<float>(0f, 1f)));
            AvoidanceSmoothing = c.Bind("Formation", "AvoidanceSmoothing", 0.4f,
                new ConfigDescription(
                    "Seconds over which separation and path-avoidance pushes ease in, so they " +
                    "do not step the target position.",
                    new AcceptableValueRange<float>(0.05f, 3f)));

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

            DefaultPosture = c.Bind("Engagement", "DefaultPosture", WingPosture.Defensive,
                "Rules of engagement the wing starts a mission with. Defensive holds the slot " +
                "no matter what; Aggressive breaks to fight aircraft and rejoins afterwards.");
            MissileDefence = c.Bind("Engagement", "MissileDefence", true,
                "Defensive wingmen prioritise shooting down inbound missiles, on themselves or " +
                "on you, when they carry a weapon capable of it.");
            DefensiveEngageRange = c.Bind("Engagement", "DefensiveEngageRange", 6000f,
                new ConfigDescription(
                    "How far a Defensive wingman will shoot from its slot, in metres. It never " +
                    "manoeuvres to engage, so this is purely a weapons-range limit.",
                    new AcceptableValueRange<float>(500f, 30000f)));
            AggressiveEngageRange = c.Bind("Engagement", "AggressiveEngageRange", 12000f,
                new ConfigDescription("Range at which an Aggressive wingman will break formation to fight.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            LeashRadius = c.Bind("Engagement", "LeashRadius", 8000f,
                new ConfigDescription(
                    "How far an Aggressive wingman may stray from you before it abandons the " +
                    "fight and rejoins. This is what stops the wing dispersing.",
                    new AcceptableValueRange<float>(1000f, 40000f)));
            MirrorWindowSeconds = c.Bind("Engagement", "MirrorWindowSeconds", 15f,
                new ConfigDescription(
                    "How long Defensive wingmen stay weapons-free against ground targets after " +
                    "you fire an anti-surface weapon.",
                    new AcceptableValueRange<float>(2f, 120f)));
            FireInterval = c.Bind("Engagement", "FireInterval", 5f,
                new ConfigDescription(
                    "Minimum seconds between shots from one wingman. Without a gap they fire " +
                    "every engagement tick and empty the aircraft in seconds.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            AutoReturnOnEmpty = c.Bind("Engagement", "AutoReturnOnEmpty", true,
                "Wingmen return to base on their own once out of ammunition or down to bingo fuel.");
            BingoFuel = c.Bind("Engagement", "BingoFuel", 0.15f,
                new ConfigDescription("Fuel fraction at which a wingman calls bingo and heads home.",
                    new AcceptableValueRange<float>(0.05f, 0.5f)));

            PathCutLookAhead = c.Bind("Formation", "PathCutLookAhead", 400f,
                new ConfigDescription(
                    "Length of the protected corridor ahead of you, in metres. Wingmen steer " +
                    "around it rather than crossing your nose to reach a slot behind you.",
                    new AcceptableValueRange<float>(0f, 2000f)));
            PathCutRadius = c.Bind("Formation", "PathCutRadius", 120f,
                new ConfigDescription("Half-width of that corridor.",
                    new AcceptableValueRange<float>(10f, 500f)));
            PathCutStrength = c.Bind("Formation", "PathCutStrength", 200f,
                new ConfigDescription("How hard wingmen are pushed out of your path.",
                    new AcceptableValueRange<float>(0f, 800f)));

            ReportUnableToKeepUp = c.Bind("Formation", "ReportUnableToKeepUp", true,
                "A wingman that keeps losing ground on its slot gives up and returns to base " +
                "instead of chasing until it crashes. Mainly affects slow aircraft recruited " +
                "into a fast flight.");
            UnableDistance = c.Bind("Formation", "UnableDistance", 3000f,
                new ConfigDescription("How far out counts as failing to hold station.",
                    new AcceptableValueRange<float>(500f, 20000f)));
            UnableSeconds = c.Bind("Formation", "UnableSeconds", 20f,
                new ConfigDescription("How long it must keep losing ground before giving up.",
                    new AcceptableValueRange<float>(5f, 120f)));
            WarnOnSlowRecruit = c.Bind("Formation", "WarnOnSlowRecruit", true,
                "Warn when recruiting an aircraft far slower than yours - a helicopter cannot " +
                "hold formation on a jet.");

            RadioChatter = c.Bind("Comms", "RadioChatter", true,
                "Wingmen report engagements, defending, Winchester and rejoins in the game's " +
                "on-screen message feed.");

            UseNativeRadial = c.Bind("UI", "UseNativeRadial", true,
                "Add a nested 'Wing Command' entry to the game's own radial menu. This uses " +
                "the game's Rewired look-axis input, which is the only scheme that works " +
                "while the cursor is captured for mouse-look.");
            EnableDebugActions = c.Bind("Debug", "EnableDebugActions", false,
                "Enable the WMC debug buttons: teleport the wing into formation, and spawn " +
                "a wing of your own aircraft type. Both are cheats and are host-only.");

            ShowHud = c.Bind("UI", "ShowWingHud", true,
                "Draw the compact wing status readout in flight while you have wingmen assigned.");
            HudCorner = c.Bind("UI", "WingHudCorner", WingCommand.HudCorner.MiddleRight,
                "Where that readout sits, so it can be kept clear of the HUD.");
            UseMfdPanel = c.Bind("UI", "UseMfdPanel", true,
                "Add a WMC screen to the cockpit MFD bezel, alongside BDF/MAP/HUD. This is " +
                "the primary wing interface; the map overlay below is only a fallback.");
            ShowMapPanel = c.Bind("UI", "ShowMapOverlayPanel", false,
                "Draw the standalone wing overlay on the maximised map. Redundant while the " +
                "WMC MFD screen is working, so off by default.");
            HighlightWingOnMap = c.Bind("UI", "HighlightWingOnMap", true,
                "Tint your wingmen's map icons so they stand out from the rest of the friendly force.");
            HighlightWingOnHud = c.Bind("UI", "HighlightWingOnHud", true,
                "Tint your wingmen's in-cockpit HUD markers to match the map. Without this " +
                "the only aircraft the HUD marks distinctly is the game's nearest-ally " +
                "indicator, which is chosen by range and has nothing to do with your wing.");
            HighlightWingTargets = c.Bind("UI", "HighlightWingTargets", true,
                "Mark the units your wing is engaging, on both the map and the HUD, so you " +
                "can see what your wingmen have committed to.");
            WingTargetColor = c.Bind("UI", "WingTargetColor", "#FFB020",
                "Hex colour for units your wing is engaging.");
            WingIconColor = c.Bind("UI", "WingIconColor", "#33E5FF",
                "Hex colour for wingmen, on the map and the HUD. Selected members are drawn brighter.");
            MapCommandEnabled = c.Bind("UI", "MapCommands", true,
                "Enable aircraft tasking and squad groups on the maximised map.");
            VerboseLogging = c.Bind("Debug", "VerboseLogging", false,
                "Log every order and state transition to the BepInEx console.");
        }
    }

    /// <summary>Screen placement for the in-flight wing readout.</summary>
    internal enum HudCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        MiddleRight,
    }

    internal enum FormationShape
    {
        EchelonRight,
        EchelonLeft,
        LineAbreast,
        Trail,
        CombatSpread,
        FingerFour,
        Vic,
        Diamond,
        Ladder,
        Wall,
    }
}
