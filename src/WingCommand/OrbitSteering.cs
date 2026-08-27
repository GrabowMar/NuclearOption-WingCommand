using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Fly a circle around a fixed point on the ground.
    ///
    /// Two orders need this and they need it identically: Orbit Here anchors to where the
    /// player was when the order was given, and Fall Back holds over its rally point once
    /// the egress is done. Written once, dispatched to the two autopilots the same way
    /// <see cref="FormationFlyState"/> does, because they answer to different commands.
    /// </summary>
    internal static class OrbitSteering
    {
        /// <summary>Height above the anchor that fixed-wing aircraft hold, in metres.</summary>
        private const float FixedWingAltitude = 1500f;

        /// <summary>Height above the anchor that rotary aircraft hold, in metres.</summary>
        private const float RotaryAltitude = 250f;

        /// <summary>
        /// How far ahead around the circle to aim. Aiming at the nearest point on the ring
        /// makes an aircraft fly at it and then have to turn hard; aiming a quarter turn
        /// ahead makes it fly the tangent, which is what an orbit actually is.
        /// </summary>
        private const float LeadAngle = 70f;

        /// <summary>
        /// Steer one aircraft around <paramref name="anchor"/>.
        /// </summary>
        /// <param name="phase">
        /// Per-aircraft angular offset in degrees, so several aircraft orbiting the same
        /// point spread around the ring instead of stacking on one another.
        /// </param>
        public static void Fly(Aircraft aircraft, ControlInputs controls,
                               GlobalPosition anchor, float radius, float phase)
        {
            if (aircraft == null) return;

            bool rotary = !(aircraft.autopilot is AutopilotPlane);

            // Where the aircraft sits around the ring right now.
            Vector3 fromAnchor = aircraft.GlobalPosition() - anchor;
            fromAnchor.y = 0f;
            if (fromAnchor.sqrMagnitude < 1f) fromAnchor = Vector3.forward;

            float bearing = Mathf.Atan2(fromAnchor.z, fromAnchor.x) * Mathf.Rad2Deg;

            // Aim at a point further round the circle, offset by this aircraft's phase so
            // the wing spreads out rather than orbiting nose to tail.
            float aimBearing = (bearing + LeadAngle + phase) * Mathf.Deg2Rad;
            Vector3 ring = new Vector3(Mathf.Cos(aimBearing), 0f, Mathf.Sin(aimBearing)) * radius;

            float altitude = rotary ? RotaryAltitude : FixedWingAltitude;
            GlobalPosition target = anchor + ring + Vector3.up * altitude;

            if (rotary)
            {
                AircraftParameters p = aircraft.GetAircraftParameters();
                float agl = Mathf.Clamp(Mathf.Max(p.minimumRadarAlt, RotaryAltitude), 25f, 3000f);

                aircraft.autopilot.AutoAim(
                    destination: target,
                    altitudeHold: agl,
                    aimDirection: Vector3.zero,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
                return;
            }

            // Cruise power. Orbiting is a holding pattern, not a race.
            AircraftParameters fp = aircraft.GetAircraftParameters();
            controls.throttle = Mathf.Clamp01(fp.cruiseThrottle);

            aircraft.autopilot.AutoAim(
                destination: target,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: 120f,
                followTerrain: false,
                altitudeHold: Mathf.Clamp(altitude, aircraft.maxRadius, 8000f),
                targetVelocity: Vector3.zero);
        }
    }
}
