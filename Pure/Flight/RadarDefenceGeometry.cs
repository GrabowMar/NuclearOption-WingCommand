using System;

namespace WingCommand
{
    internal static class RadarDefenceGeometry
    {
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
