using System;

namespace WingCommand
{
    internal static class RadialSelection
    {
        internal const float PointerRadius = 164f;
        internal const float Deadzone = 64f;

        // Top is zero, clockwise. Keep the current sector through small boundary jitter.
        internal static int FromPointer(float x, float y, int previous, int count)
        {
            if (count <= 0 || x * x + y * y < Deadzone * Deadzone) return -1;
            double angle = (Math.Atan2(x, y) * 180.0 / Math.PI + 360.0) % 360.0;
            double width = 360.0 / count;
            if (previous >= 0 && previous < count)
            {
                double distance = Math.Abs((angle - previous * width + 540.0) % 360.0 - 180.0);
                if (distance < width * 0.5 + 4.0) return previous;
            }
            return (int)Math.Floor((angle + width * 0.5) / width) % count;
        }
    }
}
