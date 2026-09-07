using System;

namespace WingCommand
{
    /// <summary>
    /// Which stock taxi runs belong to the game and which one belongs to us.
    ///
    /// Exactly one is ours. After a landing, <c>AIPilotLandingState</c> hands the aircraft
    /// to <c>AIPilotTaxiState</c> to look for a service point, and that run ends by setting
    /// <c>disembarking</c> and ejecting the pilot on the apron — correct for a faction AI
    /// whose sortie is over, fatal for a wingman that was about to be credited back into
    /// stock. Every other taxi is a departure and must be left alone: rewriting outbound
    /// taxi is what all eight attempts in <c>docs/airfield-findings.md</c> did, and it is
    /// how each of them put an aircraft into the grass.
    ///
    /// The whole decision is <c>HasTakenOff</c>. The stock taxi state reads the same flag
    /// for the same purpose — <c>SearchForAirbase</c> heads for a service point when it is
    /// set and for the takeoff runway when it is not — so agreeing with it is what keeps
    /// this from being a second opinion about where the aircraft is going.
    /// </summary>
    internal static class TaxiRewritePolicy
    {
        /// <summary>
        /// Whether a pilot entering taxi should be parked instead.
        ///
        /// <paramref name="ours"/> keeps this off the faction's own aircraft, which are
        /// taxiing to resupply because that is what the mission wants them to do.
        /// </summary>
        public static bool ShouldPark(bool ours, bool enteringTaxi, bool hasTakenOff) =>
            ours && enteringTaxi && hasTakenOff;

        /// <summary>
        /// Whether leaving a departure state should give back the runway slot.
        ///
        /// Neither <c>AIPilotTaxiState.LeaveState</c> nor <c>AIPilotTakeoffState.LeaveState</c>
        /// dequeues, and <c>Runway.DequeueTakeoff</c> can only pop the head — so an aircraft
        /// that reaches the front of a takeoff queue and then leaves by any route other than
        /// starting its run holds that strip, and every strip crossing it, for the rest of
        /// the mission.
        ///
        /// Both departure states leak, for different reasons. Taxi queues at the hold-short
        /// line and lets go of nothing if it is interrupted. Takeoff dequeues itself only on
        /// the happy path — <c>RegisterStartTakeoff</c> when the strip allows simultaneous
        /// departures, <c>RegisterTakeoffLeftRunway</c> once airborne — and its twelve-second
        /// stuck timer ejects the pilot straight to parked without touching the queue.
        ///
        /// Entering takeoff is the one transition that must not drain, because that is the
        /// state the slot was reserved for.
        /// </summary>
        public static bool ShouldDrainQueue(bool ours, bool leavingDeparture,
                                            bool enteringTakeoff) =>
            ours && leavingDeparture && !enteringTakeoff;
    }

    /// <summary>
    /// Undoing the faction stock a hangar charges for an aircraft the shop has already
    /// paid for.
    ///
    /// <c>Hangar.TrySpawnAircraft</c> debits one airframe from faction supply whenever it is
    /// called with a null player, which is how the mod calls it. The purchase transaction has
    /// already reserved its own source — faction stock, the wing reserve, or an airframe the
    /// player owns outright — so that second debit is a duplicate and has to be given back.
    ///
    /// Measured rather than assumed. The native call charges through a path that can be
    /// diverted, so comparing the count either side is the only reading that stays true if
    /// the game changes what it does, and it is naturally zero when the debit never happened
    /// (a refused spawn, or a carrier pad that abandoned the launch while its doors opened).
    /// </summary>
    internal static class SupplyCompensation
    {
        /// <summary>
        /// How much stock to put back, given the faction count before and after the native
        /// spawn call. Never negative: if the count went <i>up</i>, something other than
        /// this delivery moved it and taking that away would be inventing a debit.
        /// </summary>
        public static int Delta(int before, int after) => Math.Max(0, before - after);
    }
}
