using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission;
using UnityEngine;

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

            /// <summary>Loaded store mass for fitted-weight display (per pylon).</summary>
            public float Mass;

            /// <summary>The game's rules (R5): event-only, nuclear, strategic, not from a ship (mount.info, as WeaponChecker reads);
            /// what it is for the summary; the asset name mission restrictions use.</summary>
            public bool Disabled, EventContent, Nuclear, Strategic, ShipRearm, Jammer, Bomb;
            public string AssetName;

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
            /// <summary>This build has the store (false: a saved key it cannot resolve).</summary>
            public readonly bool Known;
            public readonly bool Disabled, EventContent, Nuclear, Strategic, ShipRearm;
            public readonly StoreKind Kind;
            public readonly string AssetName;

            public StoreOption(string key, string label, int ammo, float mass, float antiAir, float antiSurface, bool cargo,
                bool known = true, bool eventContent = false, bool nuclear = false, bool strategic = false, bool shipRearm = false,
                StoreKind kind = StoreKind.None, string assetName = null, bool disabled = false)
            {
                Key = key;
                Label = label;
                Ammo = ammo;
                Mass = mass;
                AntiAir = antiAir;
                AntiSurface = antiSurface;
                Cargo = cargo;
                Known = known;
                EventContent = eventContent;
                Nuclear = nuclear;
                Strategic = strategic;
                ShipRearm = shipRearm;
                Kind = kind;
                AssetName = assetName;
                Disabled = disabled;
            }

            public bool IsEmpty => string.IsNullOrEmpty(Key);

            /// <summary>The set's store for the fit summary.</summary>
            public StoreFacts Facts => new StoreFacts { HasKey = !IsEmpty, Known = Known && !IsEmpty, Kind = Kind, Mass = Mass, Ammo = Ammo };

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
            /// <summary>The stations as LOADOUT shows them (R5).</summary>
            public StationLayout Layout;
            public List<MountInfo>[] Options;
            public bool HasRoleData;

            /// <summary>Whether any readable store is cargo; cargo-only profiles still count as
            /// successfully read.</summary>
            public bool HasCargo;
        }

        private static readonly Dictionary<AircraftDefinition, Profile> profiles =
            new Dictionary<AircraftDefinition, Profile>();

        /// <summary>Clear prefab caches at mission end because assets may reload.</summary>
        public static void Reset()
        {
            profiles.Clear();
            blindProfilesLogged = false;
        }

        /// <summary>Declared hardpoint count, or zero if unreadable.</summary>
        public static int PylonCount(AircraftDefinition definition)
        {
            Profile profile = ProfileOf(definition);
            return profile?.Sets?.Length ?? 0;
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

            // Switched-off stores and event content stay out of this list (WingSquad's picker; R5's LOADOUT asks StoreRules).
            for (int i = 0; i < options.Count; i++)
                if (!options[i].Disabled && !options[i].EventContent) into.Add(Project(options[i]));
        }

        /// <summary>Every store the set can carry (event content included; the page asks StoreRules), without an empty entry: on
        /// LOADOUT only CLEAR empties a station.</summary>
        public static void StoresFor(AircraftDefinition definition, int set, List<StoreOption> into)
        {
            into.Clear();
            Profile profile = ProfileOf(definition);
            if (profile?.Options == null || set < 0 || set >= profile.Options.Length || profile.Options[set] == null) return;
            foreach (MountInfo info in profile.Options[set]) into.Add(Project(info));
        }

        /// <summary>The airframe's stations, or null when its hardpoints cannot be read.</summary>
        public static StationLayout Layout(AircraftDefinition definition) => ProfileOf(definition)?.Layout;

        /// <summary>What the mission allows now for <paramref name="rank"/> (all replicated to clients).</summary>
        public static MissionFacts Mission(int rank)
        {
            MissionManager m = NetworkSceneSingleton<MissionManager>.i;
            return new MissionFacts
            {
                EventContent = MissionManager.AllowEventContent, TacticalOpen = MissionManager.AllowTactical(),
                StrategicOpen = MissionManager.AllowStrategic(), TacticalMinRank = m != null ? m.tacticalMinRank : 0f,
                StrategicMinRank = m != null ? m.strategicMinRank : 0f, Rank = rank,
            };
        }

        /// <summary>A store option as StoreRules reads it (restricted by the faction's list of asset names; blocked is the station's).</summary>
        public static MountFacts FactsOf(in StoreOption o, int pylons, FactionHQ hq) => new MountFacts
        {
            Known = o.Known && !o.IsEmpty, OnStation = o.Known, Disabled = o.Disabled, EventContent = o.EventContent,
            Restricted = hq != null && hq.restrictedWeapons != null && o.AssetName != null && hq.restrictedWeapons.Contains(o.AssetName),
            Nuclear = o.Nuclear, Strategic = o.Strategic, ShipRearm = o.ShipRearm, Pylons = pylons, Ammo = o.Ammo,
        };

        /// <summary>The store keys an aircraft carries (Members' template check).</summary>
        public static void KeysOf(Loadout loadout, List<string> into)
        {
            into.Clear();
            if (loadout?.weapons == null) return;
            foreach (WeaponMount m in loadout.weapons) into.Add(StoreKey(m));
        }

        /// <summary>Resolve a pylon's store key, falling back to the empty option.</summary>
        public static StoreOption StoreOn(AircraftDefinition definition, int index, string key)
        {
            var empty = new StoreOption(null, "— EMPTY —", 0, 0f, 0f, 0f, false);
            if (string.IsNullOrEmpty(key)) return empty;

            MountInfo info = Lookup(ProfileOf(definition), index, key);
            return info != null ? Project(info) : new StoreOption(key, "UNKNOWN STORE", 0, 0f, 0f, 0f, false, known: false);
        }

        private static StoreOption Project(MountInfo info) =>
            new StoreOption(info.Key, info.Label, info.Ammo, info.Mass, info.AntiAir, info.AntiSurface, info.Cargo, true, info.EventContent,
                info.Nuclear, info.Strategic, info.ShipRearm, KindOf(info), info.AssetName, info.Disabled);

        /// <summary>What a store is for the summary and the role (cargo, ECM, missile defence, bombs, then its better role).</summary>
        private static StoreKind KindOf(MountInfo i)
        {
            if (i.Cargo) return StoreKind.Cargo;
            if (i.Jammer) return StoreKind.Ecm;
            if (i.AntiMissile > Mathf.Max(i.AntiAir, i.AntiSurface)) return StoreKind.MissileDefence;
            if (i.Bomb) return StoreKind.Bomb;
            if (i.AntiAir > 0f && i.AntiAir >= i.AntiSurface) return StoreKind.AirToAir;
            if (i.AntiSurface > 0f) return StoreKind.AirToGround;
            return StoreKind.Other;
        }

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

        /// <summary>A chosen fit (YOUR LOADOUT or a template) passes the game's own mount checks before it spawns (review R4a:
        /// the player's selection menu vets a fit, a requisition must too): event content and mission-restricted stores,
        /// ship-rearm stores off a carrier, stores the pylon does not take or a neighbour blocks, and nuclear stores against the
        /// mission's permission, the player's rank and the field's warheads. A failing pylon launches empty; returns how many.</summary>
        public static int Vet(Loadout loadout, AircraftDefinition definition, Airbase field, FactionHQ hq)
        {
            Aircraft template = definition != null && definition.unitPrefab != null ? definition.unitPrefab.GetComponent<Aircraft>() : null;
            WeaponManager manager = template != null ? template.weaponManager : null;
            if (loadout?.weapons == null || manager == null || manager.hardpointSets == null) return 0;
            GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player);
            // R5: warheads counted over the whole fit in station order, as the game's own menu counts them (its per-store check
            // lets two stores each pass against the same warheads, and RemoveWarheads runs past zero).
            int warheads = field != null ? field.GetWarheads() : int.MaxValue;
            int stripped = 0;
            for (int i = 0; i < loadout.weapons.Count && i < manager.hardpointSets.Length; i++)
            {
                WeaponMount mount = loadout.weapons[i];
                HardpointSet set = manager.hardpointSets[i];
                if (mount == null || set == null) continue;
                int need = mount.info != null && mount.info.nuclear ? (set.hardpoints != null ? set.hardpoints.Count : 0) * mount.ammo : 0;
                if (WeaponChecker.MountAllowedHQ(mount, hq) && WeaponChecker.MountAllowedAirbase(mount, field)
                    && WeaponChecker.MountAllowedHardpoint(mount, set) && WeaponChecker.MountAllowedConflict(set, loadout)
                    && WeaponChecker.MountAllowedNuclear(mount, set, field, player, hq) && need <= warheads)
                {
                    warheads -= need;
                    continue;
                }
                loadout.weapons[i] = null;
                stripped++;
            }
            if (stripped > 0)
                Plugin.LogVerbose("[Loadout] " + SafeName(definition) + ": " + stripped + " pylon(s) launch empty (not allowed here)");
            return stripped;
        }

        /// <summary>A fresh loadout for a CallSpec fit: AUTO (null) is null — the game arms it; YOUR LOADOUT is a copy of the player's
        /// own; a template is built from its keys (null when it no longer exists, so the game arms it instead).</summary>
        public static Loadout Build(AircraftDefinition definition, string fit)
        {
            if (fit == null) return null;
            if (fit == CallSpec.YourLoadout) return ClonePlayerDefault(definition);
            LoadoutTemplateRecord template = WingLoadoutTemplates.ById(fit);
            return template != null ? GuardNativeFallback(definition, BuildFromKeys(definition, template.MountKeys)) : null;
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
                Layout = LayoutOf(sets),
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

                    // Null means an empty station. Switched-off stores and event content are kept and marked (R5: a template holding one
                    // reads SWITCHED OFF or EVENT ONLY, not NOT INSTALLED; the launch's Vet strips them; OptionsFor leaves them out).
                    if (mount == null) continue;

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

                    info.Disabled = !mount.IsAllowed(true);
                    info.EventContent = !info.Disabled && !mount.IsAllowed(false);
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

        /// <summary>The stations from the sets' names, pair names, mirroring, pylons and precludes (R5).</summary>
        private static StationLayout LayoutOf(HardpointSet[] sets)
        {
            int n = sets.Length;
            var names = new string[n];
            var pairs = new string[n];
            var withPrev = new bool[n];
            var pylons = new int[n];
            var precludes = new int[n][];
            for (int i = 0; i < n; i++)
            {
                HardpointSet set = sets[i];
                names[i] = set?.name;
                pairs[i] = set?.SymmetryName;
                withPrev[i] = set != null && set.SymmetryWithPrev;
                pylons[i] = set?.hardpoints != null ? set.hardpoints.Count : 0;
                List<byte> p = set?.precludingHardpointSets;
                precludes[i] = new int[p != null ? p.Count : 0];
                for (int j = 0; j < precludes[i].Length; j++) precludes[i][j] = p[j];
            }
            return new StationLayout(names, pairs, withPrev, pylons, precludes);
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
                AssetName = mount.name,
            };
            // The game's nuclear and carrier rules read mount.info only (WeaponChecker); so do these flags.
            if (mount.info != null)
            {
                info.Nuclear = mount.info.nuclear;
                info.Strategic = mount.info.strategic;
                info.ShipRearm = mount.info.rearmShip;
            }

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
            if (weapon.jammer) info.Jammer = true;
            if (weapon.bomb || weapon.glideBomb) info.Bomb = true;
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
