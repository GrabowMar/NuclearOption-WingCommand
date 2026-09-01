using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The three <c>AutoAim</c> argument clamps every steering path was open-coding.
    ///
    /// These bounds - the fixed-wing floor at <c>maxRadius</c> and ceiling at 8 km, the
    /// rotary floor at the airframe's <c>minimumRadarAlt</c>, the pursuit-bank cap - are
    /// the fiddly numbers the modding notes warn about, and they were copied verbatim
    /// across eight-plus call sites. One home means one place to get them right.
    /// </summary>
    internal static class AutopilotMath
    {
        /// <summary>
        /// A held-altitude value for a fixed-wing <c>AutoAim</c>: never below the airframe's
        /// own turn-radius floor, never above 8 km.
        /// </summary>
        public static float CruiseHold(Aircraft aircraft, float desired) =>
            Mathf.Clamp(desired, aircraft.maxRadius, 8000f);

        /// <summary>
        /// A height-above-ground value for a rotary <c>AutoAim</c>: at least the airframe's
        /// <c>minimumRadarAlt</c>, then clamped into a sensible band for the task.
        /// </summary>
        public static float RotaryAgl(Aircraft aircraft, float desired,
                                      float min = 25f, float max = 3000f) =>
            Mathf.Clamp(Mathf.Max(aircraft.GetAircraftParameters().minimumRadarAlt, desired),
                        min, max);

        /// <summary>Bank authority for a pursuing turn, capped below inversion.</summary>
        public static float PursuitBank() =>
            Mathf.Min(Plugin.Config2.PursuitBankDegrees.Value, FixedWingFormation.MaxSafeBank);
    }
}
