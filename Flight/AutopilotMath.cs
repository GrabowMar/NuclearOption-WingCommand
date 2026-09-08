using UnityEngine;

namespace WingCommand
{
 /// <summary>Shared AutoAim clamps: fixed-wing altitude from maxRadius to 8 km, rotary altitude from
 /// minimumRadarAlt, and pursuit bank below inversion.</summary>
    internal static class AutopilotMath
    {
     /// <summary>Clamp fixed-wing held altitude to the airframe turn-radius floor and 8 km
     /// ceiling.</summary>
        public static float CruiseHold(Aircraft aircraft, float desired) =>
            Mathf.Clamp(desired, aircraft.maxRadius, 8000f);

     /// <summary>Clamp rotary AGL to the airframe minimumRadarAlt and task-specific limits.</summary>
        public static float RotaryAgl(Aircraft aircraft, float desired,
                                      float min = 25f, float max = 3000f) =>
            Mathf.Clamp(Mathf.Max(aircraft.GetAircraftParameters().minimumRadarAlt, desired),
                        min, max);

     /// <summary>Pursuit bank limit below inversion.</summary>
        public static float PursuitBank() =>
            Mathf.Min(WingTuning.PursuitBank, FixedWingFormation.MaxSafeBank);
    }
}
