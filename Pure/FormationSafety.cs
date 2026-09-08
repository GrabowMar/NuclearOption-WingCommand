using System;

namespace WingCommand
{
    internal static class FormationSafety
    {
        // Altitude floor uses terrain beneath the aircraft, not a forward terrain probe.
        public static float AimAltitude(float ownAltitude, float requestedAltitude, float radarAlt) =>
            Math.Max(requestedAltitude, ownAltitude - Math.Max(0f, radarAlt - 50f));

    }
}
