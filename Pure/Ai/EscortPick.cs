using System;

namespace WingCommand
{
    /// <summary>Which friendly aircraft "Escort target" means when the player has none selected: the nearest within
    /// <see cref="RangeM"/> and <see cref="ConeDeg"/> of the player's nose (spec M2 §6).</summary>
    internal static class EscortPick
    {
        public static float RangeM = 5000f, ConeDeg = 60f;

        /// <summary>Index of the chosen candidate among the first <paramref name="count"/>, or −1.</summary>
        public static int Nearest(Vec3 player, Vec3 nose, Vec3[] candidates, int count)
        {
            Vec3 fwd = nose.Horizontal.SqrLength > 1e-4f ? nose.Horizontal.Normalized : Vec3.Forward;
            float cos = (float)Math.Cos(ConeDeg * Scalar.Deg2Rad), best = float.MaxValue;
            int chosen = -1;
            for (int i = 0; i < count; i++)
            {
                Vec3 to = (candidates[i] - player).Horizontal;
                float d = to.Length;
                if (d < 1f || d > RangeM || Vec3.Dot(to / d, fwd) < cos || d >= best) continue;
                best = d;
                chosen = i;
            }
            return chosen;
        }
    }
}
