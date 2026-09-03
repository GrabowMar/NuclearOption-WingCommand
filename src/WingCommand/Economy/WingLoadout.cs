using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission;
using UnityEngine;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>
    /// One aircraft's loadout decision: a saved per-pylon template, or the airframe's own
    /// standard fit.
    ///
    /// This was once a three-case choice — template, role preset, or standard — but the
    /// per-pylon editor replaced the presets outright, and a preset dimension that could
    /// only ever hold Standard was carrying the whole role-scoring machinery behind it.
    ///
    /// It stays a struct rather than a bare string because every part of the mod that moves
    /// a loadout about — the shop, delivery, the reserve, recovery, takeover — carries it by
    /// value and never inspects it. Adding a case back reaches all of them without any of
    /// them changing.
    /// </summary>
    internal readonly struct WingLoadoutChoice
    {
        /// <summary>
        /// The saved template this fit came from, or null for the airframe's own fit.
        ///
        /// An id rather than the record itself: an in-flight aircraft, a reserve slot and a
        /// purchase order may all outlive the template they were fitted from, and none of
        /// them should keep it alive or follow it through a rename into something else.
        /// </summary>
        public readonly string TemplateId;

        public WingLoadoutChoice(string templateId = null)
        {
            TemplateId = templateId;
        }

        public static WingLoadoutChoice Standard => new WingLoadoutChoice(null);

        /// <summary>Fit from a saved template, or pass null to go back to the stock fit.</summary>
        public WingLoadoutChoice WithTemplate(string templateId) =>
            new WingLoadoutChoice(templateId);

        public bool IsTemplate => !string.IsNullOrEmpty(TemplateId);

        /// <summary>True when nothing has been chosen and the game fits its own.</summary>
        public bool IsStandard => !IsTemplate;
    }

    /// <summary>
    /// Reads the airframe's own weapon-station data, so the pylon editor can offer real
    /// stores and a saved template can be turned into a <c>Loadout</c> the spawner accepts.
    ///
    /// Nothing here is a weapon catalogue of this mod's own. Every option comes from
    /// <c>WeaponManager.hardpointSets[i].weaponOptions</c> — the same list the game's
    /// aircraft selection menu fills its per-hardpoint dropdowns from — and every option is
    /// described by the same <c>WeaponInfo.effectiveness</c> figures <c>WingWeapons</c>
    /// already uses to pick a station in flight. A template is therefore a choice among the
    /// airframe's stock options, not a list of weapons this mod believes it ought to carry.
    ///
    /// The one thing that cannot be reached through a compiled reference is the route from
    /// a <c>WeaponMount</c> prefab to the <c>WeaponStation</c> it will become, because that
    /// member is private. It is resolved reflectively once per mount, in the same spirit as
    /// <see cref="GameAccess"/>: if a game update moves it, <see cref="Available"/> goes
    /// false, only the stock fit is offered, and the panel says why rather than silently
    /// fitting the wrong weapons.
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

            /// <summary>
            /// True when at least one readable store is a cargo pod. Only the "could this
            /// airframe's stores be read at all" check needs it — a transport whose sole
            /// legible stores are cargo is not a blind profile, and must not be logged as one.
            /// </summary>
            public bool HasCargo;
        }

        private static readonly Dictionary<AircraftDefinition, Profile> profiles =
            new Dictionary<AircraftDefinition, Profile>();

        /// <summary>
        /// False only when nothing has ever been read successfully.
        ///
        /// One airframe throwing is that airframe's problem — it offers the standard fit and
        /// the panel says so for that aircraft. The feature is only reported as unavailable
        /// when no airframe anywhere has yielded stock role data, which is what a game update
        /// moving the members underneath this looks like.
        /// </summary>
        public static bool Available => roleDataSeen || !probeFailed;

        /// <summary>Drop cached prefab data when a mission ends; prefabs may be reloaded.</summary>
        public static void Reset()
        {
            profiles.Clear();
            blindProfilesLogged = false;
        }

        // ------------------------------------------------------------------- queries

        /// <summary>Short panel label for a choice: the template's name, or <c>STANDARD</c>.</summary>
        public static string Label(WingLoadoutChoice choice)
        {
            // Every readout on the panel — the shop's fit line, the wing tab's carrying
            // column, the reserve — comes through here, so naming the template in one place
            // names it everywhere.
            if (!choice.IsTemplate) return "STANDARD";

            return AvTheme.Truncate(WingLoadoutTemplates.NameOf(choice.TemplateId), 20)
                          .ToUpperInvariant();
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
                // Fail closed. Showing this station as usable would let a malformed modded
                // exclusion rule create a fit we cannot prove the airframe accepts.
                Fail("checking whether " + SafeName(definition) + " pylon " + (index + 1) +
                     " is blocked failed: " + e.Message);
                return true;
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

            int blocked = ClearBlockedMounts(definition, profile, weapons);

            if (anyUnknown)
                Plugin.Logger.LogInfo(
                    "[Loadout] a saved template for " + SafeName(definition) +
                    " names stores this build does not have; those pylons launch empty.");

            if (blocked > 0)
                Plugin.Logger.LogWarning(
                    "[Loadout] removed " + blocked + " blocked " +
                    (blocked == 1 ? "store" : "stores") + " from " +
                    SafeName(definition) + " before spawning.");

            return new Loadout { weapons = weapons };
        }

        /// <summary>
        /// Apply the airframe's own hardpoint-exclusion rules to a completed template.
        ///
        /// The editor performs the same checks for presentation, but saved config can be
        /// stale or hand-edited and a newly selected store can block an already-filled
        /// station. The spawn path is therefore authoritative and removes every store the
        /// game reports as blocked. Repeating reaches a stable result when clearing one
        /// station changes another station's answer.
        /// </summary>
        private static int ClearBlockedMounts(AircraftDefinition definition, Profile profile,
                                              List<WeaponMount> weapons)
        {
            if (profile?.Sets == null || weapons == null) return 0;

            var loadout = new Loadout { weapons = weapons };
            int removed = 0;
            bool changed;

            do
            {
                changed = false;
                for (int i = 0; i < profile.Sets.Length && i < weapons.Count; i++)
                {
                    if (weapons[i] == null) continue;

                    HardpointSet set = profile.Sets[i];
                    if (set == null) continue;

                    bool blocked;
                    try
                    {
                        blocked = set.BlockedByOtherHardpoint(loadout);
                    }
                    catch (Exception e)
                    {
                        // A store whose exclusion rules throw is not safe to pass to the
                        // spawner. Remove only that store and leave the rest of the fit.
                        blocked = true;
                        Fail("validating " + SafeName(definition) + " pylon " + (i + 1) +
                             " failed: " + e.Message);
                    }

                    if (!blocked) continue;
                    weapons[i] = null;
                    removed++;
                    changed = true;
                }
            }
            while (changed);

            return removed;
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

        // --------------------------------------------------------------------- build

        /// <summary>
        /// Turn a choice into a spawnable loadout, or null to let the game fit the
        /// airframe's own standard equipment.
        ///
        /// Null is returned for more than a stock choice: an airframe whose station data
        /// could not be read, and a template that has since been deleted, both resolve to
        /// the stock fit. An unarmed wingman is a worse outcome than an unfulfilled
        /// preference — it flies to the wing, reads as Winchester and turns straight round
        /// for home.
        /// </summary>
        public static Loadout Build(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            if (!choice.IsTemplate) return null;

            LoadoutTemplateRecord template = WingLoadoutTemplates.ById(choice.TemplateId);
            return template != null ? BuildFromKeys(definition, template.MountKeys) : null;
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

                    MountInfo info;
                    try
                    {
                        info = Describe(mount);
                    }
                    catch (Exception e)
                    {
                        // One malformed workshop store must not hide every other store on
                        // the aircraft. Keep profiling the rest of the hardpoint.
                        Plugin.Logger.LogWarning(
                            "[Loadout] skipped unreadable store " + NameOf(mount) + " on " +
                            SafeName(definition) + ": " + e.Message);
                        continue;
                    }

                    if (string.IsNullOrEmpty(info.Key))
                    {
                        Plugin.Logger.LogWarning(
                            "[Loadout] skipped store with no persistent identity on " +
                            SafeName(definition) + ": " + NameOf(mount));
                        continue;
                    }
                    options.Add(info);

                    if (info.Armed)
                    {
                        profile.HasRoleData = true;
                        roleDataSeen = true;
                    }
                    if (info.Cargo) profile.HasCargo = true;
                }
            }

            if (!profile.HasRoleData && !profile.HasCargo)
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
                // Workshop mounts normally publish jsonKey exactly like built-in mounts.
                // A few older mods omit it; the ScriptableObject asset name is stable
                // enough to let those stores participate in saved templates as well.
                Key = StoreKey(mount),
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
            if (!probeFailed)
            {
                probeFailed = true;
                Plugin.Logger.LogWarning(
                    "Loadout presets unavailable (" + reason +
                    "). Requisitions will use each airframe's standard fit.");
                return;
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogWarning("[Loadout] " + reason);
        }

        private static bool probeFailed;

        private static string NameOf(WeaponMount mount)
        {
            string name = mount.mountName;
            if (string.IsNullOrEmpty(name)) name = mount.name;
            return string.IsNullOrEmpty(name) ? "STORE" : name;
        }

        private const string AssetNameKeyPrefix = "@asset:";

        private static string StoreKey(WeaponMount mount)
        {
            if (mount == null) return null;
            if (!string.IsNullOrEmpty(mount.jsonKey)) return mount.jsonKey;

            string assetName = mount.name;
            if (string.IsNullOrEmpty(assetName)) assetName = mount.mountName;
            return string.IsNullOrEmpty(assetName) ? null : AssetNameKeyPrefix + assetName;
        }

        private static string SafeName(AircraftDefinition definition) =>
            definition != null && !string.IsNullOrEmpty(definition.unitName)
                ? definition.unitName
                : "airframe";
    }
}
