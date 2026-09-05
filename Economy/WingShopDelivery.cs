using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Where a purchased aircraft appears, and the spawn call itself.
    ///
    /// Requisitioned aircraft launch from a hangar or helipad at an allowed friendly
    /// field that stocks the airframe. Only-nearest pins to the closest of those and
    /// queues if every pad there is busy. Any takes the closest pad that can launch
    /// right now, and stays unpinned until one can. Surface hulls still arrive astern
    /// of the player; they have no hangar to come from.
    /// </summary>
    internal static class WingShopDelivery
    {
        private static readonly List<Airbase> fieldScratch = new List<Airbase>();

        /// <summary>
        /// Put a requisitioned airframe into the world from a hangar or helipad, or queue
        /// it at the nearest field that can produce it.
        ///
        /// We select a clear compatible hangar, then the native hangar API runs a
        /// carrier's door sequence, and lets the stock AI taxi out and take off. The catch
        /// is that the airbase call returns permission, not an aircraft — the spawn can be
        /// several seconds away. The faction's own <c>onRegisterUnit</c> event closes that
        /// gap. Until a hangar has actually taken the order, the roster shows QUE.
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

            if (TryHangarDelivery(transaction, leader, hq, loadout, out reason)) return true;

            if (string.IsNullOrEmpty(reason))
                reason = "No hangar or helipad that can launch this airframe";
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

        // ------------------------------------------------------------------ hangar path

        internal sealed class PendingDelivery
        {
            public WingShop.PurchaseTransaction Transaction;
            public Airbase Origin;
            public Loadout Loadout;
            public LiveryKey Livery;
            public float Fuel;
            public Hangar Hangar;
            public float RequestedAt;
            public float ExpiresAt;
            public float NextAttemptAt;
            public bool Starting;
            /// <summary>
            /// True when this order must wait at <see cref="Origin"/> even if another field
            /// is idle. Any-mode orders stay unpinned (<see cref="Origin"/> may be null)
            /// until a pad actually accepts them.
            /// </summary>
            public bool Pinned;
            public readonly List<Aircraft> EarlyCandidates = new List<Aircraft>();

            public AircraftDefinition Definition => Transaction?.Definition;
            public string AirframeName => Definition != null ? Definition.unitName : "Airframe";
            public string StatusCode => HangarFieldPolicy.StatusCode(Hangar != null);
        }

        private static readonly List<PendingDelivery> pending = new List<PendingDelivery>();
        private static FactionHQ watched;

        public static int PendingCount => pending.Count;
        public static PendingDelivery GetPending(int index) => (index >= 0 && index < pending.Count) ? pending[index] : null;

        public static bool CancelPending(PendingDelivery order)
        {
            if (order == null || !pending.Contains(order)) return false;
            FailDelivery(order, "cancelled by player");
            return true;
        }

        /// <summary>
        /// Order a hangar delivery from an allowed field that stocks this airframe.
        /// Only-nearest pins and queues at the closest such field. Any launches from the
        /// closest free pad, or waits unpinned until one is free.
        /// </summary>
        private static bool TryHangarDelivery(WingShop.PurchaseTransaction transaction,
                                              Aircraft leader, FactionHQ hq, Loadout loadout,
                                              out string reason)
        {
            reason = null;
            if (hq == null || leader == null)
            {
                reason = "No hangar or helipad that can launch this airframe";
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
                reason = "No hangar or helipad that can launch this airframe";
                return false;
            }

            HangarLaunchMode mode = WingLaunchFields.Mode;
            int index = SelectOrigin(definition, from, mode);
            bool pin = mode == HangarLaunchMode.OnlyNearest;
            Airbase airbase = index >= 0 ? fieldScratch[index] : null;
            if (pin && airbase == null)
            {
                reason = "No hangar or helipad that can launch this airframe";
                return false;
            }

            AircraftParameters p = definition.aircraftParameters;
            float fuel = WingShop.SpawnFuelFor(definition);

            LiveryKey? livery = null;
            int liveryIdx = WingLoadoutTemplates.GetLiveryIndex(definition);
            List<WingLoadoutTemplates.LiveryOption> options = WingLoadoutTemplates.GetLiveries(definition, hq.faction);
            if (liveryIdx > 0 && liveryIdx < options.Count)
            {
                livery = options[liveryIdx].Key;
            }

            LiveryKey finalLivery = livery.HasValue
                ? livery.Value
                : (p != null && hq.faction != null
                    ? new LiveryKey(p.GetRandomLiveryForFaction(hq.faction))
                    : leader.NetworkLiveryKey);

            var order = new PendingDelivery
            {
                Transaction = transaction,
                Origin = airbase,
                Loadout = loadout,
                Livery = finalLivery,
                Fuel = fuel,
                RequestedAt = Time.unscaledTime,
                ExpiresAt = Time.unscaledTime + WingTuning.HangarDeliveryTimeout,
                Pinned = pin,
            };

            Watch(hq);
            pending.Add(order);

            if (order.Origin != null && CanLaunchNow(order.Origin, definition))
                AttemptNativeSpawn(order);
            else
                order.NextAttemptAt = Time.unscaledTime + WingTuning.HangarRetryInterval;

            string fieldName = order.Origin != null ? order.Origin.name : "an allowed field";
            Plugin.Logger.LogInfo(order.Hangar != null
                ? "[Shop] " + definition.unitName + " ordered from a hangar at " + fieldName +
                  " with the " + WingLoadoutCatalog.Label(transaction.Loadout) + " fit"
                : "[Shop] " + definition.unitName + " queued for a hangar at " + fieldName +
                  (pin ? " — every hangar there is busy"
                       : " — waiting for any free allowed pad"));
            return true;
        }

        /// <summary>
        /// One attempt to hand this order to its target airbase. The order stays in
        /// <see cref="pending"/> either way: a claimed hangar waits on its door sequence via
        /// <see cref="OnUnitRegistered"/>, a refused one waits here for <see cref="Tick"/> to
        /// retry once a hangar there frees up.
        /// </summary>
        private static void AttemptNativeSpawn(PendingDelivery order)
        {
            if (order == null || order.Origin == null || order.Transaction == null) return;

            FactionHQ hq = order.Transaction.Hq;
            AircraftDefinition definition = order.Transaction.Definition;
            if (hq == null || definition == null) return;

            Hangar selected = SelectClearHangar(order.Origin, definition);
            if (selected == null) return;
            if (!HangarDepartureLane.Reserve(order.Origin, selected)) return;
            // Reserve the exact pad before calling native code; synchronous registration
            // can happen inside this call, and adjacent delayed spawns need spacing too.
            order.Hangar = selected;

            order.Starting = true;
            Airbase.TrySpawnResult result;
            int stockBeforeNative = hq.GetUnitSupply(definition);
            try
            {
                // A null loadout makes the hangar fit the airframe's own AI weapon selection,
                // which is what the faction's aircraft launch with. A requisition with a
                // preset hands the field the fit the player asked for instead; the hangar
                // arms it on the ramp exactly as it arms the faction's own aircraft.
                result = selected.TrySpawnAircraft(null, definition, order.Livery,
                                                       order.Loadout, order.Fuel);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Shop] hangar delivery threw, will retry: " + e.Message);
                result = default(Airbase.TrySpawnResult);
            }
            finally
            {
                // Hangar.TrySpawnAircraft charges faction supply itself when player is null.
                // The purchase transaction already reserved its exact source, so retain only
                // that debit and compensate the hangar's otherwise duplicate charge.
                int stockAfterNative = hq.GetUnitSupply(definition);
                if (stockAfterNative < stockBeforeNative)
                    hq.AddSupplyUnit(definition, stockBeforeNative - stockAfterNative);
                order.Starting = false;
            }

            if (!result.Allowed)
            {
                order.Hangar = null;
                HangarDepartureLane.Release(order.Origin);
                return;
            }

            order.Hangar = result.Hangar;
            Plugin.Logger.LogInfo("[Shop] launch accepted: " + definition.unitName +
                " at " + order.Origin.name + "/" + result.Hangar.name +
                " position=" + result.Hangar.GetSpawnTransform().position.ToGlobalPosition());
            // A hangar has now actually taken the order; give it its own door-sequence budget
            // rather than whatever was left of the time this order spent queued.
            order.ExpiresAt = Time.unscaledTime + WingTuning.HangarDeliveryTimeout;

            // If an immediate hangar spawn produced an object on the hangar, claim it directly.
            GameObject immediateSpawn = GameAccess.GetHangarSpawnedObject(result.Hangar);
            if (immediateSpawn != null)
            {
                Aircraft direct = immediateSpawn.GetComponent<Aircraft>();
                if (direct != null) TryClaim(direct);
            }

            // An immediate hangar spawn registers inside TrySpawnAircraft, before it returns
            // the native Hangar identifier. Re-evaluate only after that identifier is known.
            for (int i = 0; i < order.EarlyCandidates.Count; i++)
                TryClaim(order.EarlyCandidates[i]);
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
                                        HangarLaunchMode mode)
        {
            return HangarFieldPolicy.SelectOrigin(
                fieldScratch.Count,
                mode,
                i => (fieldScratch[i].transform.position - from).sqrMagnitude,
                i => WingLaunchFields.IsAllowed(fieldScratch[i]),
                i => CanEverProduce(fieldScratch[i], definition),
                i => CanLaunchNow(fieldScratch[i], definition));
        }

        /// <summary>Native hangar availability is the only dispatch gate.</summary>
        private static bool CanLaunchNow(Airbase airbase, AircraftDefinition definition) =>
            airbase != null && !airbase.disabled &&
            HangarDepartureLane.IsFree(airbase) &&
            SelectClearHangar(airbase, definition) != null;

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
                if (!hangar.CanSpawnAircraft(definition)) continue;
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

        /// <summary>
        /// Whether this field has a hangar or helipad that stocks the airframe, regardless
        /// of whether that pad is free this frame.
        ///
        /// <c>Airbase.GetAvailableAircraft</c> tracks what can launch <i>right now</i>, so
        /// a busy door sequence made the nearest field look unable to produce the type and
        /// the order either jumped to a farther idle pad or spawned in the circuit. The
        /// hangar's own type list is the editor-configured stock and does not blink off
        /// while the pad is occupied.
        /// </summary>
        private static bool CanEverProduce(Airbase airbase, AircraftDefinition definition) =>
            WingLaunchFields.CanProduce(airbase, definition);

        private static void Watch(FactionHQ hq)
        {
            if (watched == hq) return;
            if (watched != null) watched.onRegisterUnit -= OnUnitRegistered;
            watched = hq;
            if (watched != null) watched.onRegisterUnit += OnUnitRegistered;
        }

        /// <summary>Claim only the aircraft emitted by the native hangar that accepted the order.</summary>
        private static void OnUnitRegistered(Unit unit)
        {
            if (!(unit is Aircraft aircraft) || aircraft.Player != null) return;

            // Immediate spawns can arrive before TrySpawnAircraft returns its Hangar. Keep
            // only candidates from the requested origin, then require the exact native spawn
            // identifier once the call returns.
            for (int i = 0; i < pending.Count; i++)
            {
                PendingDelivery order = pending[i];
                if (!order.Starting || order.Transaction.Definition != aircraft.definition)
                    continue;
                Hangar spawningHangar = aircraft.NetworkspawningHangar;
                if (spawningHangar == null || spawningHangar.parentAirbase != order.Origin) continue;
                order.EarlyCandidates.Add(aircraft);
            }

            TryClaim(aircraft);
        }

        private static void TryClaim(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.Player != null || aircraft.NetworkspawningHangar == null)
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

            HangarDepartureLane.Track(match.Origin, aircraft);
            pending.Remove(match);
            if (pending.Count == 0) Watch(null);

            if (!match.Transaction.Commit(aircraft))
            {
                Plugin.Logger.LogError("[Shop] " + aircraft.unitName +
                    " spawned from hangar but purchase commit failed; not recruiting");
                return;
            }
            try { aircraft.SetLiveryKey(match.Livery); } catch { }
            WingCommandManager.Instance?.QueueRecruit(aircraft, match.Transaction?.Pilot);
            Plugin.Logger.LogInfo("[Shop] " + aircraft.unitName +
                                  " registered from " + match.Origin.name +
                                  "; handed to wing recruit queue");
        }

        private static bool Matches(PendingDelivery order, Aircraft aircraft)
        {
            if (order == null || order.Starting || order.Hangar == null ||
                order.Transaction == null || aircraft == null)
                return false;
            if (order.Transaction.Definition != aircraft.definition) return false;
            if (aircraft.NetworkHQ != order.Transaction.Hq) return false;
            GameObject spawnedObject = GameAccess.GetHangarSpawnedObject(order.Hangar);
            if (spawnedObject != null && spawnedObject == aircraft.gameObject) return true;
            if (aircraft.NetworkspawningHangar != order.Hangar) return false;
            if (Time.unscaledTime + 0.01f < order.RequestedAt) return false;

            Transform spawn = order.Hangar.GetSpawnTransform();
            return spawn == null || (aircraft.transform.position - spawn.position).sqrMagnitude <=
                   1000f * 1000f;
        }

        /// <summary>
        /// Advance every open order: retry a queued one against its target airbase, and write
        /// off ones that can never arrive, so nothing waits forever.
        ///
        /// Oldest-first, since <see cref="pending"/> is append-order: when a hangar frees up,
        /// whichever purchase queued for it first gets it, the same way a real flight line
        /// works through a backlog rather than serving whoever asks last.
        /// </summary>
        public static void Tick()
        {
            HangarDepartureLane.Tick();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.Hangar != null)
                {
                    GameObject spawnedObject = GameAccess.GetHangarSpawnedObject(order.Hangar);
                    if (spawnedObject != null)
                    {
                        Aircraft direct = spawnedObject.GetComponent<Aircraft>();
                        if (direct != null && Matches(order, direct))
                        {
                            TryClaim(direct);
                            continue;
                        }
                    }
                }
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.Hangar != null || order.Starting) continue;

                bool stillPossible = order.Pinned
                    ? CanEverProduce(order.Origin, order.Transaction.Definition)
                    : AnyAllowedCanProduce(order);
                if (stillPossible) continue;

                string where = order.Origin != null ? order.Origin.name : "allowed fields";
                Plugin.Logger.LogWarning(
                    "[Shop] " + order.Transaction.Definition.unitName + " - " +
                    where + " can no longer produce it");
                FailDelivery(order, "airbase can no longer produce this aircraft");
            }

            // FIFO retry: oldest queued order first. Occupancy-gated and throttled so a
            // busy hangar is not hammered every frame.
            float now = Time.unscaledTime;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingDelivery order = pending[i];
                if (order.Hangar != null || order.Starting) continue;
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

                AttemptNativeSpawn(order);
                // Immediate registration removes the current order from pending.
                // Visit the next oldest order instead of skipping its shifted index.
                if (!pending.Contains(order)) { i--; continue; }
                if (order.Hangar == null)
                {
                    if (!order.Pinned) order.Origin = null;
                    order.NextAttemptAt = Time.unscaledTime + WingTuning.HangarRetryInterval;
                }
            }

            now = Time.unscaledTime;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery order = pending[i];
                if (order.Hangar != null)
                {
                    if (now < order.ExpiresAt) continue;
                    Plugin.Logger.LogWarning(
                        "[Shop] hangar delivery of " +
                        order.Transaction.Definition.unitName + " never arrived");
                    FailDelivery(order, "hangar delivery timed out");
                    continue;
                }

                if (now < order.RequestedAt + WingTuning.HangarDeliveryTimeout) continue;
                Plugin.Logger.LogWarning(
                    "[Shop] hangar delivery of " +
                    order.Transaction.Definition.unitName + " waited too long for a pad");
                FailDelivery(order, "hangar delivery timed out");
            }

            if (pending.Count == 0 && watched != null) Watch(null);
        }

        private static void FailDelivery(PendingDelivery order, string reason)
        {
            HangarDepartureLane.Release(order?.Origin);
            bool restored = order.Transaction.Rollback(reason);
            WingCommandManager.Instance?.Toast(
                order.Transaction.Definition.unitName +
                (restored
                    ? " delivery failed - funds and stock restored"
                    : " delivery failed - refund is retrying"));
            pending.Remove(order);
        }

        public static void Reset()
        {
            HangarDepartureLane.Reset();
            DeliveryTaxiRouteGuard.Reset();
            for (int i = 0; i < pending.Count; i++)
                pending[i].Transaction?.Rollback("mission reset");
            pending.Clear();
            fieldScratch.Clear();
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

            AircraftParameters p = definition.aircraftParameters;
            float fuel = WingShop.SpawnFuelFor(definition);

            LiveryKey? livery = null;
            int liveryIdx = WingLoadoutTemplates.GetLiveryIndex(definition);
            List<WingLoadoutTemplates.LiveryOption> options = WingLoadoutTemplates.GetLiveries(definition, hq != null ? hq.faction : null);
            if (liveryIdx > 0 && liveryIdx < options.Count)
            {
                livery = options[liveryIdx].Key;
            }

            LiveryKey finalLivery = livery.HasValue
                ? livery.Value
                : (p != null && hq != null && hq.faction != null
                    ? new LiveryKey(p.GetRandomLiveryForFaction(hq.faction))
                    : leader.NetworkLiveryKey);

            try
            {
                return spawner.SpawnAircraft(
                    player: null,
                    prefab: prefab,
                    loadout: loadout,
                    fuelLevel: fuel,
                    livery: finalLivery,
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
