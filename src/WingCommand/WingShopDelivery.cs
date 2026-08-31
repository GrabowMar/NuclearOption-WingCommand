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
    /// Requisitioned aircraft always launch from a friendly airbase and fly to the wing
    /// under their own power. There was once a paid "fast delivery" that materialised the
    /// aircraft on the player's wing instead; it was removed because a surcharge that skips
    /// the transit is not a decision worth making — it only bought away the one part of a
    /// requisition that reads as the aircraft actually coming from somewhere.
    /// </summary>
    internal static class WingShopDelivery
    {
        /// <summary>Height above the airbase that a delivery joins the circuit at.</summary>
        private const float CircuitAltitude = 1200f;

        /// <summary>Metres of clearance kept above terrain and sea.</summary>
        private const float TerrainClearance = 120f;

        /// <summary>
        /// How long a hangar delivery has to produce an aircraft before it is written off.
        /// Generous: a carrier hangar runs a door sequence before it spawns anything.
        /// </summary>
        private const float HangarDeliveryTimeout = 60f;

        /// <summary>
        /// Put a requisitioned airframe into the world, from a hangar where the airbase can
        /// manage it and from the circuit overhead where it cannot.
        ///
        /// The hangar path is the game's own: <c>Airbase.TrySpawnAircraft</c> picks a hangar
        /// by priority, respects occupancy and the clearance the airframe needs, runs a
        /// carrier's door sequence, and lets the stock AI taxi out and take off. That is a
        /// great deal more than this mod could reproduce, and it means a requisition arrives
        /// the way the faction's own aircraft do rather than materialising in mid-air. The
        /// roster records it as departing immediately; command waits until it is airborne.
        ///
        /// It cannot always be used: a hangar stocks specific types, so an airframe the
        /// mission does have in supply may still have nowhere on the field to come from. The
        /// circuit spawn stays as the fallback for exactly that case.
        ///
        /// The catch is that the airbase call returns permission, not an aircraft — the
        /// spawn can be several seconds away. The faction's own <c>onRegisterUnit</c> event
        /// is what closes that gap.
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

            if (TryHangarDelivery(transaction, leader, hq, loadout)) return true;

            Aircraft spawned = Spawn(definition, leader, hq, loadout);
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
            WingCommandManager.Instance?.QueueRecruit(spawned);
            return true;
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
                    "[Shop] could not build the " + WingLoadoutCatalog.Label(choice.Preset) +
                    " loadout; using the standard fit: " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ hangar path

        private sealed class PendingDelivery
        {
            public WingShop.PurchaseTransaction Transaction;
            public Airbase Origin;
            public Hangar Hangar;
            public float RequestedAt;
            public float ExpiresAt;
            public bool Starting;
            public readonly List<Aircraft> EarlyCandidates = new List<Aircraft>();
        }

        private static readonly List<PendingDelivery> pending = new List<PendingDelivery>();
        private static FactionHQ watched;

        private static bool TryHangarDelivery(WingShop.PurchaseTransaction transaction,
                                              Aircraft leader, FactionHQ hq, Loadout loadout)
        {
            if (hq == null || leader == null) return false;

            AircraftDefinition definition = transaction.Definition;

            // The nearest airbase is not necessarily one that can produce this airframe — a
            // hangar stocks specific types — so take the closest field that can.
            Airbase airbase = NearestHangarFor(definition, hq, leader.transform.position);
            if (airbase == null) return false;

            AircraftParameters p = definition.aircraftParameters;
            float fuel = p != null ? p.DefaultFuelLevel : 1f;
            LiveryKey livery = p != null && hq.faction != null
                ? new LiveryKey(p.GetRandomLiveryForFaction(hq.faction))
                : leader.NetworkLiveryKey;

            // Watch before spawning: a hangar with its doors already open spawns immediately,
            // and subscribing afterwards would miss it. The order is held by reference rather
            // than by index for the same reason — by the time the call returns, the capture
            // may already have claimed and removed it.
            var order = new PendingDelivery
            {
                Transaction = transaction,
                Origin = airbase,
                RequestedAt = Time.unscaledTime,
                ExpiresAt = Time.unscaledTime + HangarDeliveryTimeout,
                Starting = true,
            };

            Watch(hq);
            pending.Add(order);

            Airbase.TrySpawnResult result;
            int stockBeforeNative = hq.GetUnitSupply(definition);
            try
            {
                // A null loadout makes the hangar fit the airframe's own AI weapon selection,
                // which is what the faction's aircraft launch with. A requisition with a
                // preset hands the field the fit the player asked for instead; the hangar
                // arms it on the ramp exactly as it arms the faction's own aircraft.
                result = airbase.TrySpawnAircraft(null, definition, livery, loadout, fuel);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Shop] hangar delivery threw, falling back: " + e.Message);
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

            order.Hangar = result.Hangar;

            // An immediate hangar spawn registers inside TrySpawnAircraft, before it returns
            // the native Hangar identifier. Re-evaluate only after that identifier is known.
            for (int i = 0; i < order.EarlyCandidates.Count; i++)
                TryClaim(order.EarlyCandidates[i]);

            if (!result.Allowed)
            {
                pending.Remove(order);
                if (pending.Count == 0) Watch(null);
                return false;
            }

            Plugin.Logger.LogInfo(
                "[Shop] " + definition.unitName + " ordered from a hangar at " + airbase.name +
                " with the " + WingLoadoutCatalog.Label(definition, transaction.Loadout) + " fit" +
                (result.DelayedSpawn ? " (waiting on hangar doors)" : ""));
            return true;
        }

        /// <summary>The closest friendly field with a hangar that stocks this airframe.</summary>
        private static Airbase NearestHangarFor(AircraftDefinition definition, FactionHQ hq,
                                                Vector3 from)
        {
            Airbase best = null;
            float bestSq = float.MaxValue;

            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || !airbase.CanSpawnAircraft(definition)) continue;

                float sq = (airbase.transform.position - from).sqrMagnitude;
                if (sq >= bestSq) continue;

                bestSq = sq;
                best = airbase;
            }

            return best;
        }

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

            pending.Remove(match);
            if (pending.Count == 0) Watch(null);

            if (!match.Transaction.Commit(aircraft)) return;
            WingCommandManager.Instance?.QueueRecruit(aircraft);
            Plugin.Logger.LogInfo("[Shop] " + aircraft.unitName +
                                  " registered from " + match.Origin.name +
                                  "; rostered, awaiting airborne activation");
        }

        private static bool Matches(PendingDelivery order, Aircraft aircraft)
        {
            if (order == null || order.Starting || order.Hangar == null ||
                order.Transaction == null || aircraft == null)
                return false;
            if (order.Transaction.Definition != aircraft.definition) return false;
            if (aircraft.NetworkHQ != order.Transaction.Hq) return false;
            if (aircraft.NetworkspawningHangar != order.Hangar) return false;
            if (Time.unscaledTime + 0.01f < order.RequestedAt) return false;

            Transform spawn = order.Hangar.GetSpawnTransform();
            return spawn == null || (aircraft.transform.position - spawn.position).sqrMagnitude <=
                   1000f * 1000f;
        }

        /// <summary>Write off orders the field never produced, so nothing waits for ever.</summary>
        public static void Tick()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < pending[i].ExpiresAt) continue;

                Plugin.Logger.LogWarning(
                    "[Shop] hangar delivery of " +
                    pending[i].Transaction.Definition.unitName + " never arrived");
                bool restored = pending[i].Transaction.Rollback("hangar delivery timed out");
                WingCommandManager.Instance?.Toast(
                    pending[i].Transaction.Definition.unitName +
                    (restored
                        ? " delivery failed - funds and stock restored"
                        : " delivery failed - refund is retrying"));
                pending.RemoveAt(i);
            }

            if (pending.Count == 0 && watched != null) Watch(null);
        }

        public static void Reset()
        {
            for (int i = 0; i < pending.Count; i++)
                pending[i].Transaction?.Rollback("mission reset");
            pending.Clear();
            Watch(null);
        }

        // ----------------------------------------------------------------- circuit path

        private static Aircraft Spawn(AircraftDefinition definition, Aircraft leader,
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

            Vector3 position;
            Quaternion rotation;
            Vector3 velocity;

            if (!BasePlacement(definition, leader, hq, out position, out rotation, out velocity))
                return null;

            // The airframe's own standard loadout and livery, which is what "default
            // equipment" means and what the faction's own AI aircraft launch with.
            AircraftParameters p = definition.aircraftParameters;
            float fuel = p != null ? p.DefaultFuelLevel : 1f;

            LiveryKey livery = leader.NetworkLiveryKey;
            if (p != null && hq != null && hq.faction != null)
                livery = new LiveryKey(p.GetRandomLiveryForFaction(hq.faction));

            try
            {
                return spawner.SpawnAircraft(
                    player: null,
                    prefab: prefab,
                    // Null unless the player requisitioned a preset. Aircraft initialisation
                    // substitutes the airframe's own standard loadout when this is null,
                    // whereas handing over an existing object shares one mutable loadout
                    // between aircraft - which once left a whole spawned wing with no
                    // ammunition and sent all of them straight home Winchester. Every
                    // requisition therefore gets a freshly built container of its own.
                    loadout: loadout,
                    fuelLevel: fuel,
                    livery: livery,
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
        /// In the circuit over the nearest friendly airbase, pointed at the player. It is
        /// recruited immediately and flies to its slot under its own power.
        /// </summary>
        private static bool BasePlacement(AircraftDefinition definition, Aircraft leader,
                                          FactionHQ hq, out Vector3 position,
                                          out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;

            Airbase airbase = hq != null ? hq.GetNearestAirbase(leader.transform.position) : null;
            if (airbase == null)
            {
                Plugin.Logger.LogWarning("[Shop] no friendly airbase for base delivery");
                return false;
            }

            Vector3 field = airbase.transform.position;

            Vector3 toLeader = leader.transform.position - field;
            toLeader.y = 0f;
            if (toLeader.sqrMagnitude < 1f) toLeader = leader.transform.forward;
            toLeader.Normalize();

            position = ClearOfGround(field + Vector3.up * CircuitAltitude);
            rotation = Quaternion.LookRotation(toLeader, Vector3.up);

            AircraftParameters p = definition.aircraftParameters;
            float cruise = p != null ? Mathf.Max(p.landingSpeed * 1.6f, 80f) : 120f;
            velocity = toLeader * cruise;

            return true;
        }

        /// <summary>Keep the spawn point clear of terrain and sea.</summary>
        private static Vector3 ClearOfGround(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 3000f, Vector3.down,
                                out RaycastHit hit, 6000f, PhysicsLayers.StaticsMask))
            {
                position.y = Mathf.Max(position.y, hit.point.y + TerrainClearance);
            }

            position.y = Mathf.Max(position.y, Datum.LocalSeaY + TerrainClearance);
            return position;
        }
    }
}
