using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Where a purchased aircraft appears, and the spawn call itself.
    ///
    /// Three routes, because the game has three answers. A helicopter or tiltwing comes out
    /// of a hangar or helipad exactly as the faction's own do — <c>Hangar.TrySpawnAircraft</c>
    /// puts it on the pad and <c>AIHeloTakeoffState</c> lifts it off, and there is nothing
    /// for this mod to improve on. A surface hull has no hangar at all and arrives astern of
    /// the player. A fixed-wing aircraft — including the VTOL jets, which are
    /// <c>PilotType.Plane</c> and taxi like anything else — is put on the takeoff threshold
    /// with <c>Spawner.SpawnAircraft</c> and <c>spawningHangar</c> left null.
    ///
    /// That last one is the whole point of this file. Requisitioning a jet into a hangar and
    /// letting the stock taxi state drive it out is the obvious implementation and it does
    /// not work: a shelter faces wherever the mission author pointed it, the taxi state locks
    /// the nosewheel for the first ten metres and then lets <c>AutoAim</c> bank toward a
    /// taxiway join that may be a hundred metres off the nose, and the aircraft leaves the
    /// pavement, tilts past three degrees and ejects. Spawning on the strip skips the part
    /// the game is bad at and keeps the parts it is good at.
    ///
    /// Nothing here moves an aircraft after it exists. The pose is chosen before the spawn
    /// call and never revised — see <c>docs/airfield-findings.md</c> for the eight attempts
    /// that establish why.
    /// </summary>
    internal static class WingShopDelivery
    {
        private static readonly List<Airbase> fieldScratch = new List<Airbase>();

        // ------------------------------------------------------------------- dispatch

        /// <summary>
        /// Put a requisitioned airframe into the world, or queue it at the nearest allowed
        /// field that can produce it.
        ///
        /// A queued order stays in <see cref="pending"/> and shows as QUE on the roster; one
        /// a field has taken shows as DEPT until its aircraft registers.
        /// </summary>
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
                WingCommandManager.Instance?.QueueRecruit(spawned, transaction.Pilot);
                return true;
            }

            if (TryFieldDelivery(transaction, leader, hq, loadout, out reason)) return true;

            if (string.IsNullOrEmpty(reason))
                reason = "No field that can launch this airframe";
            return false;
        }

        /// <summary>
        /// Fit the chosen preset, never letting a bad fit stop a delivery.
        ///
        /// A null result means "use the airframe's own standard equipment", which is both
        /// the Standard preset's meaning and the safe answer to every failure. A requisition
        /// that refused to arrive because a preset could not be built would be a far worse
        /// outcome than one that arrives configured as the faction's own aircraft are.
        /// </summary>
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

        /// <summary>
        /// Whether this airframe departs vertically from a pad rather than taxiing.
        ///
        /// Asked of the prefab's own <c>Pilot.pilotType</c>, because that is the field
        /// <c>Pilot.SetStartingAiState</c> branches on — <c>Helo</c> and <c>Tiltwing</c> get
        /// <c>AIHeloTakeoffState</c>, everything else gets <c>AIPilotTaxiState</c>. The
        /// distinction matters most for the airframes it is easiest to get wrong: a VT-7 or
        /// an FS-20 lands vertically and hovers, and is still <c>PilotType.Plane</c>, so it
        /// wants a runway. <see cref="WingShop.IsRotary"/> is the fallback for a prefab with
        /// no reachable pilot component; it agrees, by way of the autopilot type.
        /// </summary>
        private static bool LaunchesVertically(AircraftDefinition definition)
        {
            if (definition == null) return false;
            if (verticalCache.TryGetValue(definition, out bool cached)) return cached;

            bool vertical = WingShop.IsRotary(definition);
            GameObject prefab = definition.unitPrefab;
            if (prefab != null)
            {
                Pilot pilot = prefab.GetComponentInChildren<Pilot>(includeInactive: true);
                if (pilot != null)
                    vertical = pilot.pilotType == Pilot.PilotType.Helo ||
                               pilot.pilotType == Pilot.PilotType.Tiltwing;
            }

            verticalCache[definition] = vertical;
            return vertical;
        }

        private static readonly Dictionary<AircraftDefinition, bool> verticalCache =
            new Dictionary<AircraftDefinition, bool>();

        /// <summary>
        /// The preset the game would have picked for itself, chosen safely.
        ///
        /// Index one is what <c>Aircraft.OnStartClient</c> reaches for, so it is the right
        /// answer where it exists; anything shorter falls back to the first preset rather
        /// than letting the engine index past the end of the array.
        /// </summary>
        private static Loadout DefaultLoadout(AircraftDefinition definition)
        {
            List<Loadout> loadouts = definition?.aircraftParameters?.loadouts;
            if (loadouts == null || loadouts.Count == 0) return null;
            return loadouts.Count > 1 ? loadouts[1] : loadouts[0];
        }

        // ------------------------------------------------------------------ pending order

        internal sealed class PendingDelivery
        {
            public WingShop.PurchaseTransaction Transaction;
            public Airbase Origin;
            public Loadout Loadout;
            public LiveryKey Livery;
            public float Fuel;

            /// <summary>The pad that took the order. Null for a runway departure.</summary>
            public Hangar Hangar;

            /// <summary>True once a field has taken the order, whichever route it took.</summary>
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

            /// <summary>
            /// True when this order must wait at <see cref="Origin"/> even if another field
            /// is idle. Any-mode orders stay unpinned (<see cref="Origin"/> may be null)
            /// until a field actually accepts them.
            /// </summary>
            public bool Pinned;

            /// <summary>Vertical departures take a pad; the rest take a runway threshold.</summary>
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

        /// <summary>The order currently inside a hangar's native spawn call, if any.</summary>
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

        /// <summary>
        /// Why a requisition cannot be launched right now, or null when it can.
        ///
        /// Read by the shop before it takes any money, so the answer has to be about
        /// capability rather than about this frame's occupancy — a busy field is a queue,
        /// not a refusal.
        /// </summary>
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

        // --------------------------------------------------------------- field selection

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
                // Only-nearest still pins even when every pad there is busy: the order
                // queues at that field rather than jumping to a distant one.
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

        /// <summary>
        /// Whether this field could ever launch the airframe.
        ///
        /// Stock is read from the hangars' own editor-configured type lists rather than from
        /// <c>Airbase.GetAvailableAircraft</c>, which tracks what can launch <i>right now</i>
        /// and therefore makes a field with a busy door look as though it cannot produce the
        /// type at all. A fixed-wing airframe additionally needs somewhere to roll: a field
        /// that stocks jets but has only helipads and no takeoff strip is not a launch site
        /// for one.
        /// </summary>
        private static bool CanEverProduce(Airbase airbase, AircraftDefinition definition)
        {
            if (!WingLaunchFields.CanProduce(airbase, definition)) return false;
            if (LaunchesVertically(definition)) return true;
            return WingAirfield.HasTakeoffRunway(airbase, definition);
        }

        /// <summary>
        /// Whether this field can take the order this frame.
        ///
        /// One departure per field, not per pad. A jet is put on the takeoff threshold, and
        /// every aircraft at a field shares that strip however many hangars it has.
        /// </summary>
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

        // ------------------------------------------------------------------ spawn attempt

        private static void Attempt(PendingDelivery order)
        {
            if (order?.Origin == null || order.Transaction == null) return;
            if (order.Vertical) AttemptPadSpawn(order);
            else AttemptRunwaySpawn(order);
        }

        /// <summary>
        /// Put a fixed-wing requisition on the takeoff threshold.
        ///
        /// <c>spawningHangar</c> is deliberately null. It is a SyncVar that makes every
        /// client snap the aircraft's transform and rigidbody onto the named pad in
        /// <c>OnStartClient</c>, so pointing it at a hangar the aircraft is not standing in
        /// teleports it there for everyone but the host.
        /// </summary>
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
            Vector3 from = leader != null
                ? leader.transform.position : order.Origin.transform.position;

            if (!WingAirfield.TryBuildLaunchPose(order.Origin, definition, from,
                                                 out WingAirfield.LaunchPose pose))
                return;

            // Aircraft.OnStartClient substitutes aircraftParameters.loadouts[1] for a null or
            // empty loadout, by hardcoded index and with no bounds check. A hangar spawn is
            // saved from that by the weapon selection the pad runs afterwards; this path is
            // not, so an airframe with fewer than two presets would throw inside the spawn.
            Loadout loadout = order.Loadout ?? DefaultLoadout(definition);

            // The threshold, not the field centre: the lane is released when the aircraft has
            // moved clear of where it was put down, and a field centre can be further from
            // its own runway than that clearance.
            if (!HangarDepartureLane.Reserve(order.Origin, pose.Threshold, order)) return;

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

            // ServerObjectManager.Spawn runs OnStartServer and OnStartClient synchronously, so
            // the pilot is already in AIPilotTaxiState on this line — there is nothing to
            // correct here and no point looking. What the watch is for is the question that
            // cannot be answered yet: whether the stock taxi state, given a few seconds and a
            // physics step or two, actually gets this aircraft moving.
            WingAirfield.WatchLaunch(spawned, pose);
            HangarDepartureLane.Track(order, spawned);

            Plugin.LogVerbose(
                "[Shop] " + definition.unitName + " launched from " +
                WingLaunchFields.DisplayName(order.Origin) + "/" +
                pose.Runway.GetName(pose.Reverse) + " at " + pose.Position +
                " (spawningHangar=null)");

            Claim(order, spawned);
        }

        /// <summary>
        /// Hand a vertical departure to a pad and let the stock door sequence run.
        ///
        /// The order stays in <see cref="pending"/> either way: a claimed pad waits on its
        /// doors via <see cref="OnUnitRegistered"/>, a refused one waits for <see cref="Tick"/>
        /// to retry once a pad frees up.
        /// </summary>
        private static void AttemptPadSpawn(PendingDelivery order)
        {
            FactionHQ hq = order.Transaction.Hq;
            AircraftDefinition definition = order.Transaction.Definition;
            if (hq == null || definition == null) return;

            Hangar selected = SelectClearHangar(order.Origin, definition);
            if (selected == null) return;
            if (!HangarDepartureLane.Reserve(order.Origin, selected, order)) return;

            // Reserve the exact pad before calling native code; registration can happen
            // synchronously inside that call.
            order.Hangar = selected;
            // Native Hangar keeps this field after the last aircraft leaves. It is not
            // evidence of a new delivery until a different aircraft replaces it.
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
                // TrySpawnAircraft charges faction supply itself when the player argument is
                // null. The purchase transaction has already reserved its exact source, so
                // retain that debit and compensate the hangar's otherwise duplicate charge.
                //
                // ModifyUnitSupply, deliberately, not AddSupplyUnit: the latter looks for a
                // player with an outstanding reserve request for this airframe first and
                // hands them the aircraft instead of restoring the count, returning before
                // it touches supply at all. That is right for an airframe genuinely coming
                // back into stock and wrong for undoing a double charge, which would then
                // both give a stranger a free plane and leave the faction one short.
                try
                {
                    int give = SupplyCompensation.Delta(stockBeforeNative,
                                                        hq.GetUnitSupply(definition));
                    if (give > 0) hq.ModifyUnitSupply(definition, give);
                }
                finally { order.Starting = false; }
            }

            // TrySpawnAircraft can throw after scheduling doors. Once the pad becomes busy it
            // may still emit an aircraft, so retain ownership even if the return value was
            // lost to the exception.
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

            // A pad has now actually taken the order; give it its own door-sequence budget
            // rather than whatever was left of the time this order spent queued.
            order.ExpiresAt = Time.unscaledTime + WingTuning.HangarDeliveryTimeout;

            GameObject immediateSpawn = GameAccess.GetHangarSpawnedObject(selected);
            if (immediateSpawn != null)
            {
                Aircraft direct = immediateSpawn.GetComponent<Aircraft>();
                if (direct != null) TryClaim(direct);
            }

            // Registration can precede the native call's return and the hangar field update.
            TryClaim(order.ObservedAircraft);
        }

        // ------------------------------------------------------------------------- claim

        private static void Watch(FactionHQ hq)
        {
            if (watched == hq) return;
            if (watched != null) watched.onRegisterUnit -= OnUnitRegistered;
            watched = hq;
            if (watched != null) watched.onRegisterUnit += OnUnitRegistered;
        }

        /// <summary>Claim only the aircraft emitted by the pad that accepted the order.</summary>
        private static void OnUnitRegistered(Unit unit)
        {
            if (!(unit is Aircraft aircraft)) return;

            // Bind registrations to the exact requested hangar even before its native
            // spawnedObject field is updated. A partial spawn must never be refunded.
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

        /// <summary>Settle the purchase and hand the airframe to the wing's recruit queue.</summary>
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

            // The wing takes the aircraft onto its roster now and takes command of it later,
            // once LaunchSafety says the stock departure is complete. Until then the
            // DeliveryHold reflex keeps every hand off the controls.
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

        // -------------------------------------------------------------------------- tick

        /// <summary>
        /// Advance every open order: retry a queued one against its target field, and write
        /// off ones that can never arrive, so nothing waits forever.
        ///
        /// Oldest-first, since <see cref="pending"/> is append-order: when a field frees up,
        /// whichever purchase queued for it first gets it, the way a flight line works
        /// through a backlog rather than serving whoever asks last.
        /// </summary>
        public static void Tick()
        {
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

            // FIFO retry: oldest queued order first, occupancy-gated and throttled so a busy
            // field is not hammered every frame.
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
                // An immediate claim removes the current order from pending. Visit the next
                // oldest order instead of skipping its shifted index.
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

        // ----------------------------------------------------------------- surface path

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

        /// <summary>
        /// Astern of the player, at a slot interval, on the player's own plane.
        /// A warship cannot be delivered into a hangar and taxied onto a runway.
        /// </summary>
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

            // Stationary. A hull under way from the first frame would be driving before
            // anything has told it where to go.
            velocity = Vector3.zero;
            return true;
        }
    }
}
