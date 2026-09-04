using BepInEx.Configuration;
using UnityEngine;

namespace WingCommand
{
    /// <summary>How much of the squadron radio the player wants.</summary>
    internal enum ChatterLevel
    {
        Off,
        Text,
        TextAndTone,
    }

    /// <summary>How far the wing's own colouring reaches across the map and HUD.</summary>
    internal enum HighlightMode
    {
        Off,
        Wing,
        WingAndTargets,
    }

    /// <summary>
    /// The settings a player has an opinion about, and no others.
    ///
    /// This list used to run to eighty-one entries. Ten were retired keys nothing read, and
    /// fifty-three were tuned numbers with no answer to "what should I set this to?" - bank
    /// authorities, engagement ranges, XP awards, a helicopter power constant derived from
    /// the game's own collective formula. Those now live in <see cref="WingTuning"/> as
    /// constants, with the reasoning attached, where they can be changed by someone who can
    /// see the consequence. What is left is preference: how the wing flies, what it is
    /// allowed to do, and what you see of it.
    ///
    /// <see cref="Mode"/> is the one switch for cost. It gates the expensive behaviour in
    /// <see cref="WingBrain"/> rather than asking the player to switch off manoeuvres,
    /// jamming, deconfliction and chatter one at a time, which is what the old set did.
    /// </summary>
    internal class WingConfig
    {
        // --- Keys ---
        public ConfigEntry<KeyCode> RadialKey { get; private set; }
        public ConfigEntry<KeyCode> QuickRejoinKey { get; private set; }
        public ConfigEntry<KeyCode> QuickEngageKey { get; private set; }

        // --- AI ---
        public ConfigEntry<WingMode> Mode { get; private set; }

        // --- Engagement ---
        public ConfigEntry<WingRoe> DefaultRoe { get; private set; }
        public ConfigEntry<bool> AutoReturnOnEmpty { get; private set; }
        public ConfigEntry<bool> RtbReturnsToReserve { get; private set; }
        public ConfigEntry<bool> TakeoverOnDeath { get; private set; }

        // --- Comms ---
        public ConfigEntry<ChatterLevel> Radio { get; private set; }

        // --- Pilots ---
        public ConfigEntry<bool> PilotProgression { get; private set; }

        // --- Shop ---
        public ConfigEntry<bool> ShopEnabled { get; private set; }

        // --- Loadout ---
        public ConfigEntry<string> LoadoutTemplates { get; private set; }

        // --- UI ---
        public ConfigEntry<bool> ShowHud { get; private set; }
        public ConfigEntry<bool> UseMfdPanel { get; private set; }
        public ConfigEntry<bool> MapCommandEnabled { get; private set; }
        public ConfigEntry<bool> FitMapToPanels { get; private set; }
        public ConfigEntry<HighlightMode> Highlight { get; private set; }
        public ConfigEntry<string> WingIconColor { get; private set; }
        public ConfigEntry<string> WingTargetColor { get; private set; }
        public ConfigEntry<bool> TacticalPauseInSingleplayer { get; private set; }
        public ConfigEntry<bool> ExternalHitmarkerAudio { get; private set; }

        // --- MFD ---
        public ConfigEntry<bool> UseSetPanel { get; private set; }
        public ConfigEntry<float> MfdBackgroundOpacity { get; private set; }
        public ConfigEntry<bool> MfdCheckeredGrid { get; private set; }
        public ConfigEntry<bool> MfdCustomImageEnabled { get; private set; }
        public ConfigEntry<string> MfdCustomImageFile { get; private set; }

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
        public ConfigEntry<bool> VerboseLogging { get; private set; }

        // The cheats read through these, never through their own entry. EnableDebugActions
        // says "allow the development-only actions below", and used to gate only the spawn
        // button: free purchases and the wing-size bypass took effect on their own, so the
        // master switch was wrong about its own section. Reading the pair here rather than
        // ANDing at each of the seven call sites is what keeps it that way.
        public bool CheatFreePurchases => EnableDebugActions.Value && FreePlanePurchases.Value;
        public bool CheatNoWingLimit => EnableDebugActions.Value && DisableWingSizeLimit.Value;

        private const string HexHelp = "Six-digit hex, with or without the leading #.";

        /// <summary>
        /// Rejects a malformed colour at bind time rather than letting it fail silently.
        /// A typo used to reach <c>ColorUtility.TryParseHtmlString</c>, fail, and leave the
        /// icon whatever the fallback was, with nothing anywhere to say why - so the setting
        /// looked as though it did not work. BepInEx reverts an unacceptable value to the
        /// default and says so in the log, which is the whole fix.
        /// </summary>
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
            // Bound in this order and no other: BepInEx writes the .cfg in bind order, so
            // reordering these silently reshuffles every existing configuration file under
            // the player. Keys removed since the last release stay in existing files as
            // orphaned lines, which BepInEx ignores and rewrites away on the next save.
            BindMode(c);
            BindEngagement(c);
            BindComms(c);
            BindPilots(c);
            BindShop(c);
            BindLoadout(c);
            BindKeys(c);
            BindUi(c);
            BindMfd(c);
            BindDebug(c);
        }

        private void BindMode(ConfigFile c)
        {
            // The one switch most players ever touch, and the only one that trades behaviour
            // for cost. Ordered to the top of the window.
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

            // The middle rung was renamed Escort -> Tight. A config file written before the
            // rename holds "DefaultRoe = Escort", which no longer parses, so BepInEx has
            // silently substituted the default above. Recover the choice from the file on
            // disk and carry it across.
            if (DefaultRoe.Value == WingRoe.Hold && FileMentionsLegacyRoe(c, "Escort"))
                DefaultRoe.Value = WingRoe.Tight;
            AutoReturnOnEmpty = c.Bind("Engagement", "AutoReturnOnEmpty", true,
                "Wingmen return to base on their own once out of ammunition or down to " +
                "bingo fuel, instead of holding station empty.");
            RtbReturnsToReserve = c.Bind("Engagement", "RtbReturnsToReserve", true,
                "A wingman that completes a Return To Base order hands its airframe back to " +
                "the faction's stock and leaves the world, instead of parking on the apron " +
                "and being written off. Host or single-player only. Turn this off for " +
                "missions that expect recovered aircraft to stay where they landed.");
            TakeoverOnDeath = c.Bind("Engagement", "TakeoverOnDeath", true,
                "When your pilot dies or ejects, offer control of a surviving aircraft in " +
                "your wing. Host or single-player only; mission failures unrelated to the " +
                "player's aircraft are never suppressed.");
        }

        /// <summary>
        /// Whether the raw config file still assigns <c>DefaultRoe</c> a value that no longer
        /// parses. Reads the file directly because BepInEx keeps no public record of an entry
        /// it failed to bind.
        /// </summary>
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
            // Was two booleans, the second of which did nothing while the first was off.
            // One ordered choice cannot be set to a combination that means nothing. New key
            // on purpose: "true" would not parse as a ChatterLevel, and a new key is how the
            // new default reaches installations that already have a config file.
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
        }

        private void BindShop(ConfigFile c)
        {
            ShopEnabled = c.Bind("Shop", "ShopEnabled", true,
                "Allow buying wingmen. Aircraft are priced from the same value the player's " +
                "own aircraft menu uses, paid for out of your allocation, and drawn from " +
                "your faction's stock - so a purchase competes with the mission's own AI.");
        }

        private void BindLoadout(ConfigFile c)
        {
            // Written by the LOADOUT tab, not by hand, but left visible rather than hidden so
            // a player who has made a mess of their templates can clear the value instead of
            // hunting for where they live. The reader drops any record it cannot parse, so
            // editing it badly costs that record and nothing else.
            LoadoutTemplates = c.Bind("Loadout", "SavedTemplates", "",
                Advanced("Saved per-pylon loadout templates, written by the WMC LOADOUT tab. " +
                         "One record per template as airframe|id|name|store keys, records " +
                         "separated by semicolons. Clear this to delete every saved template."));
        }

        private void BindKeys(ConfigFile c)
        {
            // Purely additive. This used to double as the opt-out from the game's own wheel,
            // which meant a key left bound in a config silently deleted the Wing Command
            // slice with no message anywhere - see WingCommandManager.NativeRadialActive.
            RadialKey = c.Bind("Keys", "WingMenu", KeyCode.None,
                "Optional: hold this to open Wing Command's own wheel. The Wing Command slice " +
                "is added to the game's radial menu either way, so leave it unbound unless you " +
                "want a second key that goes straight to the wing commands.");
            QuickRejoinKey = c.Bind("Keys", "QuickRejoin", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to rejoin formation."));
            QuickEngageKey = c.Bind("Keys", "QuickEngage", KeyCode.None,
                Advanced("Optional hotkey: order the whole wing to engage."));
        }

        private void BindUi(ConfigFile c)
        {
            ShowHud = c.Bind("UI", "ShowWingHud", true,
                "Draw the compact wing status readout beside the tactical map while you have " +
                "wingmen assigned.");
            UseMfdPanel = c.Bind("UI", "UseMfdPanel", true,
                "Add a WMC screen to the cockpit MFD bezel, alongside BDF/MAP/HUD.");
            MapCommandEnabled = c.Bind("UI", "MapCommands", true,
                "Enable tactical wing selection and point tasking on the maximised map.");
            // Same key and default as the row-layout version this replaced: renaming it
            // would strand the old key in every existing config file.
            FitMapToPanels = c.Bind("UI", "FitMapToPanels", true,
                "Lay the maximised tactical map out in three columns - MFD panels on the left, " +
                "the map enlarged in the centre, and every bezel button in one rail on the right. " +
                "Off restores the stock centred map with a bezel column down each side.");

            // Was three booleans that nobody wanted to set independently - the map and HUD
            // tints are one decision seen from two places, and targets are a step further out.
            Highlight = c.Bind("UI", "Highlight", HighlightMode.WingAndTargets,
                "How much of the wing gets its own colour. Wing tints your wingmen's icons " +
                "and markers on both the map and the in-cockpit HUD; WingAndTargets also " +
                "marks the units they are engaging.");
            WingIconColor = c.Bind("UI", "WingMemberColor", "#39FF65",
                Advanced("Hex colour for wingmen across the roster, tactical map and HUD. " +
                         HexHelp, HexColour));
            WingTargetColor = c.Bind("UI", "WingTargetColor", "#FFB020",
                Advanced("Hex colour for units your wing is engaging. " + HexHelp, HexColour));
            TacticalPauseInSingleplayer = c.Bind("UI", "TacticalPauseInSingleplayer", false,
                "Slow down game time to 0.25x while the tactical command screen is active in singleplayer for tactical planning.");
            ExternalHitmarkerAudio = c.Bind("UI", "ExternalHitmarkerAudio", true,
                "Play hitmarker audio confirmation when landing hits in 3rd-person external/orbit camera views.");
        }

        private void BindMfd(ConfigFile c)
        {
            UseSetPanel = c.Bind("MFD", "UseSetPanel", true,
                "Add a SET (Settings) screen to the cockpit MFD bezel, alongside WMC/BDF/MAP/HUD.");
            MfdBackgroundOpacity = c.Bind("MFD", "BackgroundOpacity", 0.40f,
                new ConfigDescription(
                    "Opacity of the tactical MFD background (0.0 = fully transparent, 1.0 = solid opaque).",
                    new AcceptableValueRange<float>(0f, 1f)));
            MfdCheckeredGrid = c.Bind("MFD", "CheckeredGrid", false,
                "Draw the checkered datum grid across the MFD background.");
            MfdCustomImageEnabled = c.Bind("MFD", "CustomImageEnabled", false,
                "Display a custom user-uploaded wallpaper image as the MFD background.");
            MfdCustomImageFile = c.Bind("MFD", "CustomImageFile", "",
                "Filename or path of user-uploaded wallpaper image in BepInEx/config/WingCommand/Backgrounds/.");
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
                    "DEBUG CHEAT: Ignore the wing size limit when assigning or requisitioning aircraft. " +
                    "Formation geometry, HUD layout, performance and mission scripting are not " +
                    "supported for an unlimited wing and may break.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "Disable wing size limit",
                        Order = 10,
                    }));
            VerboseLogging = c.Bind("Debug", "VerboseLogging", false,
                new ConfigDescription(
                    "Log every order and state transition to the BepInEx console.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Order = 5 }));
        }
    }
}
