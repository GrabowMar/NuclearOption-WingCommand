using BepInEx.Configuration;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// All user-tunable settings. Bound through BepInEx so they show up in
    /// ConfigurationManager (F1) without any extra UI work.
    /// </summary>
    internal class WingConfig
    {
        // --- Keys ---
        public ConfigEntry<KeyCode> RadialKey { get; private set; }
        public ConfigEntry<KeyCode> QuickRejoinKey { get; private set; }
        public ConfigEntry<KeyCode> QuickEngageKey { get; private set; }

        // --- Formation: geometry ---
        public ConfigEntry<FormationShape> Shape { get; private set; }
        public ConfigEntry<float> SlotSpacing { get; private set; }
        public ConfigEntry<float> SlotStack { get; private set; }
        public ConfigEntry<float> RecruitRange { get; private set; }
        public ConfigEntry<int> MaxWingSize { get; private set; }
        public ConfigEntry<float> RotarySpacingScale { get; private set; }

        /// <summary>
        /// The one switch for how clever - and how expensive - the wing is. Smart is the
        /// full behaviour and the default; Performance is the lean profile for busy
        /// missions and multiplayer. Snapshotted at mission start into <see cref="WingBrain"/>.
        /// </summary>
        public ConfigEntry<WingMode> Mode { get; private set; }

        // --- Formation: flying ---
        //
        // Deliberately small. The previous set had thirty-eight entries, several of which
        // encoded one physical quantity as two numbers that only meant anything as a ratio,
        // so tuning one silently moved the other. Everything here names the thing it
        // controls, in the unit the controller acts in.
        public ConfigEntry<float> CommandAngle { get; private set; }
        public ConfigEntry<float> StationBankDegrees { get; private set; }
        public ConfigEntry<float> PursuitBankDegrees { get; private set; }
        public ConfigEntry<float> ThrottleGain { get; private set; }
        public ConfigEntry<float> CaptureDistance { get; private set; }
        public ConfigEntry<float> RejoinStagger { get; private set; }
        public ConfigEntry<float> BankMatchBlend { get; private set; }

        // --- Formation: rotary ---
        public ConfigEntry<float> RotaryHoverSpeed { get; private set; }
        public ConfigEntry<float> RotaryPowerSeconds { get; private set; }

        // --- Manoeuvres ---
        public ConfigEntry<bool> AerobaticsEnabled { get; private set; }
        public ConfigEntry<float> ManeuverAltitudeFloor { get; private set; }
        public ConfigEntry<float> ManeuverHardFloor { get; private set; }
        public ConfigEntry<float> ManeuverMinSpeedFraction { get; private set; }

        // --- AI ---
        public ConfigEntry<bool> AiTweakEnabled { get; private set; }
        public ConfigEntry<float> AiSkillScale { get; private set; }
        public ConfigEntry<float> AiBraveryScale { get; private set; }
        public ConfigEntry<bool> MutualSupport { get; private set; }
        public ConfigEntry<float> PlayerTargetPenalty { get; private set; }
        public ConfigEntry<bool> PanicSystem { get; private set; }
        public ConfigEntry<float> PanicClearSeconds { get; private set; }

        // --- Engagement ---
        public ConfigEntry<WingRoe> DefaultRoe { get; private set; }
        public ConfigEntry<bool> MissileDefence { get; private set; }
        public ConfigEntry<bool> JammingEnabled { get; private set; }
        public ConfigEntry<float> HoldEngageRange { get; private set; }
        public ConfigEntry<float> FreeEngageRange { get; private set; }
        public ConfigEntry<float> LeashRadius { get; private set; }
        public ConfigEntry<float> FallBackStandoff { get; private set; }
        public ConfigEntry<float> OrbitRadius { get; private set; }
        public ConfigEntry<float> MirrorWindowSeconds { get; private set; }
        public ConfigEntry<float> FireInterval { get; private set; }
        public ConfigEntry<int> MaxWingmenPerTarget { get; private set; }
        public ConfigEntry<bool> AutoReturnOnEmpty { get; private set; }
        public ConfigEntry<float> BingoFuel { get; private set; }
        public ConfigEntry<bool> RtbReturnsToReserve { get; private set; }
        public ConfigEntry<bool> TakeoverOnDeath { get; private set; }

        // --- Station keeping (safety) ---

        // --- Comms ---
        public ConfigEntry<bool> RadioChatter { get; private set; }
        public ConfigEntry<bool> RadioChatterSound { get; private set; }

        public ConfigEntry<bool> PilotProgression { get; private set; }
        public ConfigEntry<int> XpPerKill { get; private set; }
        public ConfigEntry<int> XpPerSortie { get; private set; }
        public ConfigEntry<int> XpPerEngagement { get; private set; }
        public ConfigEntry<int> XpPerRank { get; private set; }
        public ConfigEntry<float> RankEffect { get; private set; }

        // --- Capability ---
        public ConfigEntry<bool> KeepUpReports { get; private set; }

        // --- Loadout ---
        public ConfigEntry<string> LoadoutTemplates { get; private set; }

        // --- Shop ---
        public ConfigEntry<bool> ShopEnabled { get; private set; }
        public ConfigEntry<float> RecruitmentCostRate { get; private set; }
        public ConfigEntry<int> AdditionalWingReserve { get; private set; }
        public ConfigEntry<float> ExceedLimitCostMultiplier { get; private set; }
        public ConfigEntry<int> ExceedLimitRank { get; private set; }
        public ConfigEntry<int> ExceedLimitAllowance { get; private set; }
        public ConfigEntry<float> WingPriceGrowth { get; private set; }
        public ConfigEntry<float> FastDeliverySurcharge { get; private set; }
        public ConfigEntry<float> FastDeliveryDistance { get; private set; }
        public ConfigEntry<bool> IncludeUndeclaredAircraft { get; private set; }
        public ConfigEntry<float> UndeclaredStock { get; private set; }

        // --- Debug ---
        /// <summary>Carrier for the Debug category's one-line warning banner. Never read.</summary>
        public ConfigEntry<bool> DebugWarning { get; private set; }
        public ConfigEntry<bool> EnableDebugActions { get; private set; }

        /// <summary>
        /// Carrier for the spawn action's button row in the settings window. Never read —
        /// see its <c>CustomDrawer</c>.
        /// </summary>
        public ConfigEntry<bool> SpawnDebugWing { get; private set; }
        public ConfigEntry<bool> FreePlanePurchases { get; private set; }
        public ConfigEntry<bool> DisableWingSizeLimit { get; private set; }

        // --- UI ---
        public ConfigEntry<bool> UseNativeRadial { get; private set; }
        public ConfigEntry<bool> ShowHud { get; private set; }
        public ConfigEntry<bool> MapCommandEnabled { get; private set; }
        public ConfigEntry<bool> UseMfdPanel { get; private set; }
        public ConfigEntry<bool> HighlightWingOnMap { get; private set; }
        public ConfigEntry<bool> HighlightWingOnHud { get; private set; }
        public ConfigEntry<bool> HighlightWingTargets { get; private set; }
        public ConfigEntry<string> WingIconColor { get; private set; }
        public ConfigEntry<string> WingTargetColor { get; private set; }
        public ConfigEntry<bool> VerboseLogging { get; private set; }

        private static ConfigDescription Advanced(string text, AcceptableValueBase values = null) =>
            new ConfigDescription(text, values,
                new ConfigurationManagerAttributes { IsAdvanced = true });

        private static ConfigDescription Hidden(string text, AcceptableValueBase values = null) =>
            new ConfigDescription(text, values,
                new ConfigurationManagerAttributes { IsAdvanced = true, Browsable = false });

        public WingConfig(ConfigFile c)
        {
            // Bound in this order and no other: BepInEx writes the .cfg in bind
            // order, so reordering these silently reshuffles every existing
            // configuration file under the player.
            BindKeys(c);
            BindFormation(c);
            BindMode(c);
            BindFlying(c);
            BindRotary(c);
            BindAi(c);
            BindEngagement(c);
            BindManoeuvres(c);
            BindLoadout(c);
            BindShop(c);
            BindPilots(c);
            BindComms(c);
            BindRadial(c);
            BindDebug(c);
            BindUi(c);
        }

        private void BindKeys(ConfigFile c)
        {
            RadialKey = c.Bind("Keys", "FallbackRadialMenu", KeyCode.None,
                Advanced("Hold to open the standalone fallback radial. Only needed if the native " +
                         "wheel integration is turned off or unavailable; leave unbound otherwise."));
            QuickRejoinKey = c.Bind("Keys", "QuickRejoin", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to rejoin formation."));
            QuickEngageKey = c.Bind("Keys", "QuickEngage", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to engage."));
        }

        private void BindFormation(ConfigFile c)
        {
            Shape = c.Bind("Formation", "Shape", FormationShape.EchelonRight,
                "Formation geometry used when wingmen hold station.");
            SlotSpacing = c.Bind("Formation", "SlotSpacing", 120f,
                Advanced("Lateral/longitudinal spacing between slots, in metres.",
                    new AcceptableValueRange<float>(40f, 600f)));
            SlotStack = c.Bind("Formation", "SlotStack", 20f,
                new ConfigDescription("Vertical stagger per slot, in metres. Keeps wingmen out of each other's wash.",
                    new AcceptableValueRange<float>(0f, 200f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RecruitRange = c.Bind("Formation", "RecruitRange", 12000f,
                Hidden("Retired compatibility key. Assignment is by map selection, which has " +
                       "no range limit of its own.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            MaxWingSize = c.Bind("Formation", "MaxWingSize", 3,
                new ConfigDescription("Maximum number of wingmen.",
                    new AcceptableValueRange<int>(1, 8)));
            RotarySpacingScale = c.Bind("Formation", "RotarySpacingScale", 0.55f,
                new ConfigDescription(
                    "Slot spacing multiplier for helicopters. They fly slower and much closer " +
                    "together than jets, so the spacing that looks tight for fighters reads as " +
                    "scattered for them. Every rotary threshold is derived from the result, so " +
                    "changing this moves them all together.",
                    new AcceptableValueRange<float>(0.2f, 1.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private void BindMode(ConfigFile c)
        {
            // The one switch most players ever touch. Smart is the full behaviour and the
            // development target; Performance is the lean profile for busy missions and
            // multiplayer, where the host simulates every AI wingman.
            Mode = c.Bind("AI", "Mode", WingMode.Smart,
                new ConfigDescription(
                    "Smart is the full behaviour and the default. Performance is a lean " +
                    "profile for busy missions and multiplayer hosts: coarser formation " +
                    "updates, no manoeuvre or jam orders, minimal radio, and the expensive " +
                    "target-coordination and opportunity-scanning passes turned off. " +
                    "Applies at the start of a mission.",
                    null,
                    new ConfigurationManagerAttributes { Order = 100 }));
        }

        private void BindFlying(ConfigFile c)
        {
            // ---- Flying ----
            //
            // Nine knobs, each naming a quantity the controller actually uses. What they
            // replaced is worth recording, because the old set was actively misleading:
            // StationLookAhead (1200 m) and StationMaxCorrection (220 m) existed only to
            // produce a maximum command angle of atan(220/1200) = 10.4 degrees, so tuning
            // either one silently moved a quantity neither of them named. CommandAngle is
            // that quantity, stated directly.

            CommandAngle = c.Bind("Formation", "CommandAngle", 25f,
                Advanced(
                    "Largest heading correction, in degrees, a wingman will command while " +
                    "holding station. This is the real limit on how quickly it can close a " +
                    "lateral error. The old settings worked out to 10.4 degrees, which is why " +
                    "station-keeping felt sluggish.",
                    new AcceptableValueRange<float>(5f, 60f)));
            StationBankDegrees = c.Bind("Formation", "StationBankDegrees", 75f,
                Advanced(
                    "Bank authority, in degrees, while settled in the slot. The game scales this " +
                    "down again by altitude and speed, so the old value of 45 left only 27-54 " +
                    "degrees of real authority and a wingman simply could not follow a hard turn.",
                    new AcceptableValueRange<float>(20f, 160f)));
            // New key deliberately retires the old 160-degree default. The live formation
            // log recorded requests as high as 277 degrees after leader-bank feed-forward;
            // that is an inversion request, not useful pursuit authority.
            PursuitBankDegrees = c.Bind("Formation", "SafePursuitBankDegrees", 88f,
                new ConfigDescription(
                    "Bank authority, in degrees, while rejoining from outside the capture " +
                    "distance. Formation capture is capped below inversion even if an older " +
                    "configuration contained a higher value.",
                    new AcceptableValueRange<float>(60f, 100f),
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
                Advanced(
                    "How much a settled wingman rolls to match your bank, 0 to 1. This blends " +
                    "with the autopilot's own roll rather than overriding it, and disengages " +
                    "past a hard bank limit and near the ground. Set to 0 to switch it off.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        private void BindRotary(ConfigFile c)
        {
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
                    "20 is what makes those terms cancel at hover power.",
                    new AcceptableValueRange<float>(5f, 40f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private void BindAi(ConfigFile c)
        {
            AiTweakEnabled = c.Bind("AI", "EnableAiTweak", false,
                Hidden("Retired compatibility key. Wing Command no longer changes global AI skill or bravery."));
            AiSkillScale = c.Bind("AI", "SkillScale", 1.0f,
                Hidden("Retired compatibility key.", new AcceptableValueRange<float>(0.25f, 3f)));
            AiBraveryScale = c.Bind("AI", "BraveryScale", 1.0f,
                Hidden("Retired compatibility key.", new AcceptableValueRange<float>(0.25f, 3f)));
            MutualSupport = c.Bind("AI", "MutualSupport", true,
                Hidden("Retired compatibility key. ROE no longer changes the current flight task; " +
                       "use the Engage order to authorise pursuit."));
            PlayerTargetPenalty = c.Bind("AI", "PlayerConcentrationPenalty", 0f,
                Hidden("Retired compatibility key. Player aircraft receive no special protection.",
                    new AcceptableValueRange<float>(0f, 8f)));
            PanicSystem = c.Bind("AI", "PanicSystem", true,
                Advanced("Temporarily interrupt any wing order when that wingman receives a missile " +
                         "warning: announce defensive, select the correct countermeasure, evade, then " +
                         "resume the queued order after the warning clears."));
            PanicClearSeconds = c.Bind("AI", "PanicClearSeconds", 2.5f,
                new ConfigDescription(
                    "How long a missile warning must remain clear before a defensive wingman " +
                    "resumes its order.",
                    new AcceptableValueRange<float>(0.5f, 10f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private void BindEngagement(ConfigFile c)
        {
            // Renamed from DefaultPosture / DefensiveEngageRange / AggressiveEngageRange.
            // BepInEx applies a default only to a NEW key, and the old values ("Defensive")
            // would not parse as a WingRoe anyway - so these had to be new names for the
            // new defaults to take at all. The old keys go inert.
            DefaultRoe = c.Bind("Engagement", "DefaultRoe", WingRoe.Hold,
                "Rules of engagement the wing starts a mission with. Defend limits fire to " +
                "missile defence and mirrored attacks; Escort prioritises threats around " +
                "the formation; Free may shoot opportunity targets without changing orders.");
            MissileDefence = c.Bind("Engagement", "MissileDefence", true,
                Advanced("Defensive wingmen prioritise shooting down inbound missiles, on themselves or " +
                         "on you, when they carry a weapon capable of it."));
            JammingEnabled = c.Bind("Engagement", "JammingEnabled", true,
                "Allow the Jam Target order: a jam-capable wingman holds its formation slot " +
                "and runs its radar jammer continuously against a designated unit until that " +
                "unit dies or the order is replaced.");
            HoldEngageRange = c.Bind("Engagement", "HoldEngageRange", 6000f,
                Advanced(
                    "How far a wingman will shoot from its slot, in metres. Used by Hold and " +
                    "Escort: neither manoeuvres to engage, so for both it is purely a " +
                    "weapons-range limit.",
                    new AcceptableValueRange<float>(500f, 30000f)));
            FreeEngageRange = c.Bind("Engagement", "FreeEngageRange", 12000f,
                Advanced("Weapons range for a Free wingman.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            LeashRadius = c.Bind("Engagement", "LeashRadius", 8000f,
                Advanced(
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
                Advanced(
                    "How long Defensive wingmen stay weapons-free against ground targets after " +
                    "you fire an anti-surface weapon.",
                    new AcceptableValueRange<float>(2f, 120f)));
            FireInterval = c.Bind("Engagement", "FireInterval", 5f,
                Advanced(
                    "Minimum seconds between shots from one wingman. Without a gap they fire " +
                    "every engagement tick and empty the aircraft in seconds.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            MaxWingmenPerTarget = c.Bind("Engagement", "MaxWingmenPerTarget", 2,
                new ConfigDescription(
                    "Hard ceiling on simultaneous wingmen assigned to one target. Weapon " +
                    "effectiveness may choose fewer; missiles always receive one interceptor.",
                    new AcceptableValueRange<int>(1, 4),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoReturnOnEmpty = c.Bind("Engagement", "AutoReturnOnEmpty", true,
                "Wingmen return to base on their own once out of ammunition or down to bingo fuel.");
            BingoFuel = c.Bind("Engagement", "BingoFuel", 0.15f,
                new ConfigDescription("Fuel fraction at which a wingman calls bingo and heads home.",
                    new AcceptableValueRange<float>(0.05f, 0.5f)));
            RtbReturnsToReserve = c.Bind("Engagement", "RtbReturnsToReserve", true,
                "A wingman that completes a Return To Base order hands its airframe back to " +
                "the faction's stock and leaves the world, instead of parking on the apron " +
                "and being written off. Host or single-player only. Turn this off for " +
                "missions that expect recovered aircraft to stay where they landed.");
            TakeoverOnDeath = c.Bind("Engagement", "TakeoverOnDeath", true,
                "When your pilot dies or ejects, offer control of a surviving aircraft in " +
                "your wing. Host or single-player only; mission failures unrelated to the " +
                "player's aircraft are never suppressed.");

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
        }

        private void BindManoeuvres(ConfigFile c)
        {
            AerobaticsEnabled = c.Bind("Manoeuvres", "AerobaticsEnabled", true,
                "Allow the aerobatic manoeuvres (Split-S, Immelmann, Barrel Roll, Aileron " +
                "Roll, Loop). The level breaks and the wing waggle are always available.");
            ManeuverAltitudeFloor = c.Bind("Manoeuvres", "EntryAltitudeFloor", 250f,
                new ConfigDescription(
                    "Height above ground, in metres, a wingman must have before it will " +
                    "start a manoeuvre. Each manoeuvre also has its own minimum on top of this.",
                    new AcceptableValueRange<float>(60f, 2000f)));
            ManeuverHardFloor = c.Bind("Manoeuvres", "HardFloor", 120f,
                new ConfigDescription(
                    "If a wingman descends through this radar altitude mid-manoeuvre it " +
                    "abandons it wings-level and rejoins. The last-ditch anti-crash guard.",
                    new AcceptableValueRange<float>(40f, 1000f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManeuverMinSpeedFraction = c.Bind("Manoeuvres", "MinEntrySpeedFraction", 0.35f,
                new ConfigDescription(
                    "Baseline airspeed, as a fraction of the airframe's maximum, below which " +
                    "a manoeuvre is refused. Individual manoeuvres raise this for themselves.",
                    new AcceptableValueRange<float>(0.1f, 0.9f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private void BindLoadout(ConfigFile c)
        {
            // Written by the LOADOUT tab, not by hand, but left visible rather than hidden
            // so a player who has made a mess of their templates can clear the value
            // instead of hunting for where they live. The reader drops any record it
            // cannot parse, so editing it badly costs that record and nothing else.
            LoadoutTemplates = c.Bind("Loadout", "SavedTemplates", "",
                Advanced("Saved per-pylon loadout templates, written by the WMC LOADOUT tab. " +
                       "One record per template as airframe|id|name|store keys, records " +
                       "separated by semicolons. Clear this to delete every saved template."));
        }

        private void BindShop(ConfigFile c)
        {
            ShopEnabled = c.Bind("Shop", "ShopEnabled", true,
                Advanced("Allow buying wingmen. Aircraft are priced from the same value the player's " +
                         "own aircraft menu uses, paid for out of your allocation, and drawn from " +
                         "your faction's stock - so a purchase competes with the mission's own AI."));
            RecruitmentCostRate = c.Bind("Shop", "RecruitmentCostPercent", 0.25f,
                new ConfigDescription(
                    "Fraction of an airframe's list price charged the first " +
                    "time an already-active mission aircraft is assigned to your wing.",
                    new AcceptableValueRange<float>(0f, 1f)));
            AdditionalWingReserve = c.Bind("Shop", "AdditionalWingReservePerType", 0,
                Hidden(
                    "Retired compatibility key. The wing reserve now holds three concrete " +
                    "airframes selected in WMC or returned safely from the wing.",
                    new AcceptableValueRange<int>(0, 2)));
            ExceedLimitCostMultiplier = c.Bind("Shop", "ExceedSquadronLimitCost", 3f,
                new ConfigDescription(
                    "Price multiplier for an airframe requisitioned past the mission's AI " +
                    "aircraft limit. Missions often leave a limit of zero once the player's " +
                    "own presence is subtracted, so this is what keeps the shop usable there " +
                    "without making the cap meaningless.",
                    new AcceptableValueRange<float>(1f, 10f)));
            ExceedLimitRank = c.Bind("Shop", "ExceedSquadronLimitRank", 3,
                new ConfigDescription(
                    "Player rank required before the squadron limit may be exceeded at all.",
                    new AcceptableValueRange<int>(0, 10)));
            ExceedLimitAllowance = c.Bind("Shop", "ExceedSquadronLimitAllowance", 3,
                new ConfigDescription(
                    "How many over-limit airframes you may have flying at once. The allowance " +
                    "frees up as they are lost or recovered, so this caps how far past the " +
                    "mission's cap the shop can take you rather than how often you may buy.",
                    new AcceptableValueRange<int>(1, 3)));

            // Retired with the move to flat pricing and base-only delivery. Kept so existing
            // configuration files still parse; nothing reads them.
            WingPriceGrowth = c.Bind("Shop", "WingPriceGrowth", 1.5f,
                Hidden("Retired compatibility key. Airframes are priced at list value; the " +
                       "price no longer compounds with wing size.",
                    new AcceptableValueRange<float>(1f, 3f)));
            FastDeliverySurcharge = c.Bind("Shop", "FastDeliverySurcharge", 0.25f,
                Hidden("Retired compatibility key. Fast delivery has been removed; " +
                       "requisitioned aircraft always launch from an airbase.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FastDeliveryDistance = c.Bind("Shop", "FastDeliveryDistance", 2000f,
                Hidden("Retired compatibility key. Fast delivery has been removed.",
                    new AcceptableValueRange<float>(500f, 10000f)));

            IncludeUndeclaredAircraft = c.Bind("Shop", "IncludeUndeclaredAircraft", false,
                Advanced("Compatibility option: also offer airframes the mission did not stock, " +
                         "using a separate per-mission allowance. Disabled in the release profile."));
            UndeclaredStock = c.Bind("Shop", "UndeclaredStock", 3f,
                new ConfigDescription(
                    "How many of each undeclared airframe may be bought per mission.",
                    new AcceptableValueRange<float>(1f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private void BindPilots(ConfigFile c)
        {
            // Pilots. The XP figures are deliberately ordinary numbers rather than a
            // formula: the whole curve is one triangular step (XpPerRank), so moving a
            // single value retunes progression without any two settings disagreeing.
            PilotProgression = c.Bind("Pilot", "PilotProgression", true,
                "Wing pilots keep a callsign, a record and a rank that rises with " +
                "kills, completed sorties and engagements survived. Rank has a small " +
                "effect on how well they shoot; see RankEffect.");
            XpPerKill = c.Bind("Pilot", "XpPerKill", 25,
                new ConfigDescription(
                    "Experience awarded when a contact a wingman was shooting at is destroyed.",
                    new AcceptableValueRange<int>(0, 500)));
            XpPerSortie = c.Bind("Pilot", "XpPerSortie", 40,
                new ConfigDescription(
                    "Experience awarded for bringing an airframe home or completing a " +
                    "cargo delivery.",
                    new AcceptableValueRange<int>(0, 500)));
            XpPerEngagement = c.Bind("Pilot", "XpPerEngagement", 10,
                new ConfigDescription(
                    "Experience awarded for surviving a missile engagement.",
                    new AcceptableValueRange<int>(0, 500)));
            XpPerRank = c.Bind("Pilot", "XpPerRank", 120,
                new ConfigDescription(
                    "Experience step between ranks. Thresholds grow triangularly from it: " +
                    "Wingman at one step, Veteran at three, Ace at six, Legend at ten.",
                    new AcceptableValueRange<int>(10, 2000)));
            RankEffect = c.Bind("Pilot", "RankEffect", 1f,
                new ConfigDescription(
                    "How much rank actually changes a wingman's shooting. At 1 a Legend " +
                    "gets roughly 12% more weapon reach and off-boresight tolerance and " +
                    "cycles shots about 12% faster than a rookie; 0 makes rank purely a " +
                    "record.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        private void BindComms(ConfigFile c)
        {
            RadioChatter = c.Bind("Comms", "RadioChatter", true,
                "Show named, pilot-specific squadron transmissions for orders, engagements, " +
                "defensive calls, Winchester and rejoins.");

            RadioChatterSound = c.Bind("Comms", "RadioChatterSound", true,
                "Open each squadron transmission with the game's own radio click - the same " +
                "sound mission and HQ messages use. Has no effect while RadioChatter is off.");
        }

        private void BindRadial(ConfigFile c)
        {
            UseNativeRadial = c.Bind("UI", "UseNativeRadial", true,
                Advanced("Add a compact 'Wing Command' entry to the game's own radial menu. This uses " +
                         "the game's Rewired look-axis input while the cursor is captured."));
        }

        private void BindDebug(ConfigFile c)
        {
            // A display-only row. ConfigurationManager has no notion of a category header,
            // so the way to say something once above a group is to bind an entry nobody
            // reads and let its drawer be a sentence. Ordered above everything else in the
            // category; the stored value is meaningless.
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

            // Visible, where it used to be Browsable=false. The action it gates now lives in
            // this window rather than on the WMC panel, and a switch you can only reach by
            // hand-editing the .cfg is not a switch the button below it can tell you to flip.
            EnableDebugActions = c.Bind("Debug", "EnableDebugActions", false,
                new ConfigDescription(
                    "Allow the development-only actions below. They are cheats and host-only.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Enable debug actions",
                        Order = 40,
                    }));
            SpawnDebugWing = c.Bind("Debug", "SpawnDebugWing", false,
                new ConfigDescription(
                    "DEBUG CHEAT: Spawn a full wing of your own aircraft type, already in " +
                    "formation slots, and assign them. Requires the switch above.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Spawn wing of my aircraft",
                        Order = 30,
                        CustomDrawer = WingDebugActions.DrawSpawnButton,

                        // The stored value is never read — the row is a button, and a
                        // reset-to-default control on it would be meaningless.
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
                    "DEBUG CHEAT: Ignore MaxWingSize when assigning or requisitioning aircraft. " +
                    "Formation geometry, HUD layout, performance and mission scripting are not " +
                    "supported for an unlimited wing and may break.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Disable wing size limit",
                        Order = 10,
                    }));
        }

        private void BindUi(ConfigFile c)
        {
            ShowHud = c.Bind("UI", "ShowWingHud", true,
                "Draw the compact wing status readout beside the tactical map while you have " +
                "wingmen assigned.");
            UseMfdPanel = c.Bind("UI", "UseMfdPanel", true,
                Advanced("Add a WMC screen to the cockpit MFD bezel, alongside BDF/MAP/HUD."));
            HighlightWingOnMap = c.Bind("UI", "HighlightWingOnMap", true,
                Advanced("Tint your wingmen's map icons so they stand out from the friendly force."));
            HighlightWingOnHud = c.Bind("UI", "HighlightWingOnHud", true,
                Advanced("Tint your wingmen's in-cockpit HUD markers to match the map."));
            HighlightWingTargets = c.Bind("UI", "HighlightWingTargets", true,
                Advanced("Mark units your wing is engaging on both the map and HUD."));
            WingTargetColor = c.Bind("UI", "WingTargetColor", "#FFB020",
                Advanced("Hex colour for units your wing is engaging."));
            // New key on purpose. BepInEx preserves an existing value forever, so merely
            // changing WingIconColor's default would leave every current installation on
            // the old sky-blue colour. Binding WingMemberColor moves existing users to the
            // higher-contrast green while leaving the retired key harmlessly inert.
            WingIconColor = c.Bind("UI", "WingMemberColor", "#39FF65",
                Advanced("Hex colour for wingmen across the roster, tactical map and HUD."));
            MapCommandEnabled = c.Bind("UI", "MapCommands", true,
                Advanced("Enable tactical wing selection and point tasking on the maximised map."));
            VerboseLogging = c.Bind("Debug", "VerboseLogging", false,
                Advanced("Log every order and state transition to the BepInEx console."));
        }
    }
}
