using UnityEngine;

namespace WingCommand
{
 /// <summary>Native fixed-wing and rotary steering around an orbit anchor.</summary>
    internal static class OrbitSteering
    {
     /// <summary>Fixed-wing orbit height above anchor, in metres.</summary>
        private const float FixedWingAltitude = 1500f;

     /// <summary>Rotary orbit height above anchor, in metres.</summary>
        private const float RotaryAltitude = 250f;

     /// <summary>Steer an aircraft around anchor.</summary> <param name="slot">Selects a separate orbit
     /// radius; all members turn in the same direction.</param>
        public static void Fly(Aircraft aircraft, ControlInputs controls,
                               GlobalPosition anchor, float radius, int slot)
        {
            if (aircraft == null) return;

            bool rotary = WingRegistry.IsRotary(aircraft);

            Vector3 fromAnchor = aircraft.GlobalPosition() - anchor;
            float spacing = WingFormation.SlotSpacing * (rotary ? WingTuning.RotarySpacingScale : 1f);
            var aim = OrbitGeometry.AimOffset(fromAnchor.x, fromAnchor.z, radius, slot, spacing);
            Vector3 ring = new Vector3(aim.x, 0f, aim.z);

            // Apply host overwatch height only to fixed-wing rings. Rotary height remains within its
            // terrain-following AGL policy.
            float overwatch = WingHost.Current.OverwatchAltitude;
            float altitude = rotary
                ? RotaryAltitude
                : (overwatch > 0f ? overwatch : FixedWingAltitude);
            GlobalPosition target = anchor + ring + Vector3.up * altitude;

            if (rotary)
            {
                aircraft.autopilot.AutoAim(
                    destination: target,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, RotaryAltitude),
                    aimDirection: Vector3.zero,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
                return;
            }

            // Use cruise throttle for holding patterns.
            controls.throttle = Mathf.Clamp01(aircraft.GetAircraftParameters().cruiseThrottle);

            aircraft.autopilot.AutoAim(
                destination: target,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: FixedWingFormation.GroundLimitedBank(
                    aircraft.radarAlt, FixedWingFormation.MaxSafeBank,
                    aircraft.rb != null ? aircraft.rb.velocity.y : 0f),
                followTerrain: false,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, altitude),
                targetVelocity: Vector3.zero);
        }
    }
}
