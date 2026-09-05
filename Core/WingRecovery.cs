using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Settles Return To Base as an idempotent sequence: capture facts, credit exactly one
    /// inventory destination, confirm network destruction, then release ownership and roster
    /// tracking. A failed stage remains pending and is retried instead of losing the aircraft
    /// between unrelated mutations.
    /// </summary>
    internal static class WingRecovery
    {
        private const float GroundHeight = 5f;
        private const float StoppedSpeed = 3f;
        private const float RetrySeconds = 1f;

        private sealed class Settlement
        {
            /// <summary>Null for a released aircraft, which is already off the roster.</summary>
            public WingRegistry Wing;

            /// <summary>Null for a released aircraft, which no longer has a member.</summary>
            public WingMember Member;

            /// <summary>The departure record to close out, for a released aircraft.</summary>
            public WingDeparture.Departing Departing;

            public Aircraft Aircraft;
            public PersistentID AircraftId;
            public AircraftDefinition Definition;
            public FactionHQ Hq;
            public string Name;
            public bool Owned;
            public bool LoadoutKnown;
            public WingLoadoutChoice Loadout;
            public bool InventoryCredited;
            public bool StoredInReserve;
            public bool DestroyConfirmed;
            public bool OwnershipTransferred;
            public bool RosterReleased;
            public bool Completed;
            public bool SortieNoted;
            public float RetryAt;
        }

        private static readonly List<Settlement> pending = new List<Settlement>();

        public static void Tick(WingRegistry wing)
        {
            if (wing == null) return;

            foreach (WingMember member in wing.Members)
                if (member.RefitPending && IsHome(member)) member.CompleteRefit();

            if (!Plugin.Settings.RtbReturnsToReserve.Value)
            {
                // Recovery is switched off, so nothing is credited or despawned. Released
                // aircraft still have to stop being tracked once they are down, or the
                // squadron count would go on excusing capacity they are still occupying.
                WingDeparture.Prune();
                IReadOnlyList<WingDeparture.Departing> landed = WingDeparture.Outbound;
                for (int i = landed.Count - 1; i >= 0; i--)
                    if (IsHome(landed[i].Aircraft)) WingDeparture.Forget(landed[i]);
                return;
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Settlement settlement = pending[i];
                Advance(settlement);
                if (settlement.Completed) pending.RemoveAt(i);
            }

            // Iterate backwards because a newly started settlement can complete immediately
            // and remove its member from the roster.
            for (int i = wing.Count - 1; i >= 0; i--)
            {
                WingMember member = wing.Members[i];
                if (member == null || member.RefitPending || member.Order != WingOrder.ReturnToBase) continue;
                if (IsPending(member) || !IsHome(member)) continue;

                Settlement settlement = Begin(wing, member);
                pending.Add(settlement);
                Advance(settlement);
                if (settlement.Completed) pending.Remove(settlement);
            }

            // Released aircraft settle on exactly the same terms, but they are no longer on
            // the roster to be found by the loop above.
            WingDeparture.Prune();
            IReadOnlyList<WingDeparture.Departing> outbound = WingDeparture.Outbound;
            for (int i = outbound.Count - 1; i >= 0; i--)
            {
                WingDeparture.Departing departing = outbound[i];
                if (IsPending(departing) || !IsHome(departing.Aircraft)) continue;

                Settlement settlement = Begin(departing);
                pending.Add(settlement);
                Advance(settlement);
                if (settlement.Completed) pending.Remove(settlement);
            }
        }

        /// <summary>Prune must not report a staged recovery as a combat loss between retries.</summary>
        public static bool IsPending(WingMember member)
        {
            if (member == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Member == member) return true;
            return false;
        }

        private static bool IsPending(WingDeparture.Departing departing)
        {
            if (departing == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Departing == departing) return true;
            return false;
        }

        public static void Reset() => pending.Clear();

        private static Settlement Begin(WingRegistry wing, WingMember member)
        {
            Aircraft aircraft = member.Aircraft;
            var settlement = new Settlement
            {
                Wing = wing,
                Member = member,
                Aircraft = aircraft,
                AircraftId = aircraft != null ? aircraft.persistentID : default(PersistentID),
                Definition = aircraft != null ? aircraft.definition : null,
                Hq = aircraft != null ? aircraft.NetworkHQ : null,
                Name = member.Name,
                Owned = WingShop.IsPurchased(aircraft),
                Loadout = member.Loadout,
                LoadoutKnown = member.LoadoutKnown,
            };

            WingComms.Say(member, WingComms.Call.Recovered);
            return settlement;
        }

        /// <summary>
        /// The same settlement for an aircraft the player released, which reached its base
        /// after leaving the roster. The facts were captured at the moment of release, while
        /// there was still a member to read them from.
        /// </summary>
        private static Settlement Begin(WingDeparture.Departing departing)
        {
            Aircraft aircraft = departing.Aircraft;
            return new Settlement
            {
                Departing = departing,
                Aircraft = aircraft,
                AircraftId = departing.AircraftId,
                Definition = aircraft != null ? aircraft.definition : null,
                Hq = aircraft != null ? aircraft.NetworkHQ : null,
                Name = departing.Name,
                Owned = departing.Owned,
                Loadout = departing.Loadout,
                LoadoutKnown = departing.LoadoutKnown,
            };
        }

        private static void Advance(Settlement settlement)
        {
            if (settlement == null || settlement.Completed) return;
            if (Time.unscaledTime < settlement.RetryAt) return;

            try
            {
                if (!settlement.SortieNoted)
                {
                    WingPilotRoster.NoteSortie(settlement.Aircraft);
                    settlement.SortieNoted = true;
                }

                if (!settlement.InventoryCredited && !CreditInventory(settlement))
                {
                    Retry(settlement, "no inventory destination available");
                    return;
                }

                // Retire the pilot and release native state while the aircraft reference is
                // still valid. If destruction then fails, this settlement remains the durable
                // owner of the aircraft and retries instead of abandoning an untracked frame.
                if (!settlement.RosterReleased)
                {
                    if (settlement.Member == null || settlement.Wing == null)
                    {
                        // A released aircraft left the roster when the player dismissed it,
                        // so there is nothing to retire here. Its native state was already
                        // handed back at that point.
                        settlement.RosterReleased = true;
                    }
                    else
                    {
                        settlement.Wing.Recover(settlement.Member);
                        settlement.RosterReleased = !settlement.Wing.Contains(settlement.Member);
                        if (!settlement.RosterReleased)
                        {
                            Retry(settlement, "roster release did not complete");
                            return;
                        }
                    }
                }

                if (!settlement.DestroyConfirmed)
                {
                    if (settlement.Aircraft == null ||
                        !UnitRegistry.TryGetUnit(settlement.AircraftId, out Unit tracked) ||
                        tracked == null)
                    {
                        settlement.DestroyConfirmed = true;
                    }
                    else
                    {
                        NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(
                            settlement.Aircraft.Identity,
                            !settlement.Aircraft.Identity.IsSceneObject);
                        // Treat the network registry as confirmation, not a void method
                        // returning. If destruction is deferred, retain and retry the staged
                        // settlement without transferring ownership in the meantime.
                        if (UnitRegistry.TryGetUnit(settlement.AircraftId, out tracked) &&
                            tracked != null)
                        {
                            Retry(settlement, "waiting for network destruction confirmation");
                            return;
                        }
                        settlement.DestroyConfirmed = true;
                    }
                }

                // Ownership transfers only after inventory credit and network destruction.
                if (!settlement.OwnershipTransferred)
                {
                    if (settlement.Owned) WingShop.TakePurchased(settlement.AircraftId);
                    settlement.OwnershipTransferred = true;
                }

                WingLoadoutBook.Forget(settlement.AircraftId);

                int stock = settlement.Hq != null && settlement.Definition != null
                    ? settlement.Hq.GetUnitSupply(settlement.Definition)
                    : 0;
                settlement.Completed = true;
                WingDeparture.Forget(settlement.Departing);
                WingCommandManager.Instance?.Toast(settlement.StoredInReserve
                    ? settlement.Name + " recovered to wing reserve (" + WingSupplyReserve.Count +
                      "/" + WingSupplyReserve.Capacity + ")"
                    : settlement.Name + " recovered to faction stock - wing reserve full");
                Plugin.Logger.LogInfo(
                    "[Recovery] " + settlement.Name + " recovered at base; " +
                    (settlement.Definition != null ? settlement.Definition.unitName : "airframe") +
                    (settlement.StoredInReserve
                        ? " stored in wing reserve"
                        : " faction stock now " + stock));
            }
            catch (Exception e)
            {
                Retry(settlement, e.GetType().Name + " - " + e.Message);
            }
        }

        private static bool CreditInventory(Settlement settlement)
        {
            if (settlement.Definition == null) return false;

            if (WingSupplyReserve.StoreRecovered(
                    settlement.Definition, settlement.Owned,
                    settlement.LoadoutKnown, settlement.Loadout, settlement))
            {
                settlement.StoredInReserve = true;
                settlement.InventoryCredited = true;
                return true;
            }

            if (settlement.Hq == null) return false;

            int before = settlement.Hq.GetUnitSupply(settlement.Definition);
            try
            {
                settlement.Hq.AddSupplyUnit(settlement.Definition, 1);
            }
            finally
            {
                // If AddSupplyUnit changed the count and then threw, the settlement is still
                // credited and must not repeat that side effect on the next retry.
                settlement.InventoryCredited =
                    settlement.Hq.GetUnitSupply(settlement.Definition) > before;
            }
            return settlement.InventoryCredited;
        }

        private static void Retry(Settlement settlement, string reason)
        {
            settlement.RetryAt = Time.unscaledTime + RetrySeconds;
            Plugin.Logger.LogWarning(
                "[Recovery] " + settlement.Name + " settlement pending: " + reason);
        }

        private static bool IsHome(WingMember member) =>
            member != null && IsHome(member.Aircraft);

        private static bool IsHome(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled) return false;
            if (!aircraft.IsServer || !aircraft.LocalSim) return false;
            if (aircraft.radarAlt > GroundHeight) return false;

            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null || !hq.AnyNearAirbase(aircraft.transform.position, out _)) return false;
            if (aircraft.speed <= StoppedSpeed) return true;

            Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
            return pilot != null &&
                   (pilot.ejected || pilot.dead || pilot.currentState is PilotParkedState);
        }
    }
}
