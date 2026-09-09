using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Spawns vertical aircraft through native pads, surface units astern of the player, and
    /// runway aircraft at the threshold with no spawningHangar. Threshold placement avoids unreliable
    /// shelter taxi; native AI owns movement after spawn.</summary>
    internal static class WingShopDelivery
    {
        private static readonly List<Airbase> fieldScratch = new List<Airbase>();

        // Delivery dispatch.

        /// <summary>Spawn or queue the purchase at an eligible field. Pending orders show QUE; accepted
        /// field departures show DEPT until registration.</summary>
        public static bool Deliver(WingShop.PurchaseTransaction transaction, Aircraft leader,
                                   FactionHQ hq, out string reason)
        {
            reason = null;
            if (transaction == null || transaction.Definition == null)
            {
                reason = "Invalid purchase transaction";
                return false;
            }

            AircraftDefinition definition = transaction.Definition;
            Loadout loadout = BuildLoadout(definition, transaction.Loadout);

            if (WingShop.IsSurfaceDefinition(definition))
            {
                Aircraft spawned = SpawnSurface(definition, leader, hq, loadout);
                if (spawned == null)
                {
                    reason = "Delivery failed - see the BepInEx log";
                    return false;
                }

                if (!transaction.Commit(spawned))
                {
                    reason = "Delivery transaction could not be committed";
                    return false;
                }
                float fuel = WingShop.SpawnFuelFor(definition);
                RememberSpawnFuel(spawned, fuel);
                ApplySpawnFuel(spawned, fuel);
                WingCommandManager.Instance?.QueueRecruit(spawned, transaction.Pilot);
                return true;
            }

            if (TryFieldDelivery(transaction, leader, hq, loadout, out reason)) return true;

            if (string.IsNullOrEmpty(reason))
                reason = "No field that can launch this airframe";
            return false;
        }

        /// <summary>Build the requested fit, returning null on failure so native spawning can use standard
        /// equipment without blocking delivery.</summary>
        private static Loadout BuildLoadout(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            try
            {
                return WingLoadoutCatalog.Build(definition, choice);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning(
                    "[Shop] could not build the " + WingLoadoutCatalog.Label(choice) +
                    " loadout; using the standard fit: " + e.Message);
                return null;
            }
        }

        /// <summary>Classify departure from prefab autopilot: helos/tiltwings use pads unless all relevant
        /// seats identify a plane-typed VTOL. Inspect every Pilot so a gunner cannot misclassify the
        /// airframe; plane autopilots use runways.</summary>
        private static bool LaunchesVertically(AircraftDefinition definition)
        {
            if (definition == null) return false;
            if (verticalCache.TryGetValue(definition, out bool cached)) return cached;

            bool rotary = WingShop.IsRotary(definition);
            bool vertical = rotary;

            GameObject prefab = definition.unitPrefab;
            Pilot.PilotType? flightType = null;
            if (rotary && prefab != null)
            {
                bool anyVerticalSeat = false;
                bool anyPlaneSeat = false;
                foreach (Pilot pilot in prefab.GetComponentsInChildren<Pilot>(includeInactive: true))
                {
                    if (pilot == null) continue;
                    flightType ??= pilot.pilotType;
                    if (pilot.pilotType == Pilot.PilotType.Helo ||
                        pilot.pilotType == Pilot.PilotType.Tiltwing ||
                        pilot.pilotType == Pilot.PilotType.VTOL)
                        anyVerticalSeat = true;
                    else if (pilot.pilotType == Pilot.PilotType.Plane)
                        anyPlaneSeat = true;
                }

                // Use runway departure only when a Plane seat exists and no seat reports vertical
                // flight.
                if (!anyVerticalSeat && anyPlaneSeat) vertical = false;
            }

            verticalCache[definition] = vertical;

            if (verticalLogged.Add(definition) && Plugin.Settings != null &&
                Plugin.Settings.VerboseLogging.Value)
            {
                Autopilot ap = prefab != null
                    ? prefab.GetComponentInChildren<Autopilot>(includeInactive: true) : null;
                Plugin.LogVerbose(
                    "[Shop] " + definition.unitName + " launch mode=" +
                    (vertical ? "pad" : "runway") + " (rotary=" + rotary +
                    " autopilot=" + (ap != null ? ap.GetType().Name : "none") +
                    " pilotType=" + (flightType.HasValue ? flightType.Value.ToString() : "none") + ")");
            }

            return vertical;
        }

        private static readonly HashSet<AircraftDefinition> verticalLogged =
            new HashSet<AircraftDefinition>();

        private static readonly Dictionary<AircraftDefinition, bool> verticalCache =
            new Dictionary<AircraftDefinition, bool>();

        /// <summary>Shared live player-default fit with game-start fallback, keeping pad and runway
        /// STANDARD equipment consistent.</summary>
        private static Loadout DefaultLoadout(AircraftDefinition definition) =>
            WingLoadoutCatalog.ClonePlayerDefault(definition);

        private sealed class PendingFuel
        {
            public Aircraft Aircraft;
            public float Fuel;
            public float Until;
        }

        private static readonly List<PendingFuel> pendingFuel = new List<PendingFuel>();

        private static void RememberSpawnFuel(Aircraft aircraft, float fuel)
        {
            if (aircraft == null) return;
            pendingFuel.Add(new PendingFuel
            {
                Aircraft = aircraft,
                Fuel = Mathf.Clamp01(fuel),
                Until = Time.unscaledTime + 3f,
            });
        }

        private static void TickSpawnFuel()
        {
            float now = Time.unscaledTime;
            for (int i = pendingFuel.Count - 1; i >= 0; i--)
            {
                PendingFuel apply = pendingFuel[i];
                if (apply.Aircraft == null || now > apply.Until)
                {
                    pendingFuel.RemoveAt(i);
                    continue;
                }
                ApplySpawnFuel(apply.Aircraft, apply.Fuel);
            }
        }

        private static void ApplySpawnFuel(Aircraft aircraft, float fuel)
        {
            if (aircraft == null) return;
            fuel = Mathf.Clamp01(fuel);
            foreach (FuelTank tank in aircraft.GetFuelTanks())
                if (tank != null) tank.Refuel(fuel);
            aircraft.NetworkfuelLevel = fuel;
        }

        // Pending deliveries.

        internal sealed class PendingDelivery
        {
            public WingShop.PurchaseTransaction Transaction;
            public Airbase Origin;
            public Loadout Loadout;
            public LiveryKey Livery;
            public float Fuel;

            /// <summary>Accepting pad, or null for runway launch.</summary>
            public Hangar Hangar;

            /// <summary>Whether a field has accepted this order.</summary>
            public bool Claimed;

            public GameObject PreviousSpawnedObject;
            public bool NativeAccepted;
            public bool NativeSequenceFinished;
            public bool NativeAircraftObserved;
            public Aircraft ObservedAircraft;
            public bool DelayReported;
            public float RequestedAt;
            public float ExpiresAt;
            public float NextAttemptAt;
            public bool Starting;

            /// <summary>Whether the order must wait at Origin. Any-mode orders remain unpinned until a
            /// field accepts them.</summary>
            public bool Pinned;

            /// <summary>Whether departure uses a vertical pad instead of a runway threshold.</summary>
            public bool Vertical;

            public AircraftDefinition Definition => Transaction?.Definition;
            public string AirframeName => Definition != null ? Definition.unitName : "Airframe";
            public string StatusCode => HangarFieldPolicy.StatusCode(Claimed);
            public bool CanCancel => !NativeAccepted && !Starting;
        }

        private static readonly List<PendingDelivery> pending = new List<PendingDelivery>();
        private static FactionHQ watched;

        public static int PendingCount => pending.Count;

        public static PendingDelivery GetPending(int index) =>
            (index >= 0 && index < pending.Count) ? pending[index] : null;

        /// <summary>Order currently executing this hangar's native spawn call, if any.</summary>
        internal static PendingDelivery StartingAt(Hangar hangar)
        {
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Starting && pending[i].Hangar == hangar) return pending[i];
            return null;
        }

        public static bool CancelPending(PendingDelivery order)
        {
            if (order == null || !pending.Contains(order)) return false;
            if (!order.CanCancel)
            {
                WingCommandManager.Instance?.Toast(
                    "Launch already accepted; wait for delivery before releasing");
                return false;
            }
            FailDelivery(order, "cancelled by player");
            return true;
        }

        /// <summary>Capability-based launch refusal, or null. Check before charging; temporary field
        /// occupancy should queue rather than reject a purchase.</summary>
        public static string LaunchBlockReason(FactionHQ hq, AircraftDefinition definition,
                                               Vector3 from)
        {
            if (definition == null) return "No aircraft selected";
            if (WingShop.IsSurfaceDefinition(definition)) return null;
            if (hq == null) return "No faction";

            if (NetworkSceneSingleton<Spawner>.i == null && !LaunchesVertically(definition))
                return "The spawner is not available";

            CollectFields(hq);
            for (int i = 0; i < fieldScratch.Count; i++)
            {
                if (!WingLaunchFields.IsAllowed(fieldScratch[i])) continue;
                if (CanEverProduce(fieldScratch[i], definition)) return null;
            }

            return LaunchesVertically(definition)
                ? "No selected field has a pad that lists " + definition.unitName
                : "No selected field has a takeoff runway and stocks " + definition.unitName;
        }

        // Launch-field selection.

        private static bool TryFieldDelivery(WingShop.PurchaseTransaction transaction,
                                             Aircraft leader, FactionHQ hq, Loadout loadout,
                                             out string reason)
        {
            reason = null;
            if (hq == null || leader == null)
            {
                reason = "No field that can launch this airframe";
                return false;
            }

            if (!WingLaunchFields.HasAnyAllowed(hq))
            {
                reason = "No launch bases selected";
                return false;
            }

            AircraftDefinition definition = transaction.Definition;
            Vector3 from = leader.transform.position;
            CollectFields(hq);

            bool anyCanProduce = false;
            for (int i = 0; i < fieldScratch.Count; i++)
            {
                if (!WingLaunchFields.IsAllowed(fieldScratch[i])) continue;
                if (!CanEverProduce(fieldScratch[i], definition)) continue;
                anyCanProduce = true;
                break;
            }

            if (!anyCanProduce)
            {
                reason = LaunchBlockReason(hq, definition, from) ??
                         "No field that can launch this airframe";
                return false;
            }

            HangarLaunchMode mode = WingLaunchFields.Mode;
            int index = SelectOrigin(definition, from, mode);
            bool pin = mode == HangarLaunchMode.OnlyNearest;
            Airbase airbase = index >= 0 ? fieldScratch[index] : null;
            if (pin && airbase == null)
            {
                // OnlyNearest pins to the nearest field even while busy.
                index = SelectOrigin(definition, from, HangarLaunchMode.OnlyNearest);
                airbase = index >= 0 ? fieldScratch[index] : null;
                if (airbase == null)
                {
                    reason = "No field that can launch this airframe";
                    return false;
                }
            }

            var order = new PendingDelivery
            {
                Transaction = transaction,
                Origin = airbase,
                Loadout = loadout,
                Livery = LiveryFor(definition, hq, leader),
                Fuel = WingShop.SpawnFuelFor(definition),
                RequestedAt = Time.unscaledTime,
                ExpiresAt = Time.unscaledTime + WingTuning.HangarDeliveryTimeout,
                Pinned = pin,
                Vertical = LaunchesVertically(definition),
            };

            Watch(hq);
            pending.Add(order);

            if (order.Origin != null && CanLaunchNow(order.Origin, definition))
                Attempt(order);
            else
                order.NextAttemptAt = Time.unscaledTime + WingTuning.HangarRetryInterval;

            string fieldName = order.Origin != null
                ? WingLaunchFields.DisplayName(order.Origin) : "an allowed field";
            Plugin.LogVerbose(order.Claimed
                ? "[Shop] " + definition.unitName + " ordered from " + fieldName +
                  " with the " + WingLoadoutCatalog.Label(transaction.Loadout) + " fit"
                : "[Shop] " + definition.unitName + " queued for " + fieldName +
                  (pin ? " - it is busy" : " - waiting for any free allowed field"));
            return true;
        }

        private static LiveryKey LiveryFor(AircraftDefinition definition, FactionHQ hq,
                                           Aircraft leader)
        {
            LiveryKey? chosen = null;
            int liveryIdx = WingLoadoutTemplates.GetLiveryIndex(definition);
            List<WingLoadoutTemplates.LiveryOption> options =
                WingLoadoutTemplates.GetLiveries(definition, hq != null ? hq.faction : null);
            if (liveryIdx > 0 && liveryIdx < options.Count) chosen = options[liveryIdx].Key;
            if (chosen.HasValue) return chosen.Value;

            AircraftParameters p = definition != null ? definition.aircraftParameters : null;
            if (p != null && hq != null && hq.faction != null)
                return new LiveryKey(p.GetRandomLiveryForFaction(hq.faction));
            return leader != null ? leader.NetworkLiveryKey : default(LiveryKey);
        }

        private static void CollectFields(FactionHQ hq)
        {
            fieldScratch.Clear();
            if (hq == null) return;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                fieldScratch.Add(airbase);
            }
        }

        private static int SelectOrigin(AircraftDefinition definition, Vector3 from,
                                        HangarLaunchMode mode) =>
            HangarFieldPolicy.SelectOrigin(
                fieldScratch.Count,
                mode,
                i => (fieldScratch[i].transform.position - from).sqrMagnitude,
                i => WingLaunchFields.IsAllowed(fieldScratch[i]),
                i => CanEverProduce(fieldScratch[i], definition),
                i => CanLaunchNow(fieldScratch[i], definition));

        /// <summary>Check serialized hangar type support independent of current occupancy. Fixed-wing
        /// departures also require a takeoff strip.</summary>
        private static bool CanEverProduce(Airbase airbase, AircraftDefinition definition)
        {
            if (!WingLaunchFields.CanProduce(airbase, definition)) return false;
            if (LaunchesVertically(definition)) return true;
            return WingAirfield.HasTakeoffRunway(airbase, definition);
        }

        /// <summary>Whether this field can accept a departure now; reserve per field because its hangars
        /// share runways.</summary>
        private static bool CanLaunchNow(Airbase airbase, AircraftDefinition definition)
        {
            if (airbase == null || airbase.disabled) return false;
            if (!HangarDepartureLane.IsFree(airbase)) return false;
            return LaunchesVertically(definition)
                ? SelectClearHangar(airbase, definition) != null
                : WingAirfield.HasTakeoffRunway(airbase, definition);
        }

        private static Hangar SelectClearHangar(Airbase airbase, AircraftDefinition definition)
        {
            if (airbase == null || airbase.disabled || definition == null) return null;
            IList<Hangar> hangars = airbase.hangars;
            if (hangars == null) return null;

            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar hangar = hangars[i];
                if (hangar == null || hangar.Disabled) continue;
                if (!hangar.Available) continue;
                if (!hangar.CanSpawnAircraft(WingHangarStock.NativeDefinition(hangar, definition)))
                    continue;
                if (HangarClaimedByPending(hangar)) continue;
                return hangar;
            }

            return null;
        }

        private static bool HangarClaimedByPending(Hangar hangar)
        {
            if (hangar == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Hangar == hangar) return true;
            return false;
        }

        private static bool AnyAllowedCanProduce(PendingDelivery order)
        {
            if (order?.Transaction == null) return false;
            AircraftDefinition definition = order.Transaction.Definition;
            CollectFields(order.Transaction.Hq);
            for (int i = 0; i < fieldScratch.Count; i++)
            {
                if (!WingLaunchFields.IsAllowed(fieldScratch[i])) continue;
                if (CanEverProduce(fieldScratch[i], definition)) return true;
            }
            return false;
        }

        // Spawn attempts.

        private static void Attempt(PendingDelivery order)
        {
            if (order?.Origin == null || order.Transaction == null) return;
            if (order.Vertical) AttemptPadSpawn(order);
            else AttemptRunwaySpawn(order);
        }

        /// <summary>Spawn runway aircraft with spawningHangar=null. A non-null SyncVar makes clients snap
        /// the aircraft back onto that hangar's pad.</summary>
        private static void AttemptRunwaySpawn(PendingDelivery order)
        {
            FactionHQ hq = order.Transaction.Hq;
            AircraftDefinition definition = order.Transaction.Definition;
            if (hq == null || definition == null) return;

            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            GameObject prefab = definition.unitPrefab;
            if (spawner == null || prefab == null)
            {
                Plugin.Logger.LogWarning("[Shop] no spawner or prefab for " + definition.unitName);
                return;
            }

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;

            if (!WingAirfield.TryBuildLaunchPose(order.Origin, definition,
                                                 out WingAirfield.LaunchPose pose))
                return;

            // Wait for landing traffic and physical runway clearance, including wrecks.
            if (WingAirfield.LaunchSpotBlocked(pose, definition, out string blocker))
            {
                Plugin.LogVerbose("[Shop] " + definition.unitName + " launch from " +
                    WingLaunchFields.DisplayName(order.Origin) +
                    " held - threshold blocked by " + blocker);
                return;
            }

            // Supply a valid loadout because native OnStartClient blindly falls back to loadouts[1];
            // runway spawns lack the hangar's later fitting step.
            Loadout loadout = order.Loadout ?? DefaultLoadout(definition);

            // Anchor lane clearance to the actual threshold, not a potentially distant field centre.
            if (!HangarDepartureLane.Reserve(order.Origin, pose.Threshold, order)) return;

            // Claim the heading only once the runway is clear and this delivery is ready to spawn.
            pose.Runway.SetUsageDirection(pose.Reverse);

            order.Starting = true;
            Aircraft spawned;
            try
            {
                spawned = spawner.SpawnAircraft(
                    player: null,
                    prefab: prefab,
                    loadout: loadout,
                    fuelLevel: order.Fuel,
                    livery: order.Livery,
                    globalPosition: pose.Position,
                    rotation: pose.Rotation,
                    startingVel: pose.Velocity,
                    spawningHangar: null,
                    HQ: hq,
                    uniqueName: "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    skill: leader != null ? leader.skill : 1f,
                    bravery: leader != null ? leader.bravery : 0.5f);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Shop] runway spawn failed: " + e);
                spawned = null;
            }
            finally
            {
                order.Starting = false;
            }

            if (spawned == null)
            {
                HangarDepartureLane.Release(order);
                return;
            }

            order.NativeAccepted = true;
            order.NativeAircraftObserved = true;
            order.ObservedAircraft = spawned;
            order.Claimed = true;

            // Native server/client initialisation has already set taxi synchronously. Watch later
            // physics updates to confirm it actually moves.
            WingAirfield.WatchLaunch(spawned, pose);
            HangarDepartureLane.Track(order, spawned);

            Plugin.LogVerbose(
                "[Shop] " + definition.unitName + " launched from " +
                WingLaunchFields.DisplayName(order.Origin) + "/" +
                pose.Runway.GetName(pose.Reverse) + " at " + pose.Position +
                " (spawningHangar=null)");

            Claim(order, spawned);
        }

        /// <summary>Let native pad doors handle vertical launch. Keep accepted orders pending for
        /// registration and refused orders pending for retry.</summary>
        private static void AttemptPadSpawn(PendingDelivery order)
        {
            FactionHQ hq = order.Transaction.Hq;
            AircraftDefinition definition = order.Transaction.Definition;
            if (hq == null || definition == null) return;

            Hangar selected = SelectClearHangar(order.Origin, definition);
            if (selected == null) return;
            if (!HangarDepartureLane.Reserve(order.Origin, selected, order)) return;

            // Reserve the specific pad before native code can synchronously register the aircraft.
            order.Hangar = selected;
            // Ignore the hangar's retained previous spawned object until a new aircraft replaces it.
            order.PreviousSpawnedObject = GameAccess.GetHangarSpawnedObject(selected);

            order.Starting = true;
            Airbase.TrySpawnResult result;
            int stockBeforeNative = hq.GetUnitSupply(definition);
            try
            {
                result = selected.TrySpawnAircraft(null, definition, order.Livery,
                                                   order.Loadout, order.Fuel);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Shop] hangar delivery threw: " + e.Message);
                result = default(Airbase.TrySpawnResult);
            }
            finally
            {
                // The transaction already debited stock; compensate native null-player spawning's
                // duplicate debit with ModifyUnitSupply. AddSupplyUnit can instead fulfil another
                // player's reserve request without restoring the count.
                try
                {
                    int give = SupplyCompensation.Delta(stockBeforeNative,
                                                        hq.GetUnitSupply(definition));
                    if (give > 0) hq.ModifyUnitSupply(definition, give);
                }
                finally { order.Starting = false; }
            }

            // A thrown native spawn may still have scheduled doors; retain ownership when the pad
            // accepted or observed an aircraft.
            order.NativeAccepted = result.Allowed || order.NativeAircraftObserved ||
                                   (selected != null && !selected.Available);
            if (!order.NativeAccepted)
            {
                order.Hangar = null;
                HangarDepartureLane.Release(order);
                return;
            }

            order.Claimed = true;
            Plugin.LogVerbose("[Shop] launch accepted: " + definition.unitName +
                " at " + WingLaunchFields.DisplayName(order.Origin) + "/" + selected.name +
                " position=" + selected.GetSpawnTransform().position.ToGlobalPosition());

            // Start a fresh door-sequence timeout on acceptance, independent of time spent queued.
            order.ExpiresAt = Time.unscaledTime + WingTuning.HangarDeliveryTimeout;

            GameObject immediateSpawn = GameAccess.GetHangarSpawnedObject(selected);
            if (immediateSpawn != null)
            {
                Aircraft direct = immediateSpawn.GetComponent<Aircraft>();
                if (direct != null) TryClaim(direct);
            }

            // Handle registration that occurred before the native call returned or updated its hangar
            // field.
            TryClaim(order.ObservedAircraft);
        }

        // Delivery claims.

        private static void Watch(FactionHQ hq)
        {
            if (watched == hq) return;
            if (watched != null) watched.onRegisterUnit -= OnUnitRegistered;
            watched = hq;
            if (watched != null) watched.onRegisterUnit += OnUnitRegistered;
        }

        /// <summary>Claim only this order's accepting pad's aircraft.</summary>
        private static void OnUnitRegistered(Unit unit)
        {
            if (!(unit is Aircraft aircraft)) return;

            // Bind registrations to the exact requested hangar before spawnedObject updates; never
            // refund an observed partial spawn.
            for (int i = 0; i < pending.Count; i++)
            {
                PendingDelivery order = pending[i];
                if (!order.Vertical) continue;
                if (order.Transaction.Definition != aircraft.definition ||
                    order.Transaction.Hq != aircraft.NetworkHQ ||
                    aircraft.gameObject == order.PreviousSpawnedObject)
                    continue;
                Hangar spawningHangar = aircraft.NetworkspawningHangar;
                if (spawningHangar == null || spawningHangar != order.Hangar) continue;
                order.NativeAircraftObserved = true;
                order.ObservedAircraft = aircraft;
            }

            TryClaim(aircraft);
        }

        private static void TryClaim(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.Player != null ||
                aircraft.NetworkspawningHangar == null)
                return;

            PendingDelivery match = null;
            int matches = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingDelivery order = pending[i];
                if (!Matches(order, aircraft)) continue;
                match = order;
                matches++;
            }

            if (matches > 1)
            {
                Plugin.Logger.LogWarning(
                    "[Shop] ambiguous hangar registration for " + aircraft.unitName +
                    "; refusing to commandeer it");
                return;
            }
            if (matches != 1 || match == null) return;

            HangarDepartureLane.Track(match, aircraft);
            Claim(match, aircraft);
        }

        /// <summary>Commit the purchase and queue the aircraft for wing recruitment.</summary>
        private static void Claim(PendingDelivery order, Aircraft aircraft)
        {
            if (order == null || aircraft == null) return;
            pending.Remove(order);
            if (pending.Count == 0) Watch(null);

            if (!order.Transaction.Commit(aircraft))
            {
                Plugin.Logger.LogError("[Shop] " + aircraft.unitName +
                    " spawned but the purchase commit failed; not recruiting");
                return;
            }

            try { aircraft.SetLiveryKey(order.Livery); } catch { }

            RememberSpawnFuel(aircraft, order.Fuel);
            ApplySpawnFuel(aircraft, order.Fuel);

            // Add roster membership now; DeliveryHold retains native controls until LaunchSafety
            // permits handoff.
            WingCommandManager.Instance?.QueueRecruit(aircraft, order.Transaction?.Pilot);
            WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
            if (member != null) HangarDepartureLane.Transfer(order, member);

            Plugin.LogVerbose("[Shop] " + aircraft.unitName + " registered from " +
                                  WingLaunchFields.DisplayName(order.Origin) +
                                  "; handed to wing recruit queue");
        }

        private static bool Matches(PendingDelivery order, Aircraft aircraft)
        {
            if (order == null || !order.Vertical || order.Starting || order.Hangar == null ||
                order.Transaction == null || aircraft == null)
                return false;
            if (order.Transaction.Definition != aircraft.definition) return false;
            if (aircraft.NetworkHQ != order.Transaction.Hq) return false;
            if (aircraft.gameObject == order.PreviousSpawnedObject) return false;

            GameObject spawnedObject = GameAccess.GetHangarSpawnedObject(order.Hangar);
            if (spawnedObject != null && spawnedObject == aircraft.gameObject) return true;
            if (aircraft.NetworkspawningHangar != order.Hangar) return false;
            if (Time.unscaledTime + 0.01f < order.RequestedAt) return false;

            Transform spawn = order.Hangar.GetSpawnTransform();
            return spawn == null ||
                   (aircraft.transform.position - spawn.position).sqrMagnitude <= 1000f * 1000f;
        }

        // Delivery updates.

        /// <summary>Advance pending orders oldest first, retrying eligible fields and resolving failures
        /// that cannot produce an aircraft.</summary>
        public static void Tick()
        {
            TickSpawnFuel();
            HangarDepartureLane.Tick();

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.ObservedAircraft != null)
                {
                    TryClaim(order.ObservedAircraft);
                    if (!pending.Contains(order)) continue;
                }
                if (order.Hangar == null) continue;

                GameObject spawnedObject = GameAccess.GetHangarSpawnedObject(order.Hangar);
                if (spawnedObject == null) continue;
                Aircraft direct = spawnedObject.GetComponent<Aircraft>();
                if (direct == null || !Matches(order, direct)) continue;

                order.NativeAircraftObserved = true;
                order.ObservedAircraft = direct;
                TryClaim(direct);
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.NativeAccepted && HangarFieldPolicy.CanRefundDelivery(
                        order.NativeAccepted, order.NativeSequenceFinished,
                        order.Vertical && order.Hangar == null, order.NativeAircraftObserved))
                {
                    FailDelivery(order, order.Hangar == null && order.Vertical
                        ? "launch hangar was destroyed"
                        : "native launch ended without producing an aircraft");
                    continue;
                }
                if (order.Claimed || order.Starting) continue;

                bool stillPossible = order.Pinned
                    ? CanEverProduce(order.Origin, order.Transaction.Definition)
                    : AnyAllowedCanProduce(order);
                if (stillPossible) continue;

                Plugin.Logger.LogWarning(
                    "[Shop] " + order.AirframeName + " - " +
                    (order.Origin != null ? WingLaunchFields.DisplayName(order.Origin) : "allowed fields") +
                    " can no longer produce it");
                FailDelivery(order, "field can no longer produce this aircraft");
            }

            // Throttle FIFO retries and require field availability.
            float now = Time.unscaledTime;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingDelivery order = pending[i];
                if (order.Claimed || order.Starting) continue;
                if (now < order.NextAttemptAt) continue;

                AircraftDefinition definition = order.Transaction.Definition;
                Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
                Vector3 from = leader != null ? leader.transform.position : Vector3.zero;

                if (!order.Pinned)
                {
                    CollectFields(order.Transaction.Hq);
                    int index = SelectOrigin(definition, from, HangarLaunchMode.Any);
                    if (index < 0)
                    {
                        order.NextAttemptAt = now + WingTuning.HangarRetryInterval;
                        continue;
                    }
                    order.Origin = fieldScratch[index];
                }
                else if (order.Origin == null || !CanLaunchNow(order.Origin, definition))
                {
                    order.NextAttemptAt = now + WingTuning.HangarRetryInterval;
                    continue;
                }

                Attempt(order);
                // Account for immediate claim removal so the shifted next order is still visited.
                if (!pending.Contains(order)) { i--; continue; }
                if (!order.Claimed)
                {
                    if (!order.Pinned) order.Origin = null;
                    order.NextAttemptAt = Time.unscaledTime + WingTuning.HangarRetryInterval;
                }
            }

            now = Time.unscaledTime;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.NativeAccepted)
                {
                    if (now < order.ExpiresAt || order.DelayReported) continue;
                    order.DelayReported = true;
                    Plugin.Logger.LogWarning(
                        "[Shop] delivery of " + order.AirframeName +
                        " is delayed; retaining the order until the native launch completes");
                    WingCommandManager.Instance?.Toast(
                        order.AirframeName + " launch delayed; still awaiting delivery");
                    continue;
                }

                if (now < order.RequestedAt + WingTuning.HangarDeliveryTimeout) continue;
                Plugin.Logger.LogWarning(
                    "[Shop] delivery of " + order.AirframeName + " waited too long for a field");
                FailDelivery(order, "delivery timed out");
            }

            if (pending.Count == 0 && watched != null) Watch(null);
        }

        private static void FailDelivery(PendingDelivery order, string reason)
        {
            if (!HangarFieldPolicy.CanRefundDelivery(
                    order.NativeAccepted, order.NativeSequenceFinished,
                    order.Vertical && order.Hangar == null, order.NativeAircraftObserved))
                return;

            HangarDepartureLane.Release(order);
            bool restored = order.Transaction.Rollback(reason);
            WingCommandManager.Instance?.Toast(
                order.AirframeName +
                (restored
                    ? " delivery failed - funds and stock restored"
                    : " delivery failed - refund is retrying"));
            pending.Remove(order);
        }

        public static void Reset()
        {
            pendingFuel.Clear();
            HangarDepartureLane.Reset();
            WingAirfield.Reset();
            for (int i = 0; i < pending.Count; i++)
                pending[i].Transaction?.Rollback("mission reset");
            pending.Clear();
            fieldScratch.Clear();
            verticalCache.Clear();
            Watch(null);
            WingLaunchFields.Reset();
        }

        // Surface delivery.

        private static Aircraft SpawnSurface(AircraftDefinition definition, Aircraft leader,
                                             FactionHQ hq, Loadout loadout)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null)
            {
                Plugin.Logger.LogWarning("[Shop] spawner unavailable");
                return null;
            }

            GameObject prefab = definition.unitPrefab;
            if (prefab == null)
            {
                Plugin.Logger.LogWarning("[Shop] no prefab for " + definition.unitName);
                return null;
            }

            if (!SurfacePlacement(leader, out Vector3 position, out Quaternion rotation,
                                  out Vector3 velocity))
                return null;

            try
            {
                return spawner.SpawnAircraft(
                    player: null,
                    prefab: prefab,
                    loadout: loadout,
                    fuelLevel: WingShop.SpawnFuelFor(definition),
                    livery: LiveryFor(definition, hq, leader),
                    globalPosition: position.ToGlobalPosition(),
                    rotation: rotation,
                    startingVel: velocity,
                    spawningHangar: null,
                    HQ: hq,
                    uniqueName: "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    skill: leader.skill,
                    bravery: leader.bravery);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Shop] spawn failed: " + e);
                return null;
            }
        }

        /// <summary>Place surface units astern at slot spacing on the player's plane; they cannot use
        /// aircraft hangars.</summary>
        private static bool SurfacePlacement(Aircraft leader, out Vector3 position,
                                             out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;
            if (leader == null) return false;

            Vector3 forward = leader.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float astern = WingFormation.SlotSpacing * WingTuning.SurfaceSpacingScale;
            position = leader.transform.position - forward * astern;
            rotation = Quaternion.LookRotation(forward, Vector3.up);

            // Spawn at rest until a surface controller supplies a task.
            velocity = Vector3.zero;
            return true;
        }
    }
}
