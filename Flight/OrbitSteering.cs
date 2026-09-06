using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Steers fixed-wing and rotary aircraft around an anchor using their native autopilots.
    /// </summary>
    internal static class OrbitSteering
    {
        /// <summary>Height above the anchor that fixed-wing aircraft hold, in metres.</summary>
        private const float FixedWingAltitude = 1500f;

        /// <summary>Height above the anchor that rotary aircraft hold, in metres.</summary>
        private const float RotaryAltitude = 250f;

        /// <summary>
        /// Steer one aircraft around <paramref name="anchor"/>.
        /// </summary>
        /// <param name="slot">
        /// Roster slot selects a separate holding radius; every aircraft turns the same way.
        /// </param>
        public static void Fly(Aircraft aircraft, ControlInputs controls,
                               GlobalPosition anchor, float radius, int slot)
        {
            if (aircraft == null) return;

            bool rotary = WingRegistry.IsRotary(aircraft);

            Vector3 fromAnchor = aircraft.GlobalPosition() - anchor;
            float spacing = WingFormation.SlotSpacing * (rotary ? WingTuning.RotarySpacingScale : 1f);
            var aim = OrbitGeometry.AimOffset(fromAnchor.x, fromAnchor.z, radius, slot, spacing);
            Vector3 ring = new Vector3(aim.x, 0f, aim.z);

            // A host profile may raise the ring - a wing overwatching a warship wants
            // separation from the ship's own mast and missiles. Only fixed-wing takes it:
            // the rotary figure is tied to RotaryAgl and terrain following, and a
            // helicopter told to orbit at jet height is outside what that autopilot holds.
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

            // Cruise power. Orbiting is a holding pattern, not a race.
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
