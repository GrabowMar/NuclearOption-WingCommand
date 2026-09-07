using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Settles Return To Base as an idempotent sequence: capture facts, disembark without
    /// scoring a death, credit stock through the game's Returned path, refund purchase
    /// allocation, then release the squadron pilot back to the pool. A failed stage remains
    /// pending and is retried instead of losing the aircraft between unrelated mutations.
    /// </summary>
    internal static class WingRecovery
    {
        private const float GroundHeight = 5f;
        private const float StoppedSpeed = 3f;
        private const float RetrySeconds = 1f;
        private const float TouchdownHeight = 2f;
        private const float TouchdownSpeed = 1f;

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
            public float Paid;
            public bool LoadoutKnown;
            public WingLoadoutChoice Loadout;
            public bool Disembarked;
            public bool InventoryReturned;
            public bool Refunded;
            public bool OwnershipTransferred;
            public bool RosterReleased;
            public bool Completed;
            public bool SortieNoted;
            public float RefundedAmount;
            public float RetryAt;
        }

        private static readonly List<Settlement> pending = new List<Settlement>();

        public static void Tick(WingRegistry wing)
        {
            if (wing == null) return;

            foreach (WingMember member in wing.Members)
            {
                if (!member.RefitPending || !IsHome(member)) continue;
                if (CanTurnAround(member) && IsDown(member))
                {
                    member.CompleteRefit();
                    continue;
                }
                if (SeatEmpty(member))
                {
                    member.AbandonRefit();
                    WingCommandManager.Instance?.Toast(
                        member.Name + " could not turn around - recovering instead");
                }
            }

            bool despawn = RecoverySettlementPolicy.ShouldDespawn(
                Plugin.Settings.RtbReturnsToReserve.Value);

            if (!despawn)
            {
                // Recovery is switched off, so nothing is credited or despawned. The crew
                // still leaves the seat and returns to the pool, or the squadron count would
                // go on excusing capacity they are no longer flying.
                for (int i = wing.Count - 1; i >= 0; i--)
                {
                    WingMember member = wing.Members[i];
                    if (member == null || member.RefitPending ||
                        member.Order != WingOrder.ReturnToBase) continue;
                    if (!IsHome(member)) continue;
                    Disembark(member.Aircraft);
                    wing.Recover(member);
                }

                WingDeparture.Prune();
                IReadOnlyList<WingDeparture.Departing> landed = WingDeparture.Outbound;
                for (int i = landed.Count - 1; i >= 0; i--)
                {
                    WingDeparture.Departing departing = landed[i];
                    if (!IsHome(departing.Aircraft)) continue;
                    Disembark(departing.Aircraft);
                    WingPilotRoster.Retire(departing.AircraftId, survived: true);
                    WingDeparture.Forget(departing);
                }
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

        /// <summary>
        /// Whether prune should leave this member alone. Covers a settlement already
        /// staged and the one-frame gap where they are down at base under RTB/refit
        /// but the settlement has not begun yet.
        /// </summary>
        public static bool HoldsDeath(WingMember member)
        {
            if (member == null) return false;
            bool atBase = IsHome(member);
            bool rtbOrRefit = member.RefitPending || member.Order == WingOrder.ReturnToBase;
            return LandingHoldsDeath(IsPending(member), atBase, rtbOrRefit);
        }

        internal static bool LandingHoldsDeath(bool pendingSettlement, bool atFriendlyBase,
                                               bool rtbOrRefit) =>
            TaxiRewritePolicy.HoldsDeath(pendingSettlement, atFriendlyBase, rtbOrRefit);

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
                Paid = WingShop.PaidFor(aircraft),
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
                Paid = WingShop.PaidFor(departing.AircraftId),
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

                if (!settlement.Disembarked)
                {
                    Disembark(settlement.Aircraft);
                    settlement.Disembarked = true;
                }

                // Retire the pilot while the aircraft reference is still valid. If the
                // native return then fails, this settlement remains the durable owner and
                // retries instead of abandoning an untracked frame.
                if (!settlement.RosterReleased)
                {
                    WingPilotRoster.Retire(settlement.AircraftId, survived: true);
                    if (settlement.Member == null || settlement.Wing == null)
                    {
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

                if (!settlement.Refunded)
                {
                    if (RecoverySettlementPolicy.ShouldRefund(settlement.Owned, settlement.Paid) &&
                        GameManager.GetLocalPlayer(out Player player) && player != null)
                    {
                        player.AddAllocation(settlement.Paid);
                        settlement.RefundedAmount = settlement.Paid;
                    }
                    settlement.Refunded = true;
                }

                if (!settlement.InventoryReturned)
                {
                    if (settlement.Aircraft == null ||
                        !UnitRegistry.TryGetUnit(settlement.AircraftId, out Unit tracked) ||
                        tracked == null)
                    {
                        settlement.InventoryReturned = true;
                    }
                    else
                    {
                        try
                        {
                            settlement.Aircraft.ReturnToInventory();
                        }
                        catch (Exception e)
                        {
                            Retry(settlement, "ReturnToInventory - " + e.Message);
                            return;
                        }

                        if (UnitRegistry.TryGetUnit(settlement.AircraftId, out tracked) &&
                            tracked != null && !tracked.disabled)
                        {
                            Retry(settlement, "waiting for native return confirmation");
                            return;
                        }
                        settlement.InventoryReturned = true;
                    }
                }

                if (!settlement.OwnershipTransferred)
                {
                    if (settlement.Owned) WingShop.TakePurchased(settlement.AircraftId);
                    settlement.OwnershipTransferred = true;
                }

                WingLoadoutBook.Forget(settlement.AircraftId);

                settlement.Completed = true;
                WingDeparture.Forget(settlement.Departing);
                WingCommandManager.Instance?.Toast(ToastFor(settlement));
                Plugin.LogVerbose(
                    "[Recovery] " + settlement.Name + " recovered at base; " +
                    (settlement.Definition != null ? settlement.Definition.unitName : "airframe") +
                    (settlement.RefundedAmount > 0f
                        ? " refunded " + Mathf.RoundToInt(settlement.RefundedAmount)
                        : " returned to faction stock"));
            }
            catch (Exception e)
            {
                Retry(settlement, e.GetType().Name + " - " + e.Message);
            }
        }

        private static string ToastFor(Settlement settlement)
        {
            if (settlement.RefundedAmount > 0f)
                return settlement.Name + " recovered - " +
                       Mathf.RoundToInt(settlement.RefundedAmount) + " allocation returned";
            return settlement.Name + " recovered to faction stock";
        }

        private static void Retry(Settlement settlement, string reason)
        {
            settlement.RetryAt = Time.unscaledTime + RetrySeconds;
            Plugin.Logger.LogWarning(
                "[Recovery] " + settlement.Name + " settlement pending: " + reason);
        }

        private static bool CanTurnAround(WingMember member) =>
            member != null && !SeatEmpty(member);

        private static bool SeatEmpty(WingMember member)
        {
            if (member?.Pilot == null) return true;
            return member.Pilot.dead || member.Pilot.ejected;
        }

        internal static void Disembark(Aircraft aircraft)
        {
            if (aircraft == null) return;
            Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
            if (pilot != null && !(pilot.currentState is PilotParkedState) &&
                pilot.parkedState != null)
                pilot.SwitchState(pilot.parkedState);
            if (!aircraft.HasEjected()) aircraft.StartEjectionSequence();
        }

        private static bool IsHome(WingMember member) =>
            member != null && IsHome(member.Aircraft);

        /// <summary>
        /// Actually on the ground and stopped, which is a stricter question than
        /// <see cref="IsHome"/> asks.
        ///
        /// A recovery only has to know that an aircraft has arrived, because the next thing
        /// that happens to it is being returned. A refit has to know it is <i>down</i>: it
        /// replenishes and then launches again, and the five-metre arrival window admits a
        /// helicopter still two seconds above the pad and descending at walking pace, or a
        /// jet rolling out at speed. Refitting there would rearm an aircraft in mid-air, or
        /// send one back down the runway it is still braking on.
        ///
        /// Waiting is safe: a landed wingman is parked, and the stock parked state holds
        /// full brake below one metre of radar altitude, so it does stop.
        /// </summary>
        private static bool IsDown(WingMember member)
        {
            if (member == null || !IsHome(member)) return false;
            Aircraft aircraft = member.Aircraft;
            return aircraft.radarAlt <= TouchdownHeight && aircraft.speed <= TouchdownSpeed;
        }

        internal static bool IsHome(Aircraft aircraft)
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
