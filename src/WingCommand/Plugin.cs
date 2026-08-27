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
        public const string PluginVersion = "0.5.0";

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
                $"Aggression={Config2.Aggression.Value} " +
                $"Damping={Config2.Damping.Value} " +
                $"CommandAngle={Config2.CommandAngle.Value} " +
                $"StationBankDegrees={Config2.StationBankDegrees.Value} " +
                $"PursuitBankDegrees={Config2.PursuitBankDegrees.Value} " +
                $"ThrottleGain={Config2.ThrottleGain.Value} " +
                $"CaptureDistance={Config2.CaptureDistance.Value} " +
                $"BankMatchBlend={Config2.BankMatchBlend.Value} " +
                $"RotaryPowerSeconds={Config2.RotaryPowerSeconds.Value} " +
                $"RotaryCommandAngle={Config2.RotaryCommandAngle.Value} " +
                $"RotarySpacingScale={Config2.RotarySpacingScale.Value} " +
                $"ThreatWidenScale={Config2.ThreatSpacingScale.Value}");
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

        // --- Formation: geometry ---
        public readonly ConfigEntry<FormationShape> Shape;
        public readonly ConfigEntry<float> SlotSpacing;
        public readonly ConfigEntry<float> SlotStack;
        public readonly ConfigEntry<float> RecruitRange;
        public readonly ConfigEntry<int> MaxWingSize;
        public readonly ConfigEntry<float> RotarySpacingScale;
        public readonly ConfigEntry<float> ThreatSpacingScale;

        // --- Formation: flying ---
        //
        // Deliberately small. The previous set had thirty-eight entries, several of which
        // encoded one physical quantity as two numbers that only meant anything as a ratio,
        // so tuning one silently moved the other. Everything here names the thing it
        // controls, in the unit the controller acts in.
        public readonly ConfigEntry<float> Aggression;
        public readonly ConfigEntry<float> Damping;
        public readonly ConfigEntry<float> CommandAngle;
        public readonly ConfigEntry<float> StationBankDegrees;
        public readonly ConfigEntry<float> PursuitBankDegrees;
        public readonly ConfigEntry<float> ThrottleGain;
        public readonly ConfigEntry<float> CaptureDistance;
        public readonly ConfigEntry<float> RejoinStagger;
        public readonly ConfigEntry<float> BankMatchBlend;

        // --- Formation: rotary ---
        public readonly ConfigEntry<float> RotaryHoverSpeed;
        public readonly ConfigEntry<float> RotaryPowerSeconds;
        public readonly ConfigEntry<float> RotaryCommandAngle;
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
        public readonly ConfigEntry<float> FallBackStandoff;
        public readonly ConfigEntry<float> OrbitRadius;
        public readonly ConfigEntry<float> MirrorWindowSeconds;
        public readonly ConfigEntry<float> FireInterval;
        public readonly ConfigEntry<bool> AutoReturnOnEmpty;
        public readonly ConfigEntry<float> BingoFuel;

        // --- Station keeping (safety) ---

        // --- Comms ---
        public readonly ConfigEntry<bool> RadioChatter;

        // --- Capability ---
        public readonly ConfigEntry<bool> KeepUpReports;

        // --- Shop ---
        public readonly ConfigEntry<bool> ShopEnabled;
        public readonly ConfigEntry<float> WingPriceGrowth;
        public readonly ConfigEntry<float> FastDeliverySurcharge;
        public readonly ConfigEntry<float> FastDeliveryDistance;
        public readonly ConfigEntry<float> OverLimitAllowance;
        public readonly ConfigEntry<bool> IncludeUndeclaredAircraft;
        public readonly ConfigEntry<float> UndeclaredStock;

        // --- Debug ---
        public readonly ConfigEntry<bool> EnableDebugActions;

        // --- UI ---
        public readonly ConfigEntry<bool> UseNativeRadial;
        public readonly ConfigEntry<bool> ShowHud;
        public readonly ConfigEntry<HudCorner> HudCorner;
        public readonly ConfigEntry<bool> MapCommandEnabled;
        public readonly ConfigEntry<bool> UseMfdPanel;
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
                    new AcceptableValueRange<float>(0f, 200f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RecruitRange = c.Bind("Formation", "RecruitRange", 12000f,
                new ConfigDescription("Maximum range at which a friendly AI aircraft can be recruited into the wing.",
                    new AcceptableValueRange<float>(1000f, 60000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MaxWingSize = c.Bind("Formation", "MaxWingSize", 3,
                new ConfigDescription("Maximum number of wingmen.",
                    new AcceptableValueRange<int>(1, 8),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RotarySpacingScale = c.Bind("Formation", "RotarySpacingScale", 0.55f,
                new ConfigDescription(
                    "Slot spacing multiplier for helicopters. They fly slower and much closer " +
                    "together than jets, so the spacing that looks tight for fighters reads as " +
                    "scattered for them. Every rotary threshold is derived from the result, so " +
                    "changing this moves them all together.",
                    new AcceptableValueRange<float>(0.2f, 1.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ThreatSpacingScale = c.Bind("Formation", "ThreatWidenScale", 1.0f,
                new ConfigDescription(
                    "Spacing multiplier applied while the wing is Aggressive or under missile " +
                    "warning. Real formations widen when they expect to fight. 1 disables it.",
                    new AcceptableValueRange<float>(1f, 4f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // ---- Flying ----
            //
            // Nine knobs, each naming a quantity the controller actually uses. What they
            // replaced is worth recording, because the old set was actively misleading:
            // StationLookAhead (1200 m) and StationMaxCorrection (220 m) existed only to
            // produce a maximum command angle of atan(220/1200) = 10.4 degrees, so tuning
            // either one silently moved a quantity neither of them named. CommandAngle is
            // that quantity, stated directly.

            Aggression = c.Bind("Formation", "Aggression", 1.0f,
                new ConfigDescription(
                    "Master scale on how hard a wingman corrects its position: steering, " +
                    "closure and throttle demand together. Raising it tightens station-keeping " +
                    "at the cost of settling; above about 2 wingmen start to hunt.",
                    new AcceptableValueRange<float>(0.2f, 3f)));
            Damping = c.Bind("Formation", "Damping", 1.0f,
                new ConfigDescription(
                    "Master scale on the rate terms that arrest a correction before it arrives. " +
                    "This is what stops the slow left-right rocking; lower it only if wingmen " +
                    "seem sluggish to start moving, and raise it if they overshoot the slot.",
                    new AcceptableValueRange<float>(0f, 3f)));
            CommandAngle = c.Bind("Formation", "CommandAngle", 25f,
                new ConfigDescription(
                    "Largest heading correction, in degrees, a wingman will command while " +
                    "holding station. This is the real limit on how quickly it can close a " +
                    "lateral error. The old settings worked out to 10.4 degrees, which is why " +
                    "station-keeping felt sluggish.",
                    new AcceptableValueRange<float>(5f, 60f)));
            StationBankDegrees = c.Bind("Formation", "StationBankDegrees", 75f,
                new ConfigDescription(
                    "Bank authority, in degrees, while settled in the slot. The game scales this " +
                    "down again by altitude and speed, so the old value of 45 left only 27-54 " +
                    "degrees of real authority and a wingman simply could not follow a hard turn.",
                    new AcceptableValueRange<float>(20f, 160f)));
            PursuitBankDegrees = c.Bind("Formation", "PursuitBankDegrees", 160f,
                new ConfigDescription(
                    "Bank authority, in degrees, while rejoining from outside the capture " +
                    "distance. Authority eases between this and StationBank with slot error, so " +
                    "there is no step as a wingman arrives.",
                    new AcceptableValueRange<float>(60f, 180f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ThrottleGain = c.Bind("Formation", "ThrottleGain", 0.12f,
                new ConfigDescription(
                    "Throttle change per m/s of speed error. The resting throttle is the " +
                    "airframe's own cruise setting, which is the power needed to hold cruise.",
                    new AcceptableValueRange<float>(0.01f, 0.6f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CaptureDistance = c.Bind("Formation", "CaptureDistance", 500f,
                new ConfigDescription(
                    "Slot error, in metres, below which a wingman is treated as on station. " +
                    "Steering, bank authority and throttle all ease across this distance rather " +
                    "than switching at it.",
                    new AcceptableValueRange<float>(100f, 2000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RejoinStagger = c.Bind("Formation", "RejoinStagger", 1.2f,
                new ConfigDescription(
                    "Seconds per slot by which a Rejoin order is staggered, so the wing arrives " +
                    "in sequence rather than as a converging scrum.",
                    new AcceptableValueRange<float>(0f, 6f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BankMatchBlend = c.Bind("Formation", "BankMatchBlend", 0.35f,
                new ConfigDescription(
                    "How much a settled wingman rolls to match your bank, 0 to 1. This blends " +
                    "with the autopilot's own roll rather than overriding it, and disengages " +
                    "past a hard bank limit and near the ground. Set to 0 to switch it off.",
                    new AcceptableValueRange<float>(0f, 1f)));

            // ---- Rotary ----
            //
            // Helicopters get their own model, not a variation on the fixed-wing one, because
            // AutopilotHelo answers to completely different commands. Note that its forward
            // waypoint is recomputed only once per second and rate-limited to 0.8 rad, so
            // there is a ceiling on rotary responsiveness that no setting here can raise.

            RotaryHoverSpeed = c.Bind("Formation", "RotaryHoverSpeed", 25f,
                new ConfigDescription(
                    "Leader speed, in m/s, below which helicopters hold their slot as a point " +
                    "in space instead of flying a heading.",
                    new AcceptableValueRange<float>(0f, 60f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RotaryPowerSeconds = c.Bind("Formation", "RotaryPowerSeconds", 20f,
                new ConfigDescription(
                    "Seconds of travel used as the helicopter's destination distance. This is a " +
                    "power setting, not a steering one: AutopilotHelo derives collective from " +
                    "0.5 + distance*0.001 - speed*0.02, so distance IS the throttle command and " +
                    "20 is what makes those terms cancel at hover power. Steering is set " +
                    "separately by RotaryCommandAngle, so this no longer limits it.",
                    new AcceptableValueRange<float>(5f, 40f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RotaryCommandAngle = c.Bind("Formation", "RotaryCommandAngle", 30f,
                new ConfigDescription(
                    "Largest heading correction, in degrees, a helicopter will command to close " +
                    "a lateral error. Previously this was whatever fell out of the destination " +
                    "distance, which worked out at about 5 degrees.",
                    new AcceptableValueRange<float>(5f, 60f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
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
            FallBackStandoff = c.Bind("Engagement", "FallBackStandoff", 6000f,
                new ConfigDescription(
                    "How far from the threat a Fall Back order runs before the wing settles " +
                    "into its holding orbit, in metres.",
                    new AcceptableValueRange<float>(1000f, 30000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            OrbitRadius = c.Bind("Engagement", "OrbitRadius", 2000f,
                new ConfigDescription(
                    "Radius of the circle wingmen fly when holding over a point, for both " +
                    "Orbit Here and the end of a Fall Back.",
                    new AcceptableValueRange<float>(300f, 10000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
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

            // Four keys collapsed to one. The distance and the timeout never wanted tuning
            // independently of each other, and WarnOnSlowRecruit is the same question asked
            // at recruit time instead of in flight.
            KeepUpReports = c.Bind("Formation", "KeepUpReports", true,
                new ConfigDescription(
                    "Warn when a wingman cannot hold formation, and send it home rather than " +
                    "let it chase until it crashes. Mainly affects slow aircraft recruited " +
                    "into a fast flight.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShopEnabled = c.Bind("Shop", "ShopEnabled", true,
                "Allow buying wingmen. Aircraft are priced from the same value the player's " +
                "own aircraft menu uses, paid for out of your allocation, and drawn from " +
                "your faction's stock - so a purchase competes with the mission's own AI.");
            WingPriceGrowth = c.Bind("Shop", "WingPriceGrowth", 1.5f,
                new ConfigDescription(
                    "Price multiplier per wingman already in the formation, compounding. At " +
                    "1.5 a 1000-credit airframe costs 1000, 1500, 2250, 3375 as the wing " +
                    "fills. Set to 1 for flat pricing.",
                    new AcceptableValueRange<float>(1f, 3f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            FastDeliverySurcharge = c.Bind("Shop", "FastDeliverySurcharge", 0.25f,
                new ConfigDescription(
                    "Extra fraction of the price for delivery straight to your wing rather " +
                    "than to the nearest airbase.",
                    new AcceptableValueRange<float>(0f, 2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            FastDeliveryDistance = c.Bind("Shop", "FastDeliveryDistance", 2000f,
                new ConfigDescription(
                    "How far behind you a fast delivery appears, in metres.",
                    new AcceptableValueRange<float>(500f, 10000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            OverLimitAllowance = c.Bind("Shop", "OverLimitAllowance", 1f,
                new ConfigDescription(
                    "How many aircraft your purchases may push the faction above the " +
                    "mission's own AI aircraft limit. Raising this is the main way to " +
                    "unbalance a mission with this feature.",
                    new AcceptableValueRange<float>(0f, 8f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            IncludeUndeclaredAircraft = c.Bind("Shop", "IncludeUndeclaredAircraft", true,
                "Also offer airframes the mission did not stock, from the game's own aircraft " +
                "registry. This is what makes modded and workshop aircraft purchasable: they " +
                "are never in a mission's declared supply, so without this they can never " +
                "appear. They draw on their own small allowance rather than faction stock.");
            UndeclaredStock = c.Bind("Shop", "UndeclaredStock", 3f,
                new ConfigDescription(
                    "How many of each undeclared airframe may be bought per mission.",
                    new AcceptableValueRange<float>(1f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

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
