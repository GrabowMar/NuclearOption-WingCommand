using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.Networking;
using UnityEngine;
using HarmonyLib;

#pragma warning disable IDE0051 // Harmony invokes the explicitly registered survivor hooks.

namespace WingCommand.Interop
{
    /// <summary>Companion-facing pilot presentation and host-owned adversary flights.
    /// Campaign rules, rewards and replication belong to the requesting companion.</summary>
    public static class WingSquad
    {
        public static int ApiVersion => 1;
        public const int MaxAircraft = 24;
        public const int MaxWingSize = 4;
        private static readonly Dictionary<Aircraft, Aircraft> owned = new Dictionary<Aircraft, Aircraft>();
        private static readonly List<Aircraft> stale = new List<Aircraft>(MaxAircraft);
        private static readonly HashSet<string> portraitKeys = new HashSet<string>();
        private sealed class Survivor
        {
            internal PilotDismounted Native;
            internal int Outcome;
        }
        private static readonly Dictionary<PersistentID, Survivor> survivors = new Dictionary<PersistentID, Survivor>();

        /// <returns>Name, callsign, background, persona (int). No roster recruitment.</returns>
        public static object[] CreatePilot(int seed)
        {
            var random = new System.Random(seed);
            var persona = (ChatterPersona)random.Next(4);
            return new object[]
            {
                PilotIdentity.Name(random.Next),
                PilotIdentity.Callsign(random.Next, _ => false),
                PilotIdentity.Background(random.Next, persona),
                (int)persona,
            };
        }

        /// <summary>Borrow Wing Command's portrait; callers must never destroy it.</summary>
        public static Sprite Portrait(string name, string callsign)
        {
            name = Limit(name, 64);
            callsign = Limit(callsign, 32);
            string key = name + "|" + callsign;
            if (!portraitKeys.Contains(key) && portraitKeys.Count >= 128) return null;
            portraitKeys.Add(key);
            return PilotPortrait.For(new WingPilot { Name = name, Callsign = callsign });
        }

        /// <summary>Spawn an intercept flight through the native server spawner. Index zero is
        /// the ace. Only aircraft created here can receive a target preference through this API.</summary>
        public static Aircraft[] SpawnWing(Aircraft target, FactionHQ enemyHq, int seed, int tier,
                                           int count, string callsign)
        {
            if (target == null || enemyHq == null || !TryIngress(target, enemyHq, seed, out Vector3 position))
                return Array.Empty<Aircraft>();
            GlobalPosition ingress = position.ToGlobalPosition();
            return SpawnWingAt(target, enemyHq, seed, tier, count, callsign, ingress.x, ingress.z);
        }

        /// <summary>Spawn at a host-selected global ingress. The companion owns territory policy.</summary>
        public static Aircraft[] SpawnWingAt(Aircraft target, FactionHQ enemyHq, int seed, int tier,
                                           int count, string callsign, float ingressX, float ingressZ)
        {
            Prune();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer || target == null || !target.IsServer ||
                target.Player == null || target.disabled || target.NetworkHQ == null ||
                enemyHq == null || enemyHq == target.NetworkHQ || enemyHq.faction == null ||
                FactionHelper.EmptyOrNoFactionOrNeutral(enemyHq.faction.factionName) || count < 1 ||
                count > MaxWingSize || owned.Count + count > MaxAircraft)
                return Array.Empty<Aircraft>();

            AircraftDefinition definition = SelectAirframe(seed, tier);
            Aircraft template = definition?.unitPrefab?.GetComponent<Aircraft>();
            if (template == null) return Array.Empty<Aircraft>();
            Loadout loadout = InterceptLoadout(definition);
            if (loadout?.weapons == null || loadout.weapons.Count == 0)
                return Array.Empty<Aircraft>();

            var map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
            if (map == null || float.IsNaN(ingressX) || float.IsInfinity(ingressX) ||
                float.IsNaN(ingressZ) || float.IsInfinity(ingressZ) ||
                Math.Abs(ingressX) > map.MapSize.x * 0.5f - 900 ||
                Math.Abs(ingressZ) > map.MapSize.y * 0.5f - 900) return Array.Empty<Aircraft>();
            GlobalPosition global = target.GlobalPosition(), ingress = global;
            ingress.x = ingressX; ingress.z = ingressZ;
            Vector3 offset = (Vector3)(ingress - global);
            if (offset.sqrMagnitude < 9000f * 9000f) return Array.Empty<Aircraft>();
            Vector3 origin = target.transform.position + offset;
            origin.y = Mathf.Max(target.transform.position.y + 700, Datum.LocalSeaY + 1200);
            Vector3 forward = target.transform.position - origin;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            AircraftParameters parameters = template.GetAircraftParameters();
            float speed = Mathf.Clamp(parameters.cornerSpeed, parameters.takeoffSpeed * 1.5f,
                                      Mathf.Max(parameters.takeoffSpeed * 1.5f, parameters.maxSpeed * 0.75f));
            speed = Mathf.Max(100f, speed);
            var result = new List<Aircraft>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 position = origin + right * ((i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 300f)
                                       - forward * i * 180f;
                    if (Physics.Raycast(position + Vector3.up * 5000f, Vector3.down,
                                        out RaycastHit hit, 12000f, PhysicsLayers.StaticsMask))
                        position.y = Mathf.Max(position.y, hit.point.y + 900f);
                    position.y = Mathf.Max(position.y, Datum.LocalSeaY + 1200f);
                    Aircraft aircraft = spawner.SpawnAircraft(
                        player: null, prefab: definition.unitPrefab,
                        loadout: new Loadout { weapons = new List<WeaponMount>(loadout.weapons) },
                        fuelLevel: 1f, livery: default,
                        globalPosition: position.ToGlobalPosition(),
                        rotation: Quaternion.LookRotation(forward, Vector3.up),
                        startingVel: forward * speed, spawningHangar: null, HQ: enemyHq,
                        uniqueName: Limit(callsign, 24) + "-" + (i + 1) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        skill: Mathf.Clamp01(0.40f + Mathf.Clamp(tier, 0, 8) * 0.065f - i * 0.025f),
                        bravery: Mathf.Clamp01(0.65f + Mathf.Clamp(tier, 0, 8) * 0.04f));
                    if (aircraft == null) throw new InvalidOperationException("Native ace spawn returned no aircraft.");
                    owned.Add(aircraft, target);
                    result.Add(aircraft);
                    if (aircraft.disabled || aircraft.HasEjected())
                        throw new InvalidOperationException("Native ace spawn returned an unflyable aircraft.");
                    if (i == 0) WingSurvivalPerks.RegisterAce(aircraft, tier);
                    SurvivorStatus(aircraft.persistentID);
                    WakeCombat(aircraft, target);
                }
                return result.ToArray();
            }
            catch (Exception error)
            {
                ReleaseWing(result.ToArray(), destroy: true);
                Plugin.Logger.LogWarning("[WingSquad] Spawn rolled back: " + error.Message);
                return Array.Empty<Aircraft>();
            }
        }

        public static bool SetTarget(Aircraft[] wing, Aircraft target)
        {
            if (wing == null || wing.Length > MaxWingSize || target == null || target.disabled ||
                target.Player == null || target.NetworkHQ == null || !target.IsServer || target.HasEjected()) return false;
            bool changed = false;
            foreach (Aircraft aircraft in wing)
            {
                if (aircraft == null || !owned.ContainsKey(aircraft) || !aircraft.IsServer ||
                    aircraft.Player != null || aircraft.disabled || aircraft.NetworkHQ == target.NetworkHQ)
                    continue;
                if (owned[aircraft] != target) WakeCombat(aircraft, target);
                owned[aircraft] = target;
                RefreshHuntTrack(aircraft, target);
                WingRegistry.PrimaryPilot(aircraft)?.SetPrimaryTarget(target);
                changed = true;
            }
            return changed;
        }

        /// <summary>Release hunt preference to native mission AI, or remove spawned aircraft without
        /// kill rewards during transactional rollback / scene teardown.</summary>
        public static void ReleaseWing(Aircraft[] wing, bool destroy)
        {
            if (wing == null || wing.Length > MaxAircraft) return;
            foreach (Aircraft aircraft in wing)
            {
                if (ReferenceEquals(aircraft, null) || !owned.ContainsKey(aircraft)) continue;
                if (destroy || aircraft == null || !aircraft.IsServer || aircraft.Player != null)
                    WingSurvivalPerks.RemoveAce(aircraft);
                owned[aircraft] = null;
                if (aircraft == null) { owned.Remove(aircraft); continue; }
                if (!aircraft.IsServer || aircraft.Player != null) { owned.Remove(aircraft); continue; }
                if (destroy)
                {
                    owned.Remove(aircraft);
                    NetworkManagerNuclearOption.i?.ServerObjectManager.Destroy(
                        aircraft.Identity, !aircraft.Identity.IsSceneObject);
                }
                else if (!aircraft.disabled) WakeCombat(aircraft, null);
            }
        }

        /// <summary>Active ace perks: bit 0 toughness, 1 countermeasures, 2 notch expert, 3 ghost.
        /// Reads the same authority/settings gate as the actual perk hooks.</summary>
        public static int AbilityMask(Aircraft aircraft) => WingSurvivalPerks.AceAbilityMask(aircraft);

        public static void Chatter(string callsign, string context, string message) =>
            WingChatterHud.Enqueue(Limit(callsign, 32), Limit(context, 64), Limit(message, 240),
                                   null, urgent: true, key: "squad:" + Limit(callsign, 32) + ":" + Limit(message, 80));

        /// <summary>Enroll while still seated, then read native evidence after ejection.
        /// 0 unknown, 1 living dismounted pilot, 2 returned, 3 dead, 4 captured.</summary>
        public static int SurvivorStatus(PersistentID aircraftId)
        {
            if (!survivors.TryGetValue(aircraftId, out Survivor record))
            {
                if (survivors.Count >= 128) return 0;
                survivors.Add(aircraftId, new Survivor());
                return 0;
            }
            if (record.Outcome >= 2) return record.Outcome;
            ObserveSurvivor(record.Native);
            return record.Native == null && record.Outcome == 1 ? 0 : record.Outcome;
        }

        /// <summary>Commit a living ace's return after the new aircraft spawn succeeds. Retire the
        /// old survivor through Mirage so one pilot cannot remain downed and fly simultaneously.</summary>
        public static bool RecoverSurvivor(PersistentID aircraftId)
        {
            int status = SurvivorStatus(aircraftId);
            if (status == 2) return true;
            if (status != 1 || !survivors.TryGetValue(aircraftId, out Survivor record) ||
                record.Native == null || !record.Native.IsServer || record.Native.Networkplayer != null) return false;
            record.Outcome = 2;
            record.Native.NetworkunitState = Unit.UnitState.Returned;
            NetworkManagerNuclearOption.i?.ServerObjectManager.Destroy(record.Native.Identity, !record.Native.Identity.IsSceneObject);
            record.Native = null;
            return true;
        }

        private static void ObserveSurvivor(PilotDismounted native)
        {
            if (native == null || !native.IsServer || native.pilotNumber != 0 ||
                !survivors.TryGetValue(native.parentUnit, out Survivor record) || record.Outcome >= 2) return;
            record.Native = native;
            if (native.animationState == PilotDismounted.PilotState.dead) record.Outcome = 3;
            else if (native.unitState == Unit.UnitState.Returned) record.Outcome = 2;
            else if (!native.disabled) record.Outcome = 1;
        }

        [HarmonyPatch(typeof(PilotDismounted), "OnStartServer")]
        internal static class SurvivorSpawnPatch
        {
            [HarmonyPostfix] private static void Postfix(PilotDismounted __instance) => ObserveSurvivor(__instance);
        }
        [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.SetPilotState))]
        internal static class SurvivorStatePatch
        {
            [HarmonyPostfix] private static void Postfix(PilotDismounted __instance) => ObserveSurvivor(__instance);
        }
        [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.UnitDisabled))]
        internal static class SurvivorDisabledPatch
        {
            [HarmonyPostfix] private static void Postfix(PilotDismounted __instance) => ObserveSurvivor(__instance);
        }
        [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.Capture))]
        internal static class SurvivorCapturePatch
        {
            [HarmonyPostfix] private static void Postfix(PilotDismounted __instance, Unit capturingUnit)
            {
                if (__instance == null || !__instance.IsServer || __instance.pilotNumber != 0 || capturingUnit == null ||
                    !survivors.TryGetValue(__instance.parentUnit, out Survivor record) || record.Outcome == 3) return;
                record.Native = __instance;
                record.Outcome = __instance.NetworkHQ != null && capturingUnit.NetworkHQ == __instance.NetworkHQ ? 2 : 4;
            }
        }

        internal static bool TrySelectTarget(Unit searcher, List<WeaponStation> stations,
                                             ref CombatAI.TargetSearchResults result)
        {
            if (!(searcher is Aircraft aircraft) || !aircraft.IsServer || aircraft.Player != null ||
                aircraft.disabled || !owned.TryGetValue(aircraft, out Aircraft target) || target == null)
                return false;
            Pilot targetPilot = WingRegistry.PrimaryPilot(target);
            if (target.disabled || target.Player == null || targetPilot == null || targetPilot.dead ||
                targetPilot.ejected || target.NetworkHQ == null || target.NetworkHQ == aircraft.NetworkHQ)
            {
                owned[aircraft] = null;
                return false;
            }
            TrackingInfo tracking = RefreshHuntTrack(aircraft, target);
            if (tracking == null || stations == null)
                return false;
            WeaponStation best = null;
            float opportunity = 0f;
            for (int i = 0; i < Math.Min(stations.Count, 64); i++)
            {
                WeaponStation station = stations[i];
                if (station == null || station.Cargo || station.Ammo <= 0 || station.WeaponInfo == null ||
                    station.WeaponInfo.nuclear ||
                    (station.WeaponInfo.energy && (aircraft.GetPowerSupply()?.GetCharge() ?? 0f) < 0.6f)) continue;
                float value = CombatAI.AnalyzeTarget(station, aircraft, tracking, 0f, -1f, 100f).opportunity;
                if (value <= opportunity) continue;
                opportunity = value;
                best = station;
            }
            if (best == null) return false;
            result = new CombatAI.TargetSearchResults(target, best, opportunity, result.outOfAmmo);
            return true;
        }

        internal static void Reset()
        {
            // The companion may receive its scene callback after Wing Command; retain
            // ownership until all spawned AI have been removed through Mirage.
            stale.Clear();
            stale.AddRange(owned.Keys);
            ReleaseWing(stale.ToArray(), destroy: true);
            owned.Clear();
            WingSurvivalPerks.ClearAces();
            stale.Clear();
            portraitKeys.Clear();
            survivors.Clear();
        }

        private static void WakeCombat(Aircraft aircraft, Aircraft target)
        {
            Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
            if (pilot == null || pilot.dead || pilot.ejected) return;
            if (target != null) RefreshHuntTrack(aircraft, target);
            pilot.SetPrimaryTarget(target);
            if (pilot.AICombatState != null) pilot.SwitchState(pilot.AICombatState);
            else if (pilot.AIHeloCombatState != null) pilot.SwitchState(pilot.AIHeloCombatState);
        }

        private static TrackingInfo RefreshHuntTrack(Aircraft aircraft, Aircraft target)
        {
            FactionHQ hq = aircraft.NetworkHQ;
            if (!aircraft.IsServer || hq == null || target == null || target.disabled ||
                target.HasEjected() || target.Player == null || target.NetworkHQ == hq ||
                !owned.TryGetValue(aircraft, out Aircraft marked) || marked != target) return null;
            // The hunt supplies persistent faction intelligence. Refresh every director tick
            // and target evaluation; native weapon range, line of sight and locks still apply.
            TrackingInfo tracking = hq.GetTrackingData(target.persistentID);
            if (tracking == null)
            {
                tracking = new TrackingInfo(target);
                hq.trackingDatabase[target.persistentID] = tracking;
            }
            else tracking.UpdateInfo(target.GlobalPosition());
            return tracking;
        }

        private static AircraftDefinition SelectAirframe(int seed, int tier)
        {
            var catalogue = Encyclopedia.i?.aircraft;
            string wanted = AceIngress.Airframe(tier);
            if (catalogue == null || wanted == null) return null;
            for (int i = 0; i < Math.Min(catalogue.Count, 128); i++)
            {
                AircraftDefinition definition = catalogue[i];
                if (definition?.unitPrefab == null ||
                    definition.unitPrefab.GetComponentInChildren<AutopilotPlane>(true) == null) continue;
                // Ace selection is independent of the player shop's hidden-airframe filter.
                string name = definition.unitName ?? "";
                if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(wanted + " ", StringComparison.OrdinalIgnoreCase)) return definition;
            }
            return null;
        }

        private static bool TryIngress(Aircraft target, FactionHQ enemy, int seed, out Vector3 origin)
        {
            origin = default;
            var map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
            var registry = FactionRegistry.airbaseLookup;
            if (map == null || registry == null || registry.Count > 256) return false;
            var bases = new List<AceIngress.Base>(registry.Count);
            foreach (Airbase airbase in registry.Values)
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                GlobalPosition p = airbase.transform.position.ToGlobalPosition();
                bases.Add(new AceIngress.Base(p.x, p.z, airbase.CurrentHQ == enemy));
            }
            GlobalPosition player = target.transform.position.ToGlobalPosition();
            if (!AceIngress.TryPick(map.MapSize.x, map.MapSize.y, player.x, player.z, bases, seed,
                out float x, out float z)) return false;
            // Convert through the datum so origin shifts cannot move the perimeter.
            GlobalPosition ingress = player;
            ingress.x = x; ingress.z = z;
            origin = target.transform.position + (Vector3)(ingress - player);
            origin.y = Mathf.Max(target.transform.position.y + 700, Datum.LocalSeaY + 1200);
            return true;
        }

        private static Loadout InterceptLoadout(AircraftDefinition definition)
        {
            int pylons = EconomyFacade.LoadoutCatalog.PylonCount(definition);
            if (pylons <= 0 || pylons > 32) return null;
            var keys = new List<string>(pylons);
            var options = new List<WingLoadoutCatalog.StoreOption>();
            bool armed = false;
            for (int i = 0; i < pylons; i++)
            {
                options.Clear();
                EconomyFacade.LoadoutCatalog.OptionsFor(definition, i, options);
                string key = null;
                float value = 0f;
                for (int j = 0; j < Math.Min(options.Count, 128); j++)
                {
                    var option = options[j];
                    if (option.Cargo || option.AntiAir <= value || option.Ammo == 0) continue;
                    value = option.AntiAir;
                    key = option.Key;
                }
                armed |= key != null;
                keys.Add(key);
            }
            return armed ? EconomyFacade.LoadoutCatalog.FillScratch(definition, keys) : null;
        }

        private static void Prune()
        {
            stale.Clear();
            foreach (Aircraft aircraft in owned.Keys)
                if (aircraft == null || aircraft.Player != null) stale.Add(aircraft);
            foreach (Aircraft aircraft in stale)
            { WingSurvivalPerks.RemoveAce(aircraft); owned.Remove(aircraft); }
            stale.Clear();
        }

        private static string Limit(string value, int length) => string.IsNullOrEmpty(value)
            ? string.Empty : value.Substring(0, Math.Min(value.Length, length));
    }
}
