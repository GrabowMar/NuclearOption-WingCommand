using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Retryable RTB settlement: capture facts, disembark without a death, return native stock,
    /// refund allocation, and release the pilot. Completed stages are not repeated after
    /// failure.</summary>
    internal static class WingRecovery
    {
        private const float GroundHeight = 5f;
        private const float StoppedSpeed = 3f;
        private const float RetrySeconds = 1f;
        private const float TouchdownHeight = 2f;
        private const float TouchdownSpeed = 1f;

        private sealed class Settlement
        {
            /// <summary>Absent after the aircraft leaves the wing roster.</summary>
            public WingRegistry Wing;

            /// <summary>Absent for an already-released aircraft.</summary>
            public WingMember Member;

            /// <summary>Released-aircraft departure record to finish.</summary>
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

            // Complete pending settlements even after recovery is disabled.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Settlement settlement = pending[i];
                Advance(settlement);
                if (settlement.Completed) pending.RemoveAt(i);
            }

            bool despawn = RecoverySettlementPolicy.ShouldDespawn(
                Plugin.Settings.RtbReturnsToReserve.Value);

            if (!despawn)
            {
                // With recovery disabled, release crew without crediting or despawning aircraft so
                // capacity accounting stays accurate.
                for (int i = wing.Count - 1; i >= 0; i--)
                {
                    WingMember member = wing.Members[i];
                    if (member == null || member.RefitPending ||
                        member.Order != WingOrder.ReturnToBase) continue;
                    if (IsPending(member) || !IsHome(member)) continue;
                    Disembark(member.Aircraft);
                    wing.Recover(member);
                }

                WingDeparture.Prune();
                IReadOnlyList<WingDeparture.Departing> landed = WingDeparture.Outbound;
                for (int i = landed.Count - 1; i >= 0; i--)
                {
                    WingDeparture.Departing departing = landed[i];
                    if (IsPending(departing) || !IsHome(departing.Aircraft)) continue;
                    Disembark(departing.Aircraft);
                    WingPilotRoster.Retire(departing.AircraftId, survived: true);
                    WingDeparture.Forget(departing);
                }
                return;
            }

            // Walk backward because immediate settlement may remove a roster member.
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

            // Settle released aircraft separately because they are no longer on the wing roster.
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

        /// <summary>Whether a staged settlement must be protected from loss pruning during
        /// retries.</summary>
        public static bool IsPending(WingMember member)
        {
            if (member == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Member == member) return true;
            return false;
        }

        /// <summary>Protect staged recovery and the gap after RTB/refit touchdown before settlement
        /// begins.</summary>
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

        /// <summary>Start released-aircraft settlement from facts captured before roster
        /// removal.</summary>
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

                // Retire crew while the aircraft reference is valid. Keep settlement ownership for
                // retries if native return fails.
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
                    if (RecoverySettlementPolicy.ShouldRefund(settlement.Owned, settlement.Paid))
                    {
                        if (!GameManager.GetLocalPlayer(out Player player) || player == null)
                        {
                            Retry(settlement, "waiting for player to refund allocation");
                            return;
                        }
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

        /// <summary>Require grounded and stopped for refit, stricter than recovery arrival. The five-metre
        /// arrival window can include descending helicopters or rolling jets; wait for native parking
        /// brakes before replenishment and relaunch.</summary>
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
