using System;

namespace WingCommand
{
    internal static class FormationSafety
    {
        // This is a floor relative to terrain beneath the aircraft, not terrain ahead.
        public static float AimAltitude(float ownAltitude, float requestedAltitude, float radarAlt) =>
            Math.Max(requestedAltitude, ownAltitude - Math.Max(0f, radarAlt - 50f));

        // Additive leader roll trim must yield whenever sink recovery owns the roll axis.
        public static bool AllowsBankMatch(float radarAlt, float verticalSpeed, float slotVerticalSpeed = 0f) =>
            radarAlt >= WingTuning.BankMatchFloor + Math.Max(0f, -verticalSpeed) * 4f &&
            verticalSpeed >= slotVerticalSpeed - 2f;
    }
}
