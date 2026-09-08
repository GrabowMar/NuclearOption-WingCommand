using System;

namespace WingCommand
{
    internal static class FormationSafety
    {
        // This is a floor relative to terrain beneath the aircraft, not terrain ahead.
        public static float AimAltitude(float ownAltitude, float requestedAltitude, float radarAlt) =>
            Math.Max(requestedAltitude, ownAltitude - Math.Max(0f, radarAlt - 50f));

    }
}
