using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The small set of loadout configurations a wingman can be requisitioned with.
    ///
    /// Presets rather than a station-by-station editor, deliberately. The player is
    /// choosing what a wingman is for — shooting aircraft, shooting things on the ground,
    /// or hauling supplies — not building a strike package. A full hardpoint editor is the
    /// aircraft selection menu's job and the game already has one.
    /// </summary>
    internal enum WingLoadoutPreset
    {
        /// <summary>
        /// The airframe's own standard AI fit. This is what a null loadout produces, and
        /// what the faction's own aircraft launch with.
        /// </summary>
        Standard,

        AirToAir,
        AirToGround,
        Balanced,
        Cargo,
    }

    /// <summary>
    /// One aircraft's loadout decision.
    ///
    /// Three things it can be, in order of precedence: a saved per-pylon template, a role
    /// preset with an optional cargo choice, or the airframe's own standard fit. A template
    /// wins when one is set, because it is the more specific statement — the player named
    /// every station rather than describing a job.
    ///
    /// This stays one struct with three cases rather than three types because every part of
    /// the mod that moves a loadout about — the shop, delivery, the reserve, recovery,
    /// takeover — carries it by value and never inspects it. Adding a case here reaches all
    /// of them without any of them changing.
    /// </summary>
    internal readonly struct WingLoadoutChoice
    {
        public readonly WingLoadoutPreset Preset;

        /// <summary>
        /// The chosen cargo mount's <c>jsonKey</c>, or null for the first available one.
        /// Stored as the key rather than the object so a choice survives a mission reload
        /// without holding a reference to a destroyed prefab.
        /// </summary>
        public readonly string CargoKey;

        /// <summary>
        /// The saved template this fit came from, or null when it is a preset.
        ///
        /// An id rather than the record itself, for the same reason the cargo mount is a
        /// key: an in-flight aircraft, a reserve slot and a purchase order may all outlive
        /// the template they were fitted from, and none of them should keep it alive or
        /// follow it through a rename into something else.
        /// </summary>
        public readonly string TemplateId;

        public WingLoadoutChoice(WingLoadoutPreset preset, string cargoKey = null,
                                 string templateId = null)
        {
            Preset = preset;
            CargoKey = cargoKey;
            TemplateId = templateId;
        }

        public static WingLoadoutChoice Standard => new WingLoadoutChoice(WingLoadoutPreset.Standard);

        /// <summary>Choosing a preset abandons any template: they are alternatives.</summary>
        public WingLoadoutChoice WithPreset(WingLoadoutPreset preset) =>
            new WingLoadoutChoice(preset, CargoKey);

        public WingLoadoutChoice WithCargo(string cargoKey) =>
            new WingLoadoutChoice(Preset, cargoKey, TemplateId);

        /// <summary>Fit from a saved template, or pass null to go back to the preset.</summary>
        public WingLoadoutChoice WithTemplate(string templateId) =>
            new WingLoadoutChoice(Preset, CargoKey, templateId);

        public bool IsTemplate => !string.IsNullOrEmpty(TemplateId);

        /// <summary>True when nothing at all has been chosen and the game fits its own.</summary>
        public bool IsStandard => !IsTemplate && Preset == WingLoadoutPreset.Standard;
    }

    /// <summary>
    /// Reads the airframe's own weapon-station data and turns a preset into a real
    /// <c>Loadout</c> the spawner accepts.
    ///
    /// Nothing here is a weapon catalogue of this mod's own. Every option comes from
    /// <c>WeaponManager.hardpointSets[i].weaponOptions</c> — the same list the game's
    /// aircraft selection menu fills its per-hardpoint dropdowns from — and every option is
    /// ranked by the same <c>WeaponInfo.effectiveness</c> figures <c>WingWeapons</c>
    /// already uses to pick a station in flight.
    /// A preset is therefore a rule over the airframe's stock choices, not a list of
    /// weapons this mod believes an aircraft ought to carry.
    ///
    /// The one thing that cannot be reached through a compiled reference is the route from
    /// a <c>WeaponMount</c> prefab to the <c>WeaponStation</c> it will become, because that
    /// member is private. It is resolved reflectively once per mount, in the same spirit as
    /// <see cref="GameAccess"/>: if a game update moves it, <see cref="Available"/> goes
    /// false, only the Standard preset is offered, and the panel says why rather than
    /// silently fitting the wrong weapons.
    /// </summary>
    internal static class WingLoadoutCatalog
    {
        /// <summary>One weapon option on one hardpoint set, with its stock role figures.</summary>
        private sealed class MountInfo
        {
            public WeaponMount Mount;
            public string Key;
            public string Label;
            public float AntiAir;
            public float AntiSurface;
            public float AntiMissile;
            public float MaxRange;
            public bool Cargo;

            /// <summary>Rounds carried, straight off the mount. Zero where it is not a count.</summary>
            public int Ammo;

            /// <summary>Loaded mass, for the fitted-weight line under the pylon list.</summary>
            public float Mass;

            public bool Armed => AntiAir > 0f || AntiSurface > 0f || AntiMissile > 0f;
        }

        /// <summary>
        /// One store the player can put on one pylon, as the editor needs to draw it.
        ///
        /// A projection of <see cref="MountInfo"/> rather than the thing itself: the editor
        /// has no business holding a <c>WeaponMount</c> prefab reference, and the figures
        /// here are the ones a row shows.
        /// </summary>
        internal readonly struct StoreOption
        {
            public readonly string Key;
            public readonly string Label;
            public readonly int Ammo;
            public readonly float Mass;
            public readonly float AntiAir;
            public readonly float AntiSurface;
            public readonly bool Cargo;

            public StoreOption(string key, string label, int ammo, float mass,
                               float antiAir, float antiSurface, bool cargo)
            {
                Key = key;
                Label = label;
                Ammo = ammo;
                Mass = mass;
                AntiAir = antiAir;
                AntiSurface = antiSurface;
                Cargo = cargo;
            }

            public bool IsEmpty => string.IsNullOrEmpty(Key);

            /// <summary>What the store is for, in the two letters a table cell can hold.</summary>
            public string RoleTag =>
                Cargo ? "CGO"
                : AntiAir <= 0f && AntiSurface <= 0f ? ""
                : AntiAir > AntiSurface * 1.5f ? "A-A"
                : AntiSurface > AntiAir * 1.5f ? "A-G"
                : "MLT";
        }

        /// <summary>Everything known about one airframe's hardpoints, resolved once.</summary>
        private sealed class Profile
        {
            public HardpointSet[] Sets;
            public List<MountInfo>[] Options;
            public bool HasRoleData;
            public readonly List<CargoOption> Cargo = new List<CargoOption>();
        }

        /// <summary>A cargo the player can pick for a transport, named as the game names it.</summary>
        internal readonly struct CargoOption
        {
            public readonly string Key;
            public readonly string Label;

            public CargoOption(string key, string label)
            {
                Key = key;
                Label = label;
            }
        }

        private static readonly Dictionary<AircraftDefinition, Profile> profiles =
            new Dictionary<AircraftDefinition, Profile>();

        private static readonly List<CargoOption> noCargo = new List<CargoOption>();

        /// <summary>
        /// False only when nothing has ever been read successfully.
        ///
        /// One airframe throwing is that airframe's problem — it offers the standard fit and
        /// the panel says so for that aircraft. The feature is only reported as unavailable
        /// when no airframe anywhere has yielded stock role data, which is what a game update
        /// moving the members underneath this looks like.
        /// </summary>
        public static bool Available => roleDataSeen || !probeFailed;

        public static string UnavailableReason { get; private set; }

        /// <summary>Drop cached prefab data when a mission ends; prefabs may be reloaded.</summary>
        public static void Reset()
        {
            profiles.Clear();
            blindProfilesLogged = false;
        }

        // ------------------------------------------------------------------- queries

        /// <summary>The presets that can actually be fitted to this airframe, Standard first.</summary>
        public static void PresetsFor(AircraftDefinition definition, List<WingLoadoutPreset> into)
        {
            if (into == null) return;
            into.Clear();
            into.Add(WingLoadoutPreset.Standard);

            Profile profile = ProfileOf(definition);
            if (profile == null) return;

            if (profile.HasRoleData)
            {
                if (CanFit(profile, WingLoadoutPreset.AirToAir))
                    into.Add(WingLoadoutPreset.AirToAir);
                if (CanFit(profile, WingLoadoutPreset.AirToGround))
                    into.Add(WingLoadoutPreset.AirToGround);
                if (CanFit(profile, WingLoadoutPreset.Balanced))
                    into.Add(WingLoadoutPreset.Balanced);
            }

            // Checked outside the role-data gate on purpose: a transport whose only readable
            // stores are cargo pods still has a cargo choice worth offering.
            if (profile.Cargo.Count > 0) into.Add(WingLoadoutPreset.Cargo);
        }

        /// <summary>The cargo types the airframe's own hardpoints offer.</summary>
        public static IReadOnlyList<CargoOption> CargoOptionsFor(AircraftDefinition definition)
        {
            Profile profile = ProfileOf(definition);
            return profile != null ? (IReadOnlyList<CargoOption>)profile.Cargo : noCargo;
        }

        /// <summary>The cargo actually selected by a choice, falling back to the first offered.</summary>
        public static bool ResolveCargo(AircraftDefinition definition, string key,
                                        out CargoOption option)
        {
            option = default(CargoOption);
            IReadOnlyList<CargoOption> options = CargoOptionsFor(definition);
            if (options.Count == 0) return false;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Key == key)
                {
                    option = options[i];
                    return true;
                }
            }

            option = options[0];
            return true;
        }

        /// <summary>Short panel label for a choice, e.g. <c>AIR-GND</c> or <c>CARGO SUPPLY</c>.</summary>
        public static string Label(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            // Every readout on the panel — the shop's fit line, the wing tab's carrying
            // column, the reserve — comes through here, so naming the template in one place
            // names it everywhere.
            if (choice.IsTemplate)
                return UiTheme.Truncate(WingLoadoutTemplates.NameOf(choice.TemplateId), 20)
                             .ToUpperInvariant();

            switch (choice.Preset)
            {
                case WingLoadoutPreset.AirToAir:    return "AIR-AIR";
                case WingLoadoutPreset.AirToGround: return "AIR-GND";
                case WingLoadoutPreset.Balanced:    return "BALANCED";
                case WingLoadoutPreset.Cargo:
                    return ResolveCargo(definition, choice.CargoKey, out CargoOption cargo)
                        ? "CARGO " + UiTheme.Truncate(cargo.Label, 12).ToUpperInvariant()
                        : "CARGO";
                default: return "STANDARD";
            }
        }

        // ------------------------------------------------------------- pylon editing

        /// <summary>How many hardpoint sets this airframe declares. Zero when unreadable.</summary>
        public static int PylonCount(AircraftDefinition definition)
        {
            Profile profile = ProfileOf(definition);
            return profile?.Sets?.Length ?? 0;
        }

        /// <summary>
        /// What to call one pylon.
        ///
        /// The airframe names its own hardpoint sets, so the editor shows the game's names
        /// rather than inventing "STATION 3". A symmetric pair is named once, from
        /// <c>SymmetryName</c>, because the two are edited together.
        /// </summary>
        public static string PylonName(AircraftDefinition definition, int index)
        {
            Profile profile = ProfileOf(definition);
            if (profile?.Sets == null || index < 0 || index >= profile.Sets.Length)
                return "PYLON " + (index + 1);

            HardpointSet set = profile.Sets[index];
            if (set == null) return "PYLON " + (index + 1);

            string name = IsSymmetric(profile, index) && !string.IsNullOrEmpty(set.SymmetryName)
                ? set.SymmetryName
                : set.name;

            return string.IsNullOrEmpty(name) ? "PYLON " + (index + 1) : name;
        }

        /// <summary>
        /// True when this pylon mirrors the one before it.
        ///
        /// The editor hides the mirror and drives it from its partner, because presenting a
        /// left and a right wing station the player cannot arm differently as two rows is a
        /// list twice as long that says half as much.
        /// </summary>
        public static bool MirrorsPrevious(AircraftDefinition definition, int index)
        {
            Profile profile = ProfileOf(definition);
            if (profile?.Sets == null || index <= 0 || index >= profile.Sets.Length) return false;
            return profile.Sets[index] != null && profile.Sets[index].SymmetryWithPrev;
        }

        private static bool IsSymmetric(Profile profile, int index)
        {
            if (profile?.Sets == null) return false;
            if (index >= 0 && index < profile.Sets.Length &&
                profile.Sets[index] != null && profile.Sets[index].SymmetryWithPrev)
                return true;

            int next = index + 1;
            return next < profile.Sets.Length && profile.Sets[next] != null &&
                   profile.Sets[next].SymmetryWithPrev;
        }

        /// <summary>
        /// Every store this pylon will take, the bare pylon first.
        ///
        /// The empty option is a real choice, not a placeholder: an aircraft may launch with
        /// a station left clean, and it is how the player takes weight off.
        /// </summary>
        public static void OptionsFor(AircraftDefinition definition, int index,
                                      List<StoreOption> into)
        {
            if (into == null) return;
            into.Clear();
            into.Add(new StoreOption(null, "— EMPTY —", 0, 0f, 0f, 0f, false));

            Profile profile = ProfileOf(definition);
            if (profile?.Options == null || index < 0 || index >= profile.Options.Length) return;

            List<MountInfo> options = profile.Options[index];
            if (options == null) return;

            for (int i = 0; i < options.Count; i++) into.Add(Project(options[i]));
        }

        /// <summary>The store a key stands for on one pylon, or the empty option.</summary>
        public static StoreOption StoreOn(AircraftDefinition definition, int index, string key)
        {
            var empty = new StoreOption(null, "— EMPTY —", 0, 0f, 0f, 0f, false);
            if (string.IsNullOrEmpty(key)) return empty;

            MountInfo info = Lookup(ProfileOf(definition), index, key);
            return info != null ? Project(info) : new StoreOption(key, "UNKNOWN STORE", 0, 0f,
                                                                  0f, 0f, false);
        }

        private static StoreOption Project(MountInfo info) =>
            new StoreOption(info.Key, info.Label, info.Ammo, info.Mass,
                            info.AntiAir, info.AntiSurface, info.Cargo);

        private static MountInfo Lookup(Profile profile, int index, string key)
        {
            if (profile?.Options == null || index < 0 || index >= profile.Options.Length)
                return null;

            List<MountInfo> options = profile.Options[index];
            if (options == null) return null;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Key == key) return options[i];
            }
            return null;
        }

        /// <summary>
        /// Whether the airframe's own rules currently forbid loading this pylon.
        ///
        /// Asks the game, rather than reimplementing exclusion from
        /// <c>precludingHardpointSets</c>: a conformal tank that blocks the station beneath
        /// it is the airframe's business, and a second opinion here would only be a second
        /// thing to keep in step with the game.
        /// </summary>
        public static bool IsPylonBlocked(AircraftDefinition definition, int index,
                                          Loadout inProgress)
        {
            Profile profile = ProfileOf(definition);
            if (profile?.Sets == null || index < 0 || index >= profile.Sets.Length) return false;

            HardpointSet set = profile.Sets[index];
            if (set == null || inProgress == null) return false;

            try
            {
                return set.BlockedByOtherHardpoint(inProgress);
            }
            catch (Exception e)
            {
                // A pylon whose exclusion rules cannot be evaluated is shown as usable. The
                // spawner applies the same rules again when it fits the aircraft, so the
                // worst case is an option that turns out not to take.
                Fail("checking whether " + SafeName(definition) + " pylon " + (index + 1) +
                     " is blocked failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Turn a template's store keys into a spawnable loadout.
        ///
        /// A key the current build does not recognise leaves that pylon empty rather than
        /// failing the whole fit — the same trade the preset path makes, for the same
        /// reason. Unlike a preset, an all-empty result is honoured: a player who stripped
        /// every station meant it.
        /// </summary>
        public static Loadout BuildFromKeys(AircraftDefinition definition,
                                            IReadOnlyList<string> keys)
        {
            Profile profile = ProfileOf(definition);
            if (profile?.Sets == null || profile.Sets.Length == 0) return null;

            var weapons = new List<WeaponMount>(profile.Sets.Length);
            bool anyUnknown = false;

            for (int i = 0; i < profile.Sets.Length; i++)
            {
                string key = keys != null && i < keys.Count ? keys[i] : null;
                if (string.IsNullOrEmpty(key))
                {
                    weapons.Add(null);
                    continue;
                }

                MountInfo pick = Lookup(profile, i, key);
                if (pick == null) anyUnknown = true;
                weapons.Add(pick?.Mount);
            }

            if (anyUnknown)
                Plugin.Logger.LogInfo(
                    "[Loadout] a saved template for " + SafeName(definition) +
                    " names stores this build does not have; those pylons launch empty.");

            return new Loadout { weapons = weapons };
        }

        /// <summary>
        /// The same fit as <see cref="BuildFromKeys"/>, into a reused loadout.
        ///
        /// For the editor only, which asks what a half-finished template blocks several
        /// times a second. Never hand the result to the spawner: an aircraft keeps the
        /// <c>Loadout</c> it was given, and two aircraft sharing one is the bug
        /// <see cref="WingTakeover"/> and <see cref="WingShopDelivery"/> both carry warnings
        /// about — a whole wing that launches with no ammunition.
        /// </summary>
        public static Loadout FillScratch(AircraftDefinition definition,
                                          IReadOnlyList<string> keys)
        {
            Profile profile = ProfileOf(definition);
            if (profile?.Sets == null || profile.Sets.Length == 0) return null;

            scratchLoadout.weapons.Clear();
            for (int i = 0; i < profile.Sets.Length; i++)
            {
                string key = keys != null && i < keys.Count ? keys[i] : null;
                scratchLoadout.weapons.Add(string.IsNullOrEmpty(key)
                    ? null
                    : Lookup(profile, i, key)?.Mount);
            }
            return scratchLoadout;
        }

        private static readonly Loadout scratchLoadout =
            new Loadout { weapons = new List<WeaponMount>() };

        /// <summary>Short label for a preset on its own, used by the selector buttons.</summary>
        public static string Label(WingLoadoutPreset preset)
        {
            switch (preset)
            {
                case WingLoadoutPreset.AirToAir:    return "AIR-AIR";
                case WingLoadoutPreset.AirToGround: return "AIR-GND";
                case WingLoadoutPreset.Balanced:    return "BALANCED";
                case WingLoadoutPreset.Cargo:       return "CARGO";
                default:                            return "STANDARD";
            }
        }

        // --------------------------------------------------------------------- build

        /// <summary>
        /// Turn a choice into a spawnable loadout, or null to let the game fit the
        /// airframe's own standard equipment.
        ///
        /// Null is returned for far more than the Standard preset: an airframe whose station
        /// data could not be read, a preset that turns out to arm nothing, and any failure
        /// in between all resolve to the stock fit. An unarmed wingman is a worse outcome
        /// than an unfulfilled preference — it flies to the wing, reads as Winchester and
        /// turns straight round for home.
        /// </summary>
        public static Loadout Build(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            // A template is an explicit statement about every pylon, so it is answered
            // before any of the preset machinery below gets a say. A template that has since
            // been deleted falls through to whatever preset the choice also carries, which
            // for a purchase order made from the panel is Standard.
            if (choice.IsTemplate)
            {
                LoadoutTemplateRecord template = WingLoadoutTemplates.ById(choice.TemplateId);
                if (template != null) return BuildFromKeys(definition, template.MountKeys);
            }

            if (choice.IsStandard) return null;

            Profile profile = ProfileOf(definition);
            if (profile == null) return null;
            if (!profile.HasRoleData && profile.Cargo.Count == 0) return null;

            var weapons = new List<WeaponMount>(profile.Sets.Length);
            bool anyArmed = false;
            bool anyCargo = false;

            for (int i = 0; i < profile.Sets.Length; i++)
            {
                MountInfo pick = Select(profile, i, choice);
                weapons.Add(pick != null ? pick.Mount : null);

                if (pick == null) continue;
                if (pick.Cargo) anyCargo = true;
                else if (pick.Armed) anyArmed = true;
            }

            // A cargo fit is complete when it is carrying cargo, even with nothing armed;
            // every other preset has to have produced a weapon to be worth fitting.
            bool usable = choice.Preset == WingLoadoutPreset.Cargo ? anyCargo : anyArmed;
            if (!usable)
            {
                Plugin.Logger.LogInfo(
                    "[Loadout] " + SafeName(definition) + " has no stock option for " +
                    Label(choice.Preset) + "; using the standard fit");
                return null;
            }

            return new Loadout { weapons = weapons };
        }

        /// <summary>Whether a preset would arm at least one station on this airframe.</summary>
        private static bool CanFit(Profile profile, WingLoadoutPreset preset)
        {
            var choice = new WingLoadoutChoice(preset);
            for (int i = 0; i < profile.Sets.Length; i++)
            {
                MountInfo pick = Select(profile, i, choice);
                if (pick != null && pick.Armed) return true;
            }
            return false;
        }

        /// <summary>
        /// The best option on one hardpoint set for one preset.
        ///
        /// Balanced alternates the emphasis across the sets rather than averaging the two
        /// scores, because averaging picks the same compromise store on every station and
        /// produces an aircraft that is mediocre at both jobs instead of capable of each.
        /// </summary>
        private static MountInfo Select(Profile profile, int index, WingLoadoutChoice choice)
        {
            List<MountInfo> options = profile.Options[index];
            if (options == null || options.Count == 0) return null;

            if (choice.Preset == WingLoadoutPreset.Cargo)
            {
                MountInfo cargo = SelectCargo(options, choice.CargoKey);
                if (cargo != null) return cargo;

                // Not a cargo station: leave it defensive so a transport is not defenceless
                // when it happens to carry a countermeasure or self-defence store.
                return Best(options, 0.6f, 0f, 1f);
            }

            switch (choice.Preset)
            {
                case WingLoadoutPreset.AirToAir:
                    return Best(options, 1f, 0f, 0.35f);
                case WingLoadoutPreset.AirToGround:
                    return Best(options, 0f, 1f, 0.1f);
                default:
                    return (index % 2 == 0)
                        ? Best(options, 1f, 0.2f, 0.35f)
                        : Best(options, 0.2f, 1f, 0.1f);
            }
        }

        private static MountInfo SelectCargo(List<MountInfo> options, string key)
        {
            MountInfo first = null;
            for (int i = 0; i < options.Count; i++)
            {
                MountInfo option = options[i];
                if (!option.Cargo) continue;
                if (option.Key == key) return option;
                if (first == null) first = option;
            }
            return first;
        }

        /// <summary>Highest weighted stock effectiveness, longest reach breaking a tie.</summary>
        private static MountInfo Best(List<MountInfo> options, float air, float surface,
                                      float missile)
        {
            MountInfo best = null;
            float bestScore = 0f;

            for (int i = 0; i < options.Count; i++)
            {
                MountInfo option = options[i];
                if (option.Cargo) continue;

                float score = option.AntiAir * air +
                              option.AntiSurface * surface +
                              option.AntiMissile * missile;
                if (score <= 0f) continue;

                // Reach only separates equals. Letting it into the score outright would
                // fit the longest-ranged store on every station regardless of what it is
                // actually good against.
                if (best != null &&
                    (score < bestScore ||
                     (Mathf.Approximately(score, bestScore) && option.MaxRange <= best.MaxRange)))
                    continue;

                bestScore = score;
                best = option;
            }

            return best;
        }

        // ------------------------------------------------------------------ profiling

        private static Profile ProfileOf(AircraftDefinition definition)
        {
            if (definition == null) return null;
            if (profiles.TryGetValue(definition, out Profile cached)) return cached;

            Profile profile = null;
            try
            {
                profile = BuildProfile(definition);
            }
            catch (Exception e)
            {
                Fail("reading " + SafeName(definition) + "'s hardpoints failed: " + e.Message);
            }

            profiles[definition] = profile;
            return profile;
        }

        private static Profile BuildProfile(AircraftDefinition definition)
        {
            HardpointSet[] sets = HardpointSetsOf(definition);
            if (sets == null || sets.Length == 0) return null;

            var profile = new Profile
            {
                Sets = sets,
                Options = new List<MountInfo>[sets.Length],
            };

            for (int i = 0; i < sets.Length; i++)
            {
                var options = new List<MountInfo>();
                profile.Options[i] = options;

                HardpointSet set = sets[i];
                List<WeaponMount> available = set != null ? set.weaponOptions : null;
                if (available == null) continue;

                for (int j = 0; j < available.Count; j++)
                {
                    WeaponMount mount = available[j];

                    // A null entry is the game's own "nothing on this station" choice, and
                    // a disallowed one is event content or a store this game build has locked.
                    if (mount == null || mount.NotAllowed(includeEventContent: false)) continue;

                    MountInfo info = Describe(mount);
                    options.Add(info);

                    if (info.Armed)
                    {
                        profile.HasRoleData = true;
                        roleDataSeen = true;
                    }
                    if (info.Cargo && !HasCargoKey(profile, info.Key))
                        profile.Cargo.Add(new CargoOption(info.Key, info.Label));
                }
            }

            if (!profile.HasRoleData && profile.Cargo.Count == 0)
                NoteBlindProfile(definition);

            return profile;
        }

        /// <summary>
        /// Say once, in the log, that an airframe's stores could not be read.
        ///
        /// Not a hard failure: a profile with no readable role data simply offers the
        /// standard fit, which is what the aircraft would have launched with anyway. It is
        /// worth one line, because the alternative is a Loadout panel that silently shows a
        /// single preset and looks broken.
        /// </summary>
        private static void NoteBlindProfile(AircraftDefinition definition)
        {
            if (roleDataSeen || blindProfilesLogged) return;

            blindProfilesLogged = true;
            Plugin.Logger.LogInfo(
                "[Loadout] no stock weapon-role data could be read from " +
                SafeName(definition) + "'s hardpoints; that airframe offers the standard fit only.");
        }

        /// <summary>True once any airframe's stores have been read successfully.</summary>
        private static bool roleDataSeen;

        private static bool blindProfilesLogged;

        private static bool HasCargoKey(Profile profile, string key)
        {
            for (int i = 0; i < profile.Cargo.Count; i++)
            {
                if (profile.Cargo[i].Key == key) return true;
            }
            return false;
        }

        /// <summary>The airframe's weapon manager, read from the prefab the spawner uses.</summary>
        private static HardpointSet[] HardpointSetsOf(AircraftDefinition definition)
        {
            GameObject prefab = definition.unitPrefab;
            if (prefab == null) return null;

            // Non-generic lookup on purpose: the generic overload constrains its type
            // argument to Component, which would make this file fail to compile if a game
            // update ever made WeaponManager anything else.
            Component[] managers = prefab.GetComponentsInChildren(typeof(WeaponManager), true);
            if (managers == null) return null;

            for (int i = 0; i < managers.Length; i++)
            {
                object candidate = managers[i];
                if (candidate is WeaponManager manager && manager.hardpointSets != null)
                    return manager.hardpointSets;
            }

            return null;
        }

        // ----------------------------------------------------------- mount inspection

        /// <summary>Whether weapon stations are Unity components, checked once.</summary>
        private static readonly bool StationIsComponent =
            typeof(Component).IsAssignableFrom(typeof(WeaponStation));

        private static readonly List<WeaponStation> stationScratch = new List<WeaponStation>();

        private static MountInfo Describe(WeaponMount mount)
        {
            var info = new MountInfo
            {
                Mount = mount,
                Key = mount.jsonKey,
                Label = NameOf(mount),

                // Straight off the mount, which the station walk below cannot improve on.
                Ammo = mount.ammo,
                Mass = mount.mass,
                Cargo = mount.Cargo || mount.Troops,
            };

            // The mount's own weapon info is the direct route to role data and covers the
            // ordinary single-weapon store. The station walk after it is what handles a
            // mount that carries several different weapons, and a build where this member
            // has moved.
            WeaponInfo direct = mount.info;
            if (direct != null) Absorb(info, direct);

            List<WeaponStation> stations = StationsOf(mount);
            for (int i = 0; i < stations.Count; i++)
            {
                WeaponStation station = stations[i];
                if (station == null) continue;

                if (station.Cargo)
                {
                    info.Cargo = true;
                    continue;
                }

                WeaponInfo weapon = station.WeaponInfo;
                if (weapon == null) continue;

                Absorb(info, weapon);
            }

            return info;
        }

        /// <summary>Take the best figures this weapon offers into the mount's summary.</summary>
        private static void Absorb(MountInfo info, WeaponInfo weapon)
        {
            RoleIdentity role = weapon.effectiveness;
            info.AntiAir = Mathf.Max(info.AntiAir, role.antiAir);
            info.AntiSurface = Mathf.Max(info.AntiSurface, role.antiSurface);
            info.AntiMissile = Mathf.Max(info.AntiMissile, role.antiMissile);
            info.MaxRange = Mathf.Max(info.MaxRange, weapon.targetRequirements.maxRange);
            if (weapon.cargo || weapon.troops) info.Cargo = true;
        }

        /// <summary>
        /// The stations a mount prefab carries.
        ///
        /// The component search is the normal path; the reflected scan behind it covers a
        /// mount that references its station through a field instead of parenting it.
        /// </summary>
        private static List<WeaponStation> StationsOf(WeaponMount mount)
        {
            stationScratch.Clear();

            // Boxed to object first: a direct cast expression between WeaponMount and
            // Component would only compile while the two are related types.
            object boxed = mount;
            var component = boxed as Component;

            if (StationIsComponent && component != null)
            {
                Component[] found = component.GetComponentsInChildren(typeof(WeaponStation), true);
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        object candidate = found[i];
                        if (candidate is WeaponStation station) stationScratch.Add(station);
                    }
                }
            }

            if (stationScratch.Count == 0) ReflectStations(boxed, stationScratch);
            return stationScratch;
        }

        private static readonly Dictionary<Type, MemberInfo[]> memberCache =
            new Dictionary<Type, MemberInfo[]>();

        private static void ReflectStations(object mount, List<WeaponStation> into)
        {
            if (mount == null) return;

            Type type = mount.GetType();
            if (!memberCache.TryGetValue(type, out MemberInfo[] members))
            {
                var found = new List<MemberInfo>();
                const BindingFlags flags =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (Interesting(field.FieldType)) found.Add(field);
                }
                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (property.GetIndexParameters().Length == 0 &&
                        property.CanRead && Interesting(property.PropertyType))
                        found.Add(property);
                }

                members = found.ToArray();
                memberCache[type] = members;
            }

            for (int i = 0; i < members.Length; i++)
            {
                object value = null;
                try
                {
                    value = members[i] is FieldInfo field
                        ? field.GetValue(mount)
                        : ((PropertyInfo)members[i]).GetValue(mount, null);
                }
                catch
                {
                    // A property that throws on a prefab tells us nothing; skip it.
                }

                if (value is WeaponStation station) into.Add(station);
                else if (value is IEnumerable<WeaponStation> many)
                {
                    foreach (WeaponStation each in many)
                    {
                        if (each != null) into.Add(each);
                    }
                }
            }

        }

        private static bool Interesting(Type type) =>
            typeof(WeaponStation).IsAssignableFrom(type) ||
            typeof(IEnumerable<WeaponStation>).IsAssignableFrom(type);

        private static void Fail(string reason)
        {
            UnavailableReason = reason;

            if (!probeFailed)
            {
                probeFailed = true;
                Plugin.Logger.LogWarning(
                    "Loadout presets unavailable (" + reason +
                    "). Requisitions will use each airframe's standard fit.");
                return;
            }

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogWarning("[Loadout] " + reason);
        }

        private static bool probeFailed;

        private static string NameOf(WeaponMount mount)
        {
            string name = mount.mountName;
            if (string.IsNullOrEmpty(name)) name = mount.name;
            return string.IsNullOrEmpty(name) ? "STORE" : name;
        }

        private static string SafeName(AircraftDefinition definition) =>
            definition != null && !string.IsNullOrEmpty(definition.unitName)
                ? definition.unitName
                : "airframe";
    }
}
