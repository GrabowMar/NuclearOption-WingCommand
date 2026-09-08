using System;

namespace WingCommand
{
    internal static class LaunchSafety
    {
        public static bool CanHandOff(bool nativeTakeoffComplete, bool inTakeoffState,
            bool rotary, float altitude, float forwardAirspeed, float takeoffSpeed,
            float minimumAirspeed = 0f)
        {
            // Native fixed-wing takeoff completes at 75 m AGL even below flying speed. Let its next
            // native state convert the nozzles and accelerate before formation starts turning.
            if (nativeTakeoffComplete) return rotary || forwardAirspeed >= minimumAirspeed;
            if (!inTakeoffState) return false;

            // Rotary departures retain native collective and protected climb until native airborne
            // completion. An early formation turn can hit nearby terrain; fixed-wing clearance uses a
            // separate gate.
            if (rotary) return false;

            // Require runway clearance before formation may turn.
            return altitude >= 8f && takeoffSpeed > 0f &&
                forwardAirspeed >= Math.Max(minimumAirspeed,
                    takeoffSpeed * WingTuning.LaunchSpeedMargin);
        }

        public static float Clearance(float firstSize, float secondSize) =>
            Math.Max(WingTuning.LaunchClearanceMinimum,
                (Math.Max(0f, firstSize) + Math.Max(0f, secondSize)) * 0.5f +
                WingTuning.LaunchClearanceMargin);

    }
}
