using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Formation flight for helicopters and tiltwings, written against how the rotary
    /// autopilot actually behaves rather than as a variation on the fixed-wing one.
    ///
    /// Three facts drive everything here:
    ///
    /// 1. <c>Autopilot.Hover</c> is a real position-hold: clamped error, a derivative term,
    ///    collective straight from altitude error, and yaw from the aim direction. It is
    ///    what the stock transport and landing states use to sit precisely on a point, and
    ///    it is the right tool for holding a slot. Its ceiling is roughly seventeen degrees
    ///    of tilt, so it cannot cruise.
    ///
    /// 2. <c>AutopilotHelo.AutoAim</c> can cruise, but sets collective from
    ///    <c>0.5 + distance*0.001 - speed*0.02</c>. The distance to the destination *is* the
    ///    power command, so it has to be about twenty times the speed for the terms to
    ///    cancel and collective to rest at hover.
    ///
    /// 3. Because of (2), the destination's distance and its direction were doing two
    ///    different jobs through one vector, and pulling against each other: holding speed
    ///    demanded a far destination, which made every cross-track correction a tiny angle
    ///    — about five degrees in practice. They are set independently now. Distance still
    ///    comes from the power law; direction is a commanded heading offset.
    ///
    /// Worth knowing before tuning any of this: <c>AutopilotHelo</c> recomputes its forward
    /// waypoint only once per second and rate-limits it to 0.8 rad. That is a hard ceiling
    /// on rotary responsiveness which nothing here can raise.
    /// </summary>
    internal static class RotaryFormation
    {
        internal enum Mode
        {
            /// <summary>Leader slow or stationary: hold the slot as a point in space.</summary>
            Hover,

            /// <summary>Leader cruising: fly the leader's heading and close laterally.</summary>
            Cruise,
        }

        /// <summary>
        /// Shortest destination distance, in metres. Much below this the collective law
        /// reads it as an instruction to come down.
        /// </summary>
        private const float MinPowerDistance = 600f;

        /// <summary>Heading-correction gain per metre of cross-track error, in degrees.</summary>
        private const float CrossGain = 0.35f;

        /// <summary>Damping gain on cross-track closing rate, in degrees per m/s.</summary>
        private const float CrossDamping = 1.4f;

        /// <summary>
        /// Slot error at which a helicopter counts as on station, as a multiple of its own
        /// slot spacing. An absolute value does not work here: rotary slots sit at about
        /// half the fixed-wing spacing, so the old 200 m threshold called a helicopter
        /// settled while it was three slot-widths out of place.
        /// </summary>
        private const float StationSpacings = 1.5f;

        public static Mode Fly(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                               Vector3 toSlot, float distance, float slotStack, float spacing)
        {
            Vector3 heading = leader.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();

            if (leader.speed < Plugin.Config2.RotaryHoverSpeed.Value)
            {
                HoldPoint(aircraft, slotPos, heading);
                return Mode.Hover;
            }

            Cruise(aircraft, leader, toSlot, distance, slotStack, spacing, heading);
            return Mode.Cruise;
        }

        /// <summary>
        /// Slow or stationary leader: sit on the slot.
        ///
        /// Hover already contains everything needed — a clamped positional term, a
        /// derivative term that tracks a moving point, collective from altitude error, and
        /// yaw to the given direction. Passing zero for altitudeHold means "hold the slot's
        /// own altitude", which is exactly what a formation slot describes.
        /// </summary>
        private static void HoldPoint(Aircraft aircraft, GlobalPosition slotPos, Vector3 heading)
        {
            aircraft.autopilot.Hover(slotPos, 0f, heading);
        }

        /// <summary>
        /// Cruising leader: distance sets power, heading sets steering, and the two no
        /// longer fight each other.
        /// </summary>
        private static void Cruise(Aircraft aircraft, Aircraft leader, Vector3 toSlot,
                                   float distance, float slotStack, float spacing,
                                   Vector3 heading)
        {
            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            Vector3 leaderDir = leaderVel.sqrMagnitude > 1f ? leaderVel.normalized : heading;

            // --- Power ---
            // Twenty seconds of travel makes the autopilot's two collective terms cancel, so
            // it rests at hover power and can hold any speed. Being behind adds distance,
            // which asks for more power: that is how a wingman catches up rather than
            // settling into the gap.
            float alongTrack = Vector3.Dot(toSlot, leaderDir);
            float behind = Mathf.Max(0f, alongTrack);

            float sustain = leader.speed * Plugin.Config2.RotaryPowerSeconds.Value;
            float powerDistance = Mathf.Max(MinPowerDistance, sustain) + behind;

            // --- Steering ---
            // Only across-track error steers; the along-track part is already expressed in
            // the power distance above, and feeding it in twice had the destination fighting
            // itself. The correction is a heading angle, so it no longer shrinks as the
            // destination is pushed further out to hold speed.
            Vector3 across = toSlot - leaderDir * alongTrack;
            across.y = 0f;

            Vector3 drift = aircraft.rb != null ? aircraft.rb.velocity - leaderVel : Vector3.zero;
            drift.y = 0f;
            Vector3 acrossDrift = drift - leaderDir * Vector3.Dot(drift, leaderDir);

            // Sign the error and its rate about the vertical axis, so a positive command is
            // always a turn towards the slot.
            Vector3 rightOfTrack = Vector3.Cross(Vector3.up, leaderDir);
            float crossError = Vector3.Dot(across, rightOfTrack);
            float crossRate = Vector3.Dot(acrossDrift, rightOfTrack);

            float maxAngle = Plugin.Config2.RotaryCommandAngle.Value;
            float command = Mathf.Clamp(
                crossError * CrossGain * Plugin.Config2.Aggression.Value
                    - crossRate * CrossDamping * Plugin.Config2.Damping.Value,
                -maxAngle, maxAngle);

            Vector3 steer = Quaternion.AngleAxis(command, Vector3.up) * leaderDir;

            GlobalPosition destination = aircraft.GlobalPosition() + steer * powerDistance;

            // altitudeHold is a height above ground here, used both for the forward-flight
            // waypoint and the collective error, so it has to describe where the slot sits
            // above terrain rather than where the leader does.
            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Clamp(
                Mathf.Max(p.minimumRadarAlt, leader.radarAlt + slotStack), 25f, 3000f);

            // Hold the leader's heading only once roughly in place. While still closing,
            // leaving yaw to the autopilot lets the aircraft point where it is going rather
            // than crab sideways across the gap, which costs it speed it cannot spare.
            bool onStation = distance < spacing * StationSpacings;

            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: agl,
                aimDirection: onStation ? heading : Vector3.zero,
                targetVelocity: leaderVel,
                followTerrain: true);
        }
    }
}
