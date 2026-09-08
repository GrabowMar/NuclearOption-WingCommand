using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission;
using UnityEngine;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>Builds pylon options and loadouts from native hardpoint weaponOptions and effectiveness
    /// data. Reflects private mount-to-station links once per mount; unreadable profiles fall back to the
    /// stock fit with diagnostics.</summary>
    internal static class WingLoadoutCatalog
    {
        /// <summary>A hardpoint's native store option and role data.</summary>
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

            /// <summary>Mount ammunition count; zero when not countable.</summary>
            public int Ammo;

            /// <summary>Loaded store mass for fitted-weight display.</summary>
            public float Mass;

            public bool Armed => AntiAir > 0f || AntiSurface > 0f || AntiMissile > 0f;
        }

        /// <summary>Editor-facing store data without exposing WeaponMount prefab references.</summary>
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

            /// <summary>Two-letter role label for the store table.</summary>
            public string RoleTag =>
                Cargo ? "CGO"
                : AntiAir <= 0f && AntiSurface <= 0f ? ""
                : AntiAir > AntiSurface * 1.5f ? "A-A"
                : AntiSurface > AntiAir * 1.5f ? "A-G"
                : "MLT";
        }

        /// <summary>Cached hardpoint data for one airframe.</summary>
        private sealed class Profile
        {
            public HardpointSet[] Sets;
            public List<MountInfo>[] Options;
            public bool HasRoleData;

            /// <summary>Whether any readable store is cargo; cargo-only profiles still count as
            /// successfully read.</summary>
            public bool HasCargo;
        }

        private static readonly Dictionary<AircraftDefinition, Profile> profiles =
            new Dictionary<AircraftDefinition, Profile>();

        /// <summary>Unavailable only after probing fails without any successful role data. Individual
        /// unreadable airframes fall back to their standard fit.</summary>
        public static bool Available => roleDataSeen || !probeFailed;

        /// <summary>Clear prefab caches at mission end because assets may reload.</summary>
        public static void Reset()
        {
            profiles.Clear();
            blindProfilesLogged = false;
        }

        // Loadout queries.

        /// <summary>Template name or STANDARD for compact UI labels.</summary>
        public static string Label(WingLoadoutChoice choice)
        {
            // Centralise fit labels across shop, roster, and reserve displays.
            if (!choice.IsTemplate) return "STANDARD";

            return AvTheme.Truncate(WingLoadoutTemplates.NameOf(choice.TemplateId), 20)
                          .ToUpperInvariant();
        }

        // Pylon editing.

        /// <summary>Declared hardpoint count, or zero if unreadable.</summary>
        public static int PylonCount(AircraftDefinition definition)
        {
            Profile profile = ProfileOf(definition);
            return profile?.Sets?.Length ?? 0;
        }

        /// <summary>Use native hardpoint names; label symmetric pairs once with SymmetryName.</summary>
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

        /// <summary>Whether the pylon mirrors its predecessor and should be edited through that partner's
        /// row.</summary>
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

        /// <summary>List valid stores with the empty pylon first, allowing deliberate clean
        /// stations.</summary>
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

        /// <summary>Resolve a pylon's store key, falling back to the empty option.</summary>
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

        /// <summary>Ask native hardpoint-exclusion rules whether this pylon is blocked by the current
        /// fit.</summary>
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
                // Fail closed on malformed exclusion rules; do not offer a station whose fit cannot be
                // validated.
                Fail("checking whether " + SafeName(definition) + " pylon " + (index + 1) +
                     " is blocked failed: " + e.Message);
                return true;
            }
        }

        /// <summary>Build a spawnable template from store keys. Unknown keys leave their pylons empty;
        /// honour deliberate all-empty fits.</summary>
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
                Plugin.LogVerbose(
                    "[Loadout] a saved template for " + SafeName(definition) +
                    " names stores this build does not have; those pylons launch empty.");

            if (blocked > 0)
                Plugin.Logger.LogWarning(
                    "[Loadout] removed " + blocked + " blocked " +
                    (blocked == 1 ? "store" : "stores") + " from " +
                    SafeName(definition) + " before spawning.");

            return new Loadout { weapons = weapons };
        }

        /// <summary>Apply native exclusion rules authoritatively before spawn, including stale or
        /// hand-edited templates. Repeat clearing until the remaining fit is stable.</summary>
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
                        // Remove the store whose exclusion check throws; preserve the rest of the fit.
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

        /// <summary>Fill reusable editor scratch for exclusion checks. Never spawn with this shared
        /// mutable Loadout; each aircraft must own its container.</summary>
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

        // Loadout construction.

        /// <summary>Build a choice using the live player default, then game-start preset for Standard.
        /// Return null for unavailable presets or deleted templates so native spawning chooses a usable
        /// fallback fit.</summary>
        public static Loadout Build(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            if (choice.HasSnapshot) return GuardNativeFallback(definition, BuildFromKeys(definition, choice.FittedKeys));
            if (!choice.IsTemplate) return ClonePlayerDefault(definition);

            LoadoutTemplateRecord template = WingLoadoutTemplates.ById(choice.TemplateId);
            return template != null
                ? GuardNativeFallback(definition, BuildFromKeys(definition, template.MountKeys))
                : null;
        }

        /// <summary>Copy the live player default, falling back to the airframe's game-start
        /// preset.</summary>
        internal static Loadout ClonePlayerDefault(AircraftDefinition definition)
        {
            if (definition == null) return null;
            if (GameManager.aircraftCustomization != null &&
                GameManager.aircraftCustomization.TryGetValue(definition, out AircraftCustomization custom) &&
                custom?.loadout?.weapons != null && custom.loadout.weapons.Count > 0)
                return CloneLoadout(custom.loadout);
            return CloneLoadout(GameStartLoadout(definition));
        }

        /// <summary>Keep one entry per hardpoint for empty fits; a missing or empty list triggers native
        /// loadouts[1] fallback and can restore removed tanks.</summary>
        private static Loadout GuardNativeFallback(AircraftDefinition definition, Loadout loadout)
        {
            if (loadout?.weapons != null &&
                !RecoverySettlementPolicy.NativeLoadoutReplaces(loadout.weapons.Count))
                return loadout;
            return EmptyStations(definition) ?? loadout;
        }

        private static Loadout EmptyStations(AircraftDefinition definition)
        {
            int count = PylonCount(definition);
            if (count <= 0) return null;
            var weapons = new List<WeaponMount>(count);
            for (int i = 0; i < count; i++) weapons.Add(null);
            return new Loadout { weapons = weapons };
        }

        /// <summary>Snapshot the actual fitted stores, including native Standard.</summary>
        internal static WingLoadoutChoice SnapshotFit(Aircraft aircraft, WingLoadoutChoice choice)
        {
            List<WeaponMount> weapons = aircraft?.Networkloadout?.weapons;
            // Hangar registration may precede loadout installation; retry the snapshot on the book's
            // next read.
            if (weapons == null) return choice;
            var keys = new List<string>(weapons.Count);
            for (int i = 0; i < weapons.Count; i++) keys.Add(StoreKey(weapons[i]));
            return choice.Snapshot(keys);
        }

        /// <summary>Read the native player-start preset at index 1, not index 0.</summary>
        private static Loadout GameStartLoadout(AircraftDefinition definition)
        {
            if (definition == null) return null;

            List<Loadout> loadouts = definition.aircraftParameters?.loadouts;
            if (loadouts == null || loadouts.Count == 0) return null;

            // Match native first-time defaults; use index 0 only for incomplete single-entry workshop
            // presets.
            return loadouts[loadouts.Count > 1 ? 1 : 0];
        }

        /// <summary>Copy each aircraft's mutable Loadout list; immutable WeaponMount assets may be
        /// shared.</summary>
        private static Loadout CloneLoadout(Loadout source)
        {
            if (source?.weapons == null) return null;
            return new Loadout { weapons = new List<WeaponMount>(source.weapons) };
        }

        // Airframe profiling.

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

                    // Null means an empty station; reject locked or event-only stores.
                    if (mount == null || mount.NotAllowed(includeEventContent: false)) continue;

                    MountInfo info;
                    try
                    {
                        info = Describe(mount);
                    }
                    catch (Exception e)
                    {
                        // Skip malformed workshop stores while retaining the rest of the hardpoint
                        // options.
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

        /// <summary>Log unreadable role data once per airframe and offer its standard fit.</summary>
        private static void NoteBlindProfile(AircraftDefinition definition)
        {
            if (roleDataSeen || blindProfilesLogged) return;

            blindProfilesLogged = true;
            Plugin.LogVerbose(
                "[Loadout] no stock weapon-role data could be read from " +
                SafeName(definition) + "'s hardpoints; that airframe offers the standard fit only.");
        }

        /// <summary>Whether any airframe has yielded readable store data.</summary>
        private static bool roleDataSeen;

        private static bool blindProfilesLogged;

        /// <summary>Read hardpoint sets from the spawning prefab's weapon manager.</summary>
        private static HardpointSet[] HardpointSetsOf(AircraftDefinition definition)
        {
            GameObject prefab = definition.unitPrefab;
            if (prefab == null) return null;

            // Use the non-generic lookup to avoid a compile-time Component constraint on WeaponManager.
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

        // Mount inspection.

        /// <summary>Cached check for WeaponStation deriving from Component.</summary>
        private static readonly bool StationIsComponent =
            typeof(Component).IsAssignableFrom(typeof(WeaponStation));

        private static readonly List<WeaponStation> stationScratch = new List<WeaponStation>();

        private static MountInfo Describe(WeaponMount mount)
        {
            var info = new MountInfo
            {
                Mount = mount,
                // Prefer jsonKey; use the asset name for older workshop stores without one.
                Key = StoreKey(mount),
                Label = NameOf(mount),

                // Read ammunition directly from the mount.
                Ammo = mount.ammo,
                Mass = mount.mass,
                Cargo = mount.Cargo || mount.Troops,
            };

            // Read ordinary weapon data directly; inspect stations for multi-weapon mounts and fallback
            // metadata.
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

        /// <summary>Merge this weapon's best role figures into the mount summary.</summary>
        private static void Absorb(MountInfo info, WeaponInfo weapon)
        {
            RoleIdentity role = weapon.effectiveness;
            info.AntiAir = Mathf.Max(info.AntiAir, role.antiAir);
            info.AntiSurface = Mathf.Max(info.AntiSurface, role.antiSurface);
            info.AntiMissile = Mathf.Max(info.AntiMissile, role.antiMissile);
            info.MaxRange = Mathf.Max(info.MaxRange, weapon.targetRequirements.maxRange);
            if (weapon.cargo || weapon.troops) info.Cargo = true;
        }

        /// <summary>Find child station components, then reflect fields for mounts that reference stations
        /// elsewhere.</summary>
        private static List<WeaponStation> StationsOf(WeaponMount mount)
        {
            stationScratch.Clear();

            // Cast through object so compilation does not require WeaponMount and Component to be
            // related.
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
                    // Ignore prefab properties that throw during inspection.
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
