using System;

namespace WingCommand
{
    /// <summary>Splits an attack order's targets across the wing (spec M5 §1, M5b): a member keeps its target while it
    /// lives and it could attack it within the last <see cref="KeepSeconds"/> (a target saturated with missiles in flight
    /// or a short track dropout does not move it; review M5b I2), the nearest <see cref="PerTarget"/> holders winning a
    /// contended target; then pairs first (review M5b I3): each target in the order's priority fills up to
    /// <see cref="PerTarget"/> with the nearest free members that can attack it; members still free join the live target
    /// they can attack with the fewest attackers (then the nearest). A member that can attack none is −1. No allocation:
    /// caller arrays and a fixed scratch; <paramref name="current"/> and <paramref name="result"/> must differ.</summary>
    internal static class TargetAllocator
    {
        public const int MaxTargets = 16;
        public static int PerTarget = 2;
        public static float KeepSeconds = 5f;
        private static readonly int[] count = new int[MaxTargets];

        /// <param name="lost">Per member, how long it has been unable to attack its current target (kept here).</param>
        public static void Assign(int members, int targets, bool[] canAttack, float[] distance, bool[] alive, int[] current, float[] lost, float dt, int[] result)
        {
            // Rows keep the caller's stride; only the loops stop at MaxTargets (review M5b minor 6).
            int n = Math.Min(targets, MaxTargets);
            for (int t = 0; t < n; t++) count[t] = 0;
            for (int m = 0; m < members; m++)
            {
                result[m] = -1;
                int c = current[m];
                lost[m] = c < 0 || c >= n || !alive[c] ? 0f : canAttack[m * targets + c] ? 0f : lost[m] + dt;
            }
            for (int t = 0; t < n; t++)
            {
                if (!alive[t]) continue;
                while (count[t] < PerTarget)
                {
                    int best = -1;
                    for (int m = 0; m < members; m++)
                    {
                        if (result[m] >= 0 || current[m] != t || lost[m] >= KeepSeconds) continue;
                        if (best < 0 || distance[m * targets + t] < distance[best * targets + t]) best = m;
                    }
                    if (best < 0) break;
                    result[best] = t;
                    count[t]++;
                }
            }
            for (int t = 0; t < n; t++)
            {
                if (!alive[t]) continue;
                while (count[t] < PerTarget)
                {
                    int best = Nearest(members, targets, t, canAttack, distance, result);
                    if (best < 0) break;
                    result[best] = t;
                    count[t]++;
                }
            }
            for (int m = 0; m < members; m++)
            {
                if (result[m] >= 0) continue;
                int best = -1;
                for (int t = 0; t < n; t++)
                {
                    if (!alive[t] || !canAttack[m * targets + t]) continue;
                    if (best < 0 || count[t] < count[best] ||
                        (count[t] == count[best] && distance[m * targets + t] < distance[m * targets + best])) best = t;
                }
                if (best < 0) continue;
                result[m] = best;
                count[best]++;
            }
            for (int m = 0; m < members; m++)
                if (result[m] != current[m]) lost[m] = 0f;
        }

        private static int Nearest(int members, int targets, int t, bool[] canAttack, float[] distance, int[] result)
        {
            int best = -1;
            for (int m = 0; m < members; m++)
            {
                if (result[m] >= 0 || !canAttack[m * targets + t]) continue;
                if (best < 0 || distance[m * targets + t] < distance[best * targets + t]) best = m;
            }
            return best;
        }
    }
}
