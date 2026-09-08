namespace WingCommand
{
    /// <summary>Same-direction orbit lanes separated by roster slot radius.</summary>
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
            // Project the aim tangent onto the desired radius; aiming directly on the circle would cut
            // inward.
            double aimRadius = laneRadius / System.Math.Cos(lookahead);
            return ((float)(System.Math.Cos(bearing) * aimRadius),
                    (float)(System.Math.Sin(bearing) * aimRadius));
        }
    }

    /// <summary>Followers use temporary flight lead; the lead and flights without one follow the
    /// player.</summary>
    internal static class FlightLeadPolicy
    {
        public static T FormationLeader<T>(bool isThisMemberTheLead, T designatedLead,
                                           T wingLeader) where T : class =>
            (isThisMemberTheLead || designatedLead == null) ? wingLeader : designatedLead;
    }

    /// <summary>Engine-free rotary hover/cruise transition policy.</summary>
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

    /// <summary>Convert world slot height to rotary AGL hold because terrain-following ignores destination
    /// height; preserve terrain clearance for low slots.</summary>
    internal static class RotaryAltitudePolicy
    {
        public static float SlotAgl(float ownAltitude, float ownRadarAltitude,
                                    float slotAltitude, float terrainClearance)
        {
            float groundAltitude = ownAltitude - System.Math.Max(0f, ownRadarAltitude);
            return System.Math.Max(terrainClearance, slotAltitude - groundAltitude);
        }
    }

    /// <summary>Restart timeout whenever a decreasing progress value advances.</summary>
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

    /// <summary>Terrain escape policy for aircraft already controlled by the wing.</summary>
    internal static class TerrainAbortPolicy
    {
        public const float GrabRange = 400f;
        public const float ReleaseRange = 200f;
        public const float AbortAlt = 50f;
        public const float AbortReleaseAlt = 90f;

        /// <summary>Use the native five-second terrain horizon and current descent before a fixed
        /// altitude floor becomes too late. Native warning also covers terrain ahead.</summary>
        public static bool ImmediateDanger(float radarAlt, float verticalSpeed, float terrainUrgency) =>
            radarAlt >= 8f && (terrainUrgency > 0f ||
                (verticalSpeed < -5f && radarAlt < AbortAlt - verticalSpeed * 5f));

        public static bool AllowsRecovery(WingOrder order) => order != WingOrder.LandHere &&
            order != WingOrder.ReturnToBase && order != WingOrder.DeliverCargo;

        public static bool AllowsRecovery(in WingSituation s, bool incumbent = false) =>
            !s.DeliveryPending && !s.MemberIsSurface && (s.RadarAlt >= 8f || incumbent) &&
            AllowsRecovery(s.Order);

        public static bool TerrainThreat(in WingSituation s, bool incumbent) =>
            (incumbent && s.RadarAlt < AbortReleaseAlt) ||
            ImmediateDanger(s.RadarAlt, s.VerticalSpeed, s.TerrainUrgency) ||
            ShouldAbort(s.RadarAlt, s.LeaderDistance, s.Order, incumbent, s.DeliveryPending);

        /// <summary>Keep the existing recovery controller until a defensive exit is upright, no longer
        /// diving, and has enough forward airspeed to turn toward a task.</summary>
        public static bool ShouldRecover(in WingSituation s, bool incumbent) =>
            AllowsRecovery(in s, incumbent) && (TerrainThreat(in s, incumbent) ||
                (!s.MemberIsRotary && !s.MissileWarned && (incumbent || s.RecoveringFromDefence) &&
                    (System.Math.Abs(s.BankAngle) > 35f || s.VerticalSpeed < -10f ||
                     s.Airspeed < s.MinimumAirspeed)));

        public static bool ShouldAbort(
            float radarAlt, float leaderDistance, WingOrder order,
            bool incumbent, bool deliveryPending)
        {
            if (deliveryPending) return false;
            if (!AllowsAbort(order)) return false;
            // Exclude apron altitude from pull-up triggers so taxi keeps native ownership.
            if (radarAlt < 8f) return false;

            float alt = incumbent ? AbortReleaseAlt : AbortAlt;
            float range = incumbent ? ReleaseRange : GrabRange;
            return radarAlt < alt && leaderDistance > range;
        }

        /// <summary>Low-altitude tasks for which terrain abort would oppose the order.</summary>
        public static bool AllowsAbort(WingOrder order) =>
            order != WingOrder.LandHere &&
            order != WingOrder.ReturnToBase &&
            order != WingOrder.Attack &&
            order != WingOrder.FireForEffect &&
            order != WingOrder.DeliverCargo;
    }
}
