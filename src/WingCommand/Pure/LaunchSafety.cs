using System;

namespace WingCommand
{
    internal static class LaunchSafety
    {
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

        public static bool ReadyForHandoff(float altitude, float speed, float takeoffSpeed,
            float verticalSpeed, bool rotary, bool launching) =>
            !launching && altitude >= (rotary ? WingTuning.RotaryAirborneAlt : WingTuning.FixedWingAirborneAlt) &&
            verticalSpeed >= -WingTuning.LaunchMaximumSink &&
            (rotary || speed >= Math.Max(1f, takeoffSpeed) * WingTuning.LaunchSpeedMargin);
    }
}
