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

    /// <summary>Engine-free hangar departure corridor rules. Native frees a hangar once the previous
    /// aircraft is 30 m clear of the door, which is a tail in front of the next nose; a pad is safe
    /// to spawn from only when its spawn point and the roll-out in front of it are physically clear.
    /// The corridor width and roll-out length are the values the RTS-Commander family validated
    /// against repeated on-deck losses.</summary>
    internal static class HangarLaunchPolicy
    {
        /// <summary>How far in front of the pad door the roll-out corridor is checked.</summary>
        public const float ExitPathMeters = 150f;

        /// <summary>Corridor half-width: a fighter's length with margin.</summary>
        public const float PathClearanceMeters = 40f;

        /// <summary>Squared distance from a point to a segment, in one space, pure.</summary>
        public static float SegmentDistanceSquared(
            float px, float py, float pz,
            float ax, float ay, float az,
            float bx, float by, float bz)
        {
            float abx = bx - ax, aby = by - ay, abz = bz - az;
            float apx = px - ax, apy = py - ay, apz = pz - az;

            float lengthSq = abx * abx + aby * aby + abz * abz;
            float t = lengthSq > 0.0001f
                ? (apx * abx + apy * aby + apz * abz) / lengthSq
                : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float dx = apx - abx * t;
            float dy = apy - aby * t;
            float dz = apz - abz * t;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>Whether a unit at the point blocks a departure whose pad is the segment start
        /// and whose roll-out ends at the far end, within the clearance.</summary>
        public static bool PathBlocked(
            float px, float py, float pz,
            float padX, float padY, float padZ,
            float exitX, float exitY, float exitZ,
            float clearance) =>
            SegmentDistanceSquared(px, py, pz, padX, padY, padZ, exitX, exitY, exitZ)
                <= clearance * clearance;
    }
}
