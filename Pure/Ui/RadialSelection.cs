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
            if (count <= 0 || IsInDeadzone(x, y)) return -1;
            double angle = (Math.Atan2(x, y) * 180.0 / Math.PI + 360.0) % 360.0;
            double width = 360.0 / count;
            if (previous >= 0 && previous < count)
            {
                double distance = Math.Abs((angle - previous * width + 540.0) % 360.0 - 180.0);
                if (distance < width * 0.5 + 4.0) return previous;
            }
            return (int)Math.Floor((angle + width * 0.5) / width) % count;
        }

        internal static bool IsInDeadzone(float x, float y) =>
            x * x + y * y < Deadzone * Deadzone;

        internal static float SectorAngle(int index, int totalSectors) =>
            totalSectors <= 0 ? 0f : index * (360f / totalSectors);

        internal static float SectorSweep(int totalSectors, float gapDegrees = 1.2f) =>
            totalSectors <= 0 ? 0f : Math.Max(0f, (360f / totalSectors) - gapDegrees);

        internal static float SectorStartAngle(int index, int totalSectors, float gapDegrees = 1.2f)
        {
            float center = SectorAngle(index, totalSectors);
            float sweep = SectorSweep(totalSectors, gapDegrees);
            return center - sweep * 0.5f;
        }

        internal static string FormatSquadronSubtitle(int wingCount, string roe, string formation)
        {
            string countLabel = wingCount == 1 ? "1 WINGMAN" : $"{wingCount} WINGMEN";
            return $"{countLabel}  •  ROE: {roe}\nFORMATION: {formation}";
        }

        internal static string FormatTargetSubtitle(bool hasTarget, string targetName, string fallback = "NO TARGET LOCKED")
        {
            if (hasTarget && !string.IsNullOrWhiteSpace(targetName))
                return $"TARGET: {targetName.ToUpperInvariant()}";
            return fallback;
        }

        internal static string FormatRoeTransition(string currentRoe, string nextRoe) =>
            $"{currentRoe}  ▶  {nextRoe}";

        internal static string FormatHint(bool inDeadzone, bool isAvailable, bool isTargetRequired = false)
        {
            if (inDeadzone) return "MOVE TO SELECT  •  R-CLICK CANCEL";
            if (!isAvailable) return isTargetRequired ? "LOCK TARGET ON HUD TO ORDER" : "ORDER UNAVAILABLE";
            return "RELEASE TO CONFIRM";
        }
    }
}
