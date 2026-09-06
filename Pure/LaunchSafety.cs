using System;

namespace WingCommand
{
    internal static class LaunchSafety
    {
        public static bool CanHandOff(bool nativeTakeoffComplete, bool inTakeoffState,
            bool rotary, float altitude, float speed, float takeoffSpeed)
        {
            if (nativeTakeoffComplete) return true;
            if (!inTakeoffState) return false;
            // Clear the runway/helipad before formation can request a turn.
            return rotary ? altitude >= 5f :
                altitude >= 8f && takeoffSpeed > 0f && speed >= takeoffSpeed * WingTuning.LaunchSpeedMargin;
        }

        public static float RejoinBankLimit(float altitude, float speed, float takeoffSpeed)
        {
            float clearance = Math.Max(0f, Math.Min(1f,
                (altitude - WingTuning.FixedWingAirborneAlt) / WingTuning.RejoinBankHeightSpan));
            float terrainLimit = WingTuning.DepartureTurnBank + clearance *
                (WingTuning.RejoinMaximumBank - WingTuning.DepartureTurnBank);
            // Turning raises stall speed. Preserve a flying-speed margin instead of
            // letting a distant/high-bank leader demand a near-vertical turn on liftoff.
            float ratio = Math.Max(1f, takeoffSpeed) * WingTuning.LaunchSpeedMargin / Math.Max(1f, speed);
            float energyLimit = (float)(Math.Acos(Math.Min(1f, ratio * ratio)) * 180d / Math.PI);
            return Math.Max(WingTuning.RejoinMinimumBank, Math.Min(terrainLimit, energyLimit));
        }

        public static float Clearance(float firstSize, float secondSize) =>
            Math.Max(WingTuning.LaunchClearanceMinimum,
                (Math.Max(0f, firstSize) + Math.Max(0f, secondSize)) * 0.5f +
                WingTuning.LaunchClearanceMargin);

    }
}
