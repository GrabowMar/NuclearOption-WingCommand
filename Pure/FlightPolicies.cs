namespace WingCommand
{
    /// <summary>Same-direction holding circles, separated radially by roster slot.</summary>
    internal static class OrbitGeometry
    {
        public static (float x, float z) AimOffset(float fromX, float fromZ,
                                                   float radius, int slot, float spacing)
        {
            if (fromX * fromX + fromZ * fromZ < 1f) { fromX = 0f; fromZ = 1f; }
            const double lookahead = 70d * System.Math.PI / 180d;
            double bearing = System.Math.Atan2(fromZ, fromX) + lookahead;
            float laneRadius = System.Math.Max(200f, radius) +
                               System.Math.Max(0, slot - 1) * System.Math.Max(0f, spacing);
            // The aim point's radial projection is the desired holding radius. A point
            // on the circle itself would always steer inward and cut the orbit short.
            double aimRadius = laneRadius / System.Math.Cos(lookahead);
            return ((float)(System.Math.Cos(bearing) * aimRadius),
                    (float)(System.Math.Sin(bearing) * aimRadius));
        }
    }

    /// <summary>
    /// Followers use the designated lead; that lead, or a flight without one, uses the player.
    /// </summary>
    internal static class FlightLeadPolicy
    {
        public static T FormationLeader<T>(bool isThisMemberTheLead, T designatedLead,
                                           T wingLeader) where T : class =>
            (isThisMemberTheLead || designatedLead == null) ? wingLeader : designatedLead;
    }

    /// <summary>Pure rotary hover transition, separated from Unity steering for tests.</summary>
    internal static class RotaryHoverPolicy
    {
        public static bool ShouldHover(bool wasHovering, float leaderHorizontalSpeed,
                                       float horizontalSlotError, float spacing,
                                       float hoverSpeed, float hysteresis,
                                       float stationSpacings)
        {
            float threshold = wasHovering ? hoverSpeed + hysteresis
                                          : hoverSpeed - hysteresis;
            bool onStation = horizontalSlotError < spacing * stationSpacings;
            return onStation && leaderHorizontalSpeed < threshold;
        }
    }

    /// <summary>A timeout whose clock restarts whenever a decreasing quantity progresses.</summary>
    internal sealed class CargoProgressTracker
    {
        public int LastAmount { get; private set; }
        public float LastProgressAt { get; private set; }
        public bool MadeProgress { get; private set; }

        public void Reset(int amount, float now)
        {
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = false;
        }

        public bool Observe(int amount, float now)
        {
            if (amount >= LastAmount) return false;
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = true;
            return true;
        }

        public bool IsStalled(float now, float timeout) => now - LastProgressAt >= timeout;
    }

    /// <summary>Terrain-abort policy for aircraft Wing Command already controls.</summary>
    internal static class TerrainAbortPolicy
    {
        public const float GrabRange = 400f;
        public const float ReleaseRange = 200f;
        public const float AbortAlt = 50f;
        public const float AbortReleaseAlt = 90f;

        public static bool ShouldAbort(
            float radarAlt, float leaderDistance, WingOrder order,
            bool incumbent, bool deliveryPending)
        {
            if (deliveryPending) return false;
            if (!AllowsAbort(order)) return false;

            float alt = incumbent ? AbortReleaseAlt : AbortAlt;
            float range = incumbent ? ReleaseRange : GrabRange;
            return radarAlt < alt && leaderDistance > range;
        }

        /// <summary>
        /// Orders that are supposed to be low. A pull-up here would fight the task.
        /// </summary>
        public static bool AllowsAbort(WingOrder order) =>
            order != WingOrder.LandHere &&
            order != WingOrder.ReturnToBase &&
            order != WingOrder.Attack &&
            order != WingOrder.FireForEffect &&
            order != WingOrder.DeliverCargo;
    }
}
