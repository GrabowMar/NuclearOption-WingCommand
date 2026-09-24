using System;

namespace WingCommand
{
    /// <summary>Splits an attack order's targets across the wing (spec M5 §1, M5b): a member keeps its target while it
    /// lives, it can attack it and the target has room (<see cref="PerTarget"/>); then each target in the order's
    /// priority fills up to <see cref="PerTarget"/> with the nearest free members that can attack it; members still free
    /// join the live target they can attack with the fewest attackers (then the nearest). A member that can attack none
    /// is −1. No allocation: caller arrays and a fixed scratch.</summary>
    internal static class TargetAllocator
    {
        public const int MaxTargets = 16;
        public static int PerTarget = 2;
        private static readonly int[] count = new int[MaxTargets];

        public static void Assign(int members, int targets, bool[] canAttack, float[] distance, bool[] alive, int[] current, int[] result)
        {
            targets = Math.Min(targets, MaxTargets);
            for (int t = 0; t < targets; t++) count[t] = 0;
            for (int m = 0; m < members; m++)
            {
                result[m] = -1;
                int c = current[m];
                if (c < 0 || c >= targets || !alive[c] || !canAttack[m * targets + c] || count[c] >= PerTarget) continue;
                result[m] = c;
                count[c]++;
            }
            bool placed = true;
            while (placed)
            {
                placed = false;
                for (int t = 0; t < targets; t++)
                {
                    if (!alive[t] || count[t] >= PerTarget) continue;
                    int best = Nearest(members, targets, t, canAttack, distance, result);
                    if (best < 0) continue;
                    result[best] = t;
                    count[t]++;
                    placed = true;
                }
            }
            for (int m = 0; m < members; m++)
            {
                if (result[m] >= 0) continue;
                int best = -1;
                for (int t = 0; t < targets; t++)
                {
                    if (!alive[t] || !canAttack[m * targets + t]) continue;
                    if (best < 0 || count[t] < count[best] ||
                        (count[t] == count[best] && distance[m * targets + t] < distance[m * targets + best])) best = t;
                }
                if (best < 0) continue;
                result[m] = best;
                count[best]++;
            }
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
