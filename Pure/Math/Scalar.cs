using System;

namespace WingCommand
{
    /// <summary>Float helpers shared by guidance and control (System.Math works in doubles).</summary>
    internal static class Scalar
    {
        public const float G = 9.81f;
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 == edge0) return x < edge0 ? 0f : 1f;
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Wrap degrees into (-180, 180].</summary>
        public static float Wrap180(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees <= -180f) degrees += 360f;
            return degrees;
        }
    }
}
