using System;

namespace WingCommand
{
 /// <summary>Redirect only owned post-landing taxi to parking, preventing native service taxi from
 /// ejecting returning crew. Use HasTakenOff, matching native taxi routing; preserve all outbound
 /// departures.</summary>
    internal static class TaxiRewritePolicy
    {
     /// <summary>Whether owned inbound taxi should enter parking; exclude ordinary faction
     /// resupply.</summary>
        public static bool ShouldPark(bool ours, bool enteringTaxi, bool hasTakenOff) =>
            ours && enteringTaxi && hasTakenOff;

     /// <summary>Suppress post-landing ejection only for pending refit, which needs a seated pilot.
     /// Plain RTB retains native disembarkation.</summary>
        public static bool ShouldSuppressEjection(bool ours, bool refitPending, bool hasTakenOff) =>
            ours && refitPending && hasTakenOff;

     /// <summary>Protect grounded friendly-base RTB/refit from loss pruning before settlement is
     /// staged.</summary>
        public static bool HoldsDeath(bool pendingSettlement, bool atFriendlyBase,
                                      bool rtbOrRefit) =>
            pendingSettlement || (atFriendlyBase && rtbOrRefit);

     /// <summary>Drain owned taxi/takeoff queue claims on interrupted departure, but retain them when
     /// entering takeoff. Native LeaveState does not dequeue, and only successful takeoff phases
     /// release claims.</summary>
        public static bool ShouldDrainQueue(bool ours, bool leavingDeparture,
                                            bool enteringTakeoff) =>
            ours && leavingDeparture && !enteringTakeoff;
    }

 /// <summary>Compensate native hangar stock debits already covered by the purchase transaction. Measure
 /// before/after counts because rejected or deferred spawn paths may not debit.</summary>
    internal static class SupplyCompensation
    {
     /// <summary>Nonnegative stock decrease to restore; never remove an unrelated increase.</summary>
        public static int Delta(int before, int after) => Math.Max(0, before - after);
    }
}
