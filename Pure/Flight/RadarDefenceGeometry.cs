using System;

namespace WingCommand
{
    internal static class RadarDefenceGeometry
    {
        /// <summary>Radar altitude below which the aircraft is still climbing out.</summary>
        public const float ClimboutAlt = 420f;

        /// <summary>Higher climb-out band used only while slow.</summary>
        public const float ClimboutSlowAlt = 650f;

        /// <summary>Slow-speed threshold for the higher climb-out band, in m/s.</summary>
        public const float ClimboutSlowSpeed = 85f;

        /// <summary>Climb-out commits to a break only inside this predicted impact time.</summary>
        public const float ClimboutCommitSeconds = 5.5f;

        /// <summary>Climb-out commits to a break only inside this slant range, in metres.</summary>
        public const float ClimboutCommitRange = 2200f;

        public static bool IsClimbout(float radarAlt, float airspeed) =>
            radarAlt < ClimboutAlt || (radarAlt < ClimboutSlowAlt && airspeed < ClimboutSlowSpeed);

        /// <summary>RWR paint is not a missile. Climb-out only breaks for a close inbound.</summary>
        public static bool AllowEvadeCommit(
            bool climbout, bool hasNearMissile, float impactSeconds, float missileDistance)
        {
            if (!hasNearMissile) return false;
            if (!climbout) return true;
            return impactSeconds < ClimboutCommitSeconds || missileDistance < ClimboutCommitRange;
        }

        // Keep the chosen side for this threat: crossing the leader must not reverse the notch.
        public static (float x, float z, int side) Notch(float sourceX, float sourceZ,
            float headingX, float headingZ, float leaderX, float leaderZ,
            bool preferLeader, int previousSide)
        {
            float length = (float)Math.Sqrt(sourceX * sourceX + sourceZ * sourceZ);
            if (length < 1f) return (headingX, headingZ, previousSide);
            float x = -sourceZ / length, z = sourceX / length;
            float alignment = x * headingX + z * headingZ;
            int side = previousSide;
            if (side == 0)
            {
                float leaderLength = (float)Math.Sqrt(leaderX * leaderX + leaderZ * leaderZ);
                // Never reverse an already established notch just to reduce separation.
                if (preferLeader && Math.Abs(alignment) < 0.7f && leaderLength > 1f)
                    alignment += 0.75f * (x * leaderX + z * leaderZ) / leaderLength;
                side = alignment >= 0f ? 1 : -1;
            }
            return (x * side, z * side, side);
        }
    }
}
