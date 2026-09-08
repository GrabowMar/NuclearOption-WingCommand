using BepInEx.Configuration;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Squadron radio detail preference.</summary>
    internal enum ChatterLevel
    {
        Off,
        Text,
        TextAndTone,
    }

    /// <summary>Display scope for wing outlines and HUD tints.</summary>
    internal enum HighlightMode
    {
        Off,
        Wing,
        WingAndTargets,
    }

    /// <summary>Player preferences and feature permissions. Internal tuning belongs in WingTuning; Mode
    /// selects the WingFidelity behaviour budget.</summary>
    internal class WingConfig
    {
        // Formation settings.
        public ConfigEntry<FormationShape> FormationShape { get; private set; }
        public ConfigEntry<float> FormationSpacing { get; private set; }

        // Key bindings.
        public ConfigEntry<KeyCode> RadialKey { get; private set; }
        public ConfigEntry<KeyCode> QuickRejoinKey { get; private set; }
        public ConfigEntry<KeyCode> QuickEngageKey { get; private set; }
        public ConfigEntry<KeyCode> QuickDisengageKey { get; private set; }
        public ConfigEntry<KeyCode> QuickAttackKey { get; private set; }
        public ConfigEntry<KeyCode> CycleRoeKey { get; private set; }

        // AI settings.
        public ConfigEntry<WingMode> Mode { get; private set; }
        public ConfigEntry<bool> AiSharpTurns { get; private set; }
        public ConfigEntry<bool> AiTargetSpreading { get; private set; }
        public ConfigEntry<bool> AiMissileWarningRepair { get; private set; }

        // Engagement settings.
        public ConfigEntry<WingRoe> DefaultRoe { get; private set; }
        public ConfigEntry<bool> AutoReturnOnEmpty { get; private set; }
        public ConfigEntry<bool> RtbReturnsToReserve { get; private set; }
        public ConfigEntry<bool> TakeoverOnDeath { get; private set; }
        public ConfigEntry<float> LeashDistance { get; private set; }
        public ConfigEntry<int> MaxWingmenPerTarget { get; private set; }
        public ConfigEntry<float> BingoFuelThreshold { get; private set; }

        // Radio settings.
        public ConfigEntry<ChatterLevel> Radio { get; private set; }

        // Pilot settings.
        public ConfigEntry<bool> PilotProgression { get; private set; }
        public ConfigEntry<float> RankEffect { get; private set; }

        // Shop settings.
        public ConfigEntry<bool> ShopEnabled { get; private set; }
        public ConfigEntry<float> RecruitmentCostPercent { get; private set; }

        // Loadout persistence.
        public ConfigEntry<string> LoadoutTemplates { get; private set; }

        // Display settings.
        public ConfigEntry<bool> ShowHud { get; private set; }
        public ConfigEntry<int> WingHudX { get; private set; }
        public ConfigEntry<int> WingHudY { get; private set; }
        public ConfigEntry<bool> UseMfdPanel { get; private set; }
        public ConfigEntry<bool> MapCommandEnabled { get; private set; }
        public ConfigEntry<HighlightMode> Highlight { get; private set; }
        public ConfigEntry<string> WingIconColor { get; private set; }
        public ConfigEntry<string> WingTargetColor { get; private set; }
        public ConfigEntry<bool> TacticalPauseInSingleplayer { get; private set; }
        public ConfigEntry<float> TacticalPauseScale { get; private set; }
        public ConfigEntry<bool> ExternalHitmarkerAudio { get; private set; }

        // Debug controls.
        /// <summary>Display-only carrier for the Debug warning banner; its value is unused.</summary>
        public ConfigEntry<bool> DebugWarning { get; private set; }
        public ConfigEntry<bool> EnableDebugActions { get; private set; }

        /// <summary>Display-only carrier for the spawn button; CustomDrawer handles the action.</summary>
        public ConfigEntry<bool> SpawnDebugWing { get; private set; }
        public ConfigEntry<string> DebugSpawnAircraft { get; private set; }
        public ConfigEntry<bool> FreePlanePurchases { get; private set; }
        public ConfigEntry<bool> DisableWingSizeLimit { get; private set; }
        public ConfigEntry<bool> BypassRankRequirement { get; private set; }
        public ConfigEntry<bool> VerboseLogging { get; private set; }

        // Read cheats through these accessors so EnableDebugActions gates every debug feature
        // consistently.
        public bool CheatFreePurchases => EnableDebugActions.Value && FreePlanePurchases.Value;
        public bool CheatNoWingLimit => EnableDebugActions.Value && DisableWingSizeLimit.Value;
        public bool CheatBypassRank => EnableDebugActions.Value && BypassRankRequirement.Value;

        public float BingoFuel => BingoFuelThreshold != null ? BingoFuelThreshold.Value : WingTuning.BingoFuel;
        public float RecruitmentCostRate => RecruitmentCostPercent != null ? RecruitmentCostPercent.Value : WingTuning.RecruitmentCostRate;

        private const string HexHelp = "Six-digit hex, with or without the leading #.";

        /// <summary>Validate colours when binding so BepInEx logs malformed values and restores the
        /// default.</summary>
        private sealed class HexColourValue : AcceptableValueBase
        {
            public HexColourValue() : base(typeof(string)) { }

            private static bool IsSixDigitHex(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                string digits = value[0] == '#' ? value.Substring(1) : value;
                if (digits.Length != 6) return false;

                foreach (char c in digits)
                {
                    bool hex = (c >= '0' && c <= '9')
                               || (c >= 'a' && c <= 'f')
                               || (c >= 'A' && c <= 'F');
                    if (!hex) return false;
                }
                return true;
            }

            public override object Clamp(object value) => IsValid(value) ? value : "#FFFFFF";

            public override bool IsValid(object value) => IsSixDigitHex(value as string);

            public override string ToDescriptionString() => "# Expects " + HexHelp;
        }

        private static readonly HexColourValue HexColour = new HexColourValue();

        private static ConfigDescription Advanced(string text, AcceptableValueBase values = null) =>
            new ConfigDescription(text, values,
                new ConfigurationManagerAttributes { IsAdvanced = true });

        public WingConfig(ConfigFile c)
        {
            // Preserve bind order because BepInEx uses it in saved configs. Removed keys remain
            // orphaned until its next save.
            BindMode(c);
            BindFormation(c);
            BindEngagement(c);
            BindComms(c);
            BindPilots(c);
            BindShop(c);
            BindLoadout(c);
            BindKeys(c);
            BindUi(c);
            BindDebug(c);
        }

        private void BindFormation(ConfigFile c)
        {
            FormationShape = c.Bind("Formation", "Shape", WingCommand.FormationShape.EchelonRight,
                "The formation geometry the wing assumes at the start of a mission.");
            FormationSpacing = c.Bind("Formation", "Spacing", 120f,
                new ConfigDescription(
                    "Standard lateral and longitudinal distance between formation slots, in metres.",
                    new AcceptableValueRange<float>(50f, 300f)));
        }

        private void BindMode(ConfigFile c)
        {
            AiSharpTurns = c.Bind("AI", "AiSharpTurns", true,
                "Enable stronger turns for airborne AI with sufficient speed and terrain clearance. " +
                "Applies on the next steering update, including non-wing AI.");
            AiTargetSpreading = c.Bind("AI", "AiTargetSpreading", true,
                "Spread locally simulated AI across comparable targets. Applies on the next target " +
                "selection, including non-wing AI. Performance mode still disables this feature.");
            AiMissileWarningRepair = c.Bind("AI", "AiMissileWarningRepair", true,
                "Repair AI missile-warning subscriptions when entering combat. Applies on the next " +
                "combat entry, including non-wing AI; disabling does not undo existing subscriptions.");
            // Put the shared behaviour-budget switch at the top of settings.
            Mode = c.Bind("AI", "Mode", WingMode.Smart,
                new ConfigDescription(
                    "Smart is the full behaviour and the default. Performance is a lean " +
                    "profile for busy missions and multiplayer hosts, where the host " +
                    "simulates every AI wingman: coarser formation updates, no manoeuvre or " +
                    "jam orders, minimal radio, and the expensive target-coordination and " +
                    "opportunity-scanning passes turned off. Applies at the start of a mission.",
                    null,
                    new ConfigurationManagerAttributes { Order = 100 }));
        }

        private void BindEngagement(ConfigFile c)
        {
            DefaultRoe = c.Bind("Engagement", "DefaultRoe", WingRoe.Hold,
                "Rules of engagement the wing starts a mission with. Hold limits fire to " +
                "missile defence only; Tight prioritises threats around the formation; " +
                "Free may shoot opportunity targets without changing orders.");

            // Migrate Escort to Tight from the raw config; the renamed enum otherwise fails parsing and
            // becomes Hold.
            if (DefaultRoe.Value == WingRoe.Hold && FileMentionsLegacyRoe(c, "Escort"))
                DefaultRoe.Value = WingRoe.Tight;
            AutoReturnOnEmpty = c.Bind("Engagement", "AutoReturnOnEmpty", true,
                "Wingmen return to base on their own once out of ammunition or down to " +
                "bingo fuel, instead of holding station empty.");
            RtbReturnsToReserve = c.Bind("Engagement", "RtbReturnsToReserve", true,
                "A wingman that completes a Return To Base order leaves the cockpit, " +
                "returns its airframe to faction stock, refunds the allocation spent on it, " +
                "and puts its pilot back in the squadron pool. Turn this off to park on the " +
                "apron instead of despawning. Host or single-player only.");
            TakeoverOnDeath = c.Bind("Engagement", "TakeoverOnDeath", true,
                "When your pilot dies or ejects, offer control of a surviving aircraft in " +
                "your wing. Host or single-player only; mission failures unrelated to the " +
                "player's aircraft are never suppressed.");
            LeashDistance = c.Bind("Engagement", "LeashDistance", 5000f,
                new ConfigDescription(
                    "Maximum distance (in metres) wingmen may stray from the leader to pursue targets before breaking off and rejoining.",
                    new AcceptableValueRange<float>(2000f, 15000f)));
            MaxWingmenPerTarget = c.Bind("Engagement", "MaxWingmenPerTarget", 2,
                new ConfigDescription(
                    "Maximum number of wingmen that can simultaneously engage the same target.",
                    new AcceptableValueRange<int>(1, 4)));
            BingoFuelThreshold = c.Bind("Engagement", "BingoFuel", 0.15f,
                new ConfigDescription(
                    "Fuel fraction at which wingmen call bingo and automatically return to base when AutoReturnOnEmpty is active.",
                    new AcceptableValueRange<float>(0.05f, 0.40f)));
        }

        /// <summary>Check the raw file for legacy ROE values; BepInEx exposes no failed-bind
        /// record.</summary>
        private static bool FileMentionsLegacyRoe(ConfigFile c, string legacyValue)
        {
            try
            {
                string path = c.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;

                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string t = line.Trim();
                    if (t.StartsWith("DefaultRoe", System.StringComparison.Ordinal) &&
                        t.IndexOf(legacyValue, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (System.IO.IOException) { }
            catch (System.UnauthorizedAccessException) { }

            return false;
        }

        private void BindComms(ConfigFile c)
        {
            // Use a new enum key because legacy booleans cannot parse as ChatterLevel; existing
            // installations receive the new default.
            Radio = c.Bind("Comms", "Radio", ChatterLevel.TextAndTone,
                "Squadron radio. Text shows named, pilot-specific transmissions for orders, " +
                "engagements, defensive calls, Winchester and rejoins; TextAndTone opens " +
                "each one with the game's own radio click, the same sound mission and HQ " +
                "messages use. Performance mode cuts the traffic to essentials either way.");
        }

        private void BindPilots(ConfigFile c)
        {
            PilotProgression = c.Bind("Pilot", "PilotProgression", true,
                "Wing pilots keep a callsign, a record and a rank that rises with kills, " +
                "completed sorties and engagements survived. Rank has a small effect on how " +
                "well they shoot: a Legend gets roughly 12% more weapon reach and cycles " +
                "shots about 12% faster than a rookie.");
            RankEffect = c.Bind("Pilot", "RankEffect", 1.0f,
                new ConfigDescription(
                    "Multiplier on pilot rank benefits (weapon reach, reaction cadence and formation control). Set to 0 for cosmetic progression only.",
                    new AcceptableValueRange<float>(0f, 2.0f)));
        }

        private void BindShop(ConfigFile c)
        {
            ShopEnabled = c.Bind("Shop", "ShopEnabled", true,
                "Allow buying wingmen. Aircraft are priced from the same value the player's " +
                "own aircraft menu uses, paid for out of your allocation, and drawn from " +
                "your faction's stock - so a purchase competes with the mission's own AI.");
            RecruitmentCostPercent = c.Bind("Shop", "RecruitmentCostPercent", 0.25f,
                new ConfigDescription(
                    "Fraction of an airframe's list price charged when recruiting active friendly mission AI into the wing (0.0 = free).",
                    new AcceptableValueRange<float>(0f, 1.0f)));
        }

        private void BindLoadout(ConfigFile c)
        {
            // LOADOUT writes templates, but keep the setting visible for manual reset. Parsing drops
            // only malformed records.
            LoadoutTemplates = c.Bind("Loadout", "SavedTemplates", "",
                Advanced("Saved per-pylon loadout templates, written by the WMC LOADOUT tab. " +
                         "One record per template as airframe|id|name|store keys, records " +
                         "separated by semicolons. Clear this to delete every saved template. " +
                         "STANDARD FIT is the live player default for this mission and is not stored here."));
        }

        private void BindKeys(ConfigFile c)
        {
            // Optional extra wheel key; native radial availability is independent.
            RadialKey = c.Bind("Keys", "WingMenu", KeyCode.C,
                "Hold to open Wing Command's wheel, aim at an order, then release to confirm. " +
                "Right-click cancels. Set None to disable the shortcut. " +
                "The Wing Command slice remains available in the game's radial menu.");
            QuickRejoinKey = c.Bind("Keys", "QuickRejoin", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to rejoin formation."));
            QuickEngageKey = c.Bind("Keys", "QuickEngage", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to engage."));
            QuickDisengageKey = c.Bind("Keys", "QuickDisengage", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to fall back / disengage."));
            QuickAttackKey = c.Bind("Keys", "QuickAttackTarget", KeyCode.None,
                Advanced("Optional hotkey: order the wing to attack the player's currently targeted unit."));
            CycleRoeKey = c.Bind("Keys", "CycleRoe", KeyCode.None,
                Advanced("Optional hotkey: cycle wing Rules of Engagement (Hold -> Tight -> Free)."));
        }

        private void BindUi(ConfigFile c)
        {
            ShowHud = c.Bind("UI", "ShowWingHud", true,
                "Draw the compact wing status readout beside the tactical map while you have " +
                "wingmen assigned.");
            WingHudX = c.Bind("UI", "WingHudX", 0,
                new ConfigDescription(
                    "Horizontal offset from the minimap in HUD units (scales with the game's UI). " +
                    "Positive moves right; negative moves left. Applies immediately. Reset X and Y to 0 to dock beside the map.",
                    new AcceptableValueRange<int>(-4000, 4000),
                    new ConfigurationManagerAttributes { DispName = "Wing HUD X offset", Order = 2 }));
            WingHudY = c.Bind("UI", "WingHudY", 0,
                new ConfigDescription(
                    "Vertical offset from the minimap in HUD units (scales with the game's UI). " +
                    "Positive moves up; negative moves down. Applies immediately. Reset X and Y to 0 to dock beside the map.",
                    new AcceptableValueRange<int>(-4000, 4000),
                    new ConfigurationManagerAttributes { DispName = "Wing HUD Y offset", Order = 1 }));
            UseMfdPanel = c.Bind("UI", "UseMfdPanel", true,
                "Add a WMC screen to the cockpit MFD bezel, alongside BDF/MAP/HUD.");
            MapCommandEnabled = c.Bind("UI", "MapCommands", true,
                "Enable tactical wing selection and point tasking on the maximised map.");

            // Share identity highlighting between map outlines and HUD tints.
            Highlight = c.Bind("UI", "Highlight", HighlightMode.WingAndTargets,
                "Wing outlines your wingmen's map icons and tints their in-cockpit HUD " +
                "markers; WingAndTargets also " +
                "marks the units they are engaging.");
            WingIconColor = c.Bind("UI", "WingMemberColor", "#39FF65",
                Advanced("Hex colour for wingmen's roster markers, map outlines and HUD. " +
                         HexHelp, HexColour));
            WingTargetColor = c.Bind("UI", "WingTargetColor", "#FFB020",
                Advanced("Hex colour for units your wing is engaging. " + HexHelp, HexColour));
            TacticalPauseInSingleplayer = c.Bind("UI", "TacticalPauseInSingleplayer", false,
                "Slow down game time while the tactical command screen is active in singleplayer for tactical planning.");
            TacticalPauseScale = c.Bind("UI", "TacticalPauseScale", 0.25f,
                new ConfigDescription(
                    "Game speed time-scale while tactical pause in singleplayer is active (0.0 = full pause, 0.25 = slow-mo).",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            ExternalHitmarkerAudio = c.Bind("UI", "ExternalHitmarkerAudio", true,
                "Play hitmarker audio confirmation when landing hits in 3rd-person external/orbit camera views.");
        }

        private void BindDebug(ConfigFile c)
        {
            // Use a display-only entry as the category banner; ConfigurationManager has no header API.
            // Its stored value is unused.
            DebugWarning = c.Bind("Debug", "DebugWarningBanner", false,
                new ConfigDescription(
                    "Display only. The Debug settings are cheats: unbalanced, barely tested, " +
                    "and liable to break mission or mod progression.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        Order = 50,
                        CustomDrawer = WingDebugActions.DrawWarning,
                        HideSettingName = true,
                        HideDefaultButton = true,
                    }));

            // Expose the master switch beside the debug actions it gates.
            EnableDebugActions = c.Bind("Debug", "EnableDebugActions", false,
                new ConfigDescription(
                    "Allow the development-only actions below. They are cheats and host-only.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Enable debug actions",
                        Order = 40,
                    }));
            DebugSpawnAircraft = c.Bind("Debug", "DebugSpawnAircraft", "",
                new ConfigDescription(
                    "DEBUG CHEAT: Override the aircraft used by the debug spawn button. " +
                    "Choose a catalogue aircraft regardless of faction stock or rank. " +
                    "Empty uses your current aircraft. Requires EnableDebugActions; host-only.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Debug spawn aircraft",
                        Order = 35,
                        CustomDrawer = WingDebugActions.DrawAircraftSelector,
                    }));
            SpawnDebugWing = c.Bind("Debug", "SpawnDebugWing", false,
                new ConfigDescription(
                    "DEBUG CHEAT: Spawn a full wing of the selected debug aircraft, already in " +
                    "formation slots, and assign them. Requires the switch above.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Spawn debug wing",
                        Order = 30,
                        CustomDrawer = WingDebugActions.DrawSpawnButton,

                        // The stored value is unused for this action button, so hide reset.
                        HideDefaultButton = true,
                    }));
            FreePlanePurchases = c.Bind("Debug", "FreePlanePurchases", false,
                new ConfigDescription(
                    "DEBUG CHEAT: Requisitioned aircraft cost no allocation. Stock, rank and " +
                    "squadron-cap rules still apply. This is unbalanced, insufficiently tested, " +
                    "and may break mission or mod progression.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Free plane purchases",
                        Order = 20,
                    }));
            DisableWingSizeLimit = c.Bind("Debug", "DisableWingSizeLimit", false,
                new ConfigDescription(
                    "DEBUG CHEAT: Ignore the wing size limit when assigning or requisitioning aircraft. " +
                    "Formation geometry, HUD layout, performance and mission scripting are not " +
                    "supported for an unlimited wing and may break.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Disable wing size limit",
                        Order = 10,
                    }));
            BypassRankRequirement = c.Bind("Debug", "BypassRankRequirement", false,
                new ConfigDescription(
                    "DEBUG CHEAT: Ignore the player-rank requirement when requisitioning " +
                    "aircraft, and the rank gate on exceeding the squadron limit. Mission " +
                    "and mod progression are built around these gates and are not tested " +
                    "without them.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Bypass rank requirement",
                        Order = 15,
                    }));
            VerboseLogging = c.Bind("Debug", "VerboseLogging", false,
                new ConfigDescription(
                    "Log command requests, results, state transitions and flight diagnostics to the " +
                    "BepInEx console and LogOutput.log. Applies immediately; does not require debug cheats. " +
                    "Disable after reproducing an issue to reduce log volume.",
                    null,
                    new ConfigurationManagerAttributes { DispName = "Debug action logging", IsAdvanced = false, Order = 60 }));
        }
    }
}
