using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Formation flight for helicopters, built around how a helicopter actually moves
    /// rather than reusing the fixed-wing pursuit loop.
    ///
    /// The central idea: a helicopter's natural command is a VELOCITY, not a heading. It can
    /// produce horizontal thrust in any direction by tilting its rotor, so the controller
    /// asks for a velocity — match the leader, plus a correction proportional to how far the
    /// slot is — and hands that to the autopilot as both a direction of travel and a power
    /// setting. When the wingman is on station the demanded velocity is simply the leader's;
    /// when it is off station the correction term crabes it back, sideways included.
    ///
    /// The one obstacle the game puts in this path: <c>AutopilotHelo.AutoAim</c> recomputes
    /// its steering waypoint only once per second and rate-limits it, so the direction a
    /// cruising helicopter actually flies lags the commanded direction by a second or more.
    /// That lag is removed by seeding the waypoint's start direction with the commanded one
    /// (see <c>targetVelocity</c> below), so the waypoint points where we asked on the very
    /// first update.
    ///
    /// Slow flight uses <c>Autopilot.Hover</c>, the game's real position hold — no waypoint,
    /// instantaneous tilt — which is exactly the helicopter behaviour for a near-stationary
    /// leader.
    /// </summary>
    internal static class RotaryFormation
    {
        internal enum Mode
        {
            /// <summary>Leader slow or stationary: hold the slot as a point in space.</summary>
            Hover,

            /// <summary>Leader moving: match the leader's velocity and close on the slot.</summary>
            Cruise,
        }

        /// <summary>
        /// Shortest destination distance, in metres. The autopilot's collective law reads the
        /// destination distance as a power command; much below this it reads it as an
        /// instruction to descend.
        /// </summary>
        private const float MinPowerDistance = 600f;

        /// <summary>Slot error at which a helicopter counts as on station, as a multiple of its own spacing.</summary>
        private const float StationSpacings = 1.5f;

        /// <summary>
        /// Hysteresis on the hover/cruise switch, in m/s. The two modes hold the slot
        /// differently, so a leader hovering at the threshold should not flap the wingman
        /// between them.
        /// </summary>
        private const float HoverHysteresis = 3f;

        /// <summary>Seconds of leader vertical speed fed into the altitude hold, so climbs and dives are followed rather than trailed.</summary>
        private const float AltitudeLeadSeconds = 1f;

        /// <summary>
        /// Closing speed commanded per metre of slot error, in (m/s)/m. This is the position
        /// loop: it makes the demanded velocity converge on the leader's with a first-order
        /// response (time constant ~ 1/gain), which is monotonic — no rate term, no
        /// overshoot, no catch-and-fall cycle.
        /// </summary>
        private const float FollowGain = 0.4f;

        /// <summary>
        /// Steer one wingman. <paramref name="previous"/> is the mode flown last frame, for
        /// the hover/cruise hysteresis. Reports the horizontal slot error through
        /// <paramref name="horizontalError"/>.
        /// </summary>
        public static Mode Fly(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                               Vector3 toSlot, float distance, float slotStack, float spacing,
                               Mode previous, out float horizontalError)
        {
            Vector3 heading = leader.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();

            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            Vector3 leaderVelFlat = leaderVel;
            leaderVelFlat.y = 0f;

            // Horizontal error to the slot, and the direction straight at it.
            Vector3 toSlotFlat = toSlot;
            toSlotFlat.y = 0f;
            float flat = toSlotFlat.magnitude;
            horizontalError = flat;

            Vector3 slotDir = flat > 0.5f ? toSlotFlat / flat : heading;

            // A rotary wingman may use the leader's hover regime only after it has actually
            // reached the slot. Hover is an excellent position hold but a poor long-range
            // rejoin command: selecting it merely because the leader stopped left aircraft
            // hanging hundreds of metres away instead of closing into formation.
            float hoverSpeed = Plugin.Config2.RotaryHoverSpeed.Value;
            bool wasHovering = previous == Mode.Hover;
            bool onStation = flat < spacing * StationSpacings;

            // Use horizontal velocity rather than Aircraft.speed. A helicopter climbing or
            // settling vertically beside the player is still hovering for formation
            // purposes, and should not make every wingman alternate into cruise.
            if (RotaryHoverPolicy.ShouldHover(
                    wasHovering, leaderVelFlat.magnitude, flat, spacing, hoverSpeed,
                    HoverHysteresis, StationSpacings))
            {
                // The game's real position hold: instant tilt, no waypoint lag. Face the
                // direction of travel while closing, then swing onto the leader's heading on
                // station — that swing is what a helicopter does on the pad.
                Vector3 lookDir = onStation ? heading : slotDir;
                aircraft.autopilot.Hover(slotPos, 0f, lookDir);
                return Mode.Hover;
            }

            Cruise(aircraft, leader, toSlotFlat, flat, slotDir, heading, leaderVel,
                   leaderVelFlat, spacing, slotStack, onStation);
            return Mode.Cruise;
        }

        /// <summary>
        /// Cruising leader: demand the leader's velocity plus a correction toward the slot,
        /// and hand that to the autopilot as a direction of travel and a power setting.
        /// </summary>
        private static void Cruise(Aircraft aircraft, Aircraft leader, Vector3 toSlotFlat,
                                   float flat, Vector3 slotDir, Vector3 heading,
                                   Vector3 leaderVel, Vector3 leaderVelFlat,
                                   float spacing, float slotStack, bool onStation)
        {
            // --- The commanded velocity. ---
            // Match the leader, and add a correction proportional to the gap. On station the
            // correction is nil and this is just the leader's velocity; off station it points
            // back at the slot, so the helicopter crabes home — sideways and backwards
            // included, because that is what a helicopter does.
            Vector3 vDes = leaderVelFlat + toSlotFlat * FollowGain;

            float vDesMag = vDes.magnitude;
            Vector3 moveDir = vDesMag > 1f ? vDes / vDesMag : slotDir;

            // --- Power: the destination distance IS the collective command. ---
            // Twenty seconds of travel makes the autopilot's two collective terms cancel, so
            // it rests at hover power and can hold the commanded speed; a larger gap (via
            // vDes) automatically asks for more power.
            float sustain = Mathf.Max(vDesMag, leader.speed) * Plugin.Config2.RotaryPowerSeconds.Value;
            float powerDistance = Mathf.Max(MinPowerDistance, sustain);

            GlobalPosition destination = aircraft.GlobalPosition() + moveDir * powerDistance;

            // --- Kill the waypoint lag. ---
            // The autopilot builds its steering waypoint from
            //     current = (ownVelocity - targetVelocity) + forward * 20
            // and then rotates that toward the destination at a capped rate, once a second.
            // Feeding it a targetVelocity that already points the result at the destination
            // makes the waypoint land on the commanded direction immediately, removing the
            // rate-limit lag that otherwise trails every manoeuvre.
            Vector3 targetVel = aircraft.rb.velocity
                              + aircraft.transform.forward * 20f
                              - moveDir * powerDistance;

            // altitudeHold is a height above ground here, so it must describe where the slot
            // sits above terrain, led by the leader's vertical speed so a climb is followed.
            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Clamp(
                Mathf.Max(p.minimumRadarAlt,
                          leader.radarAlt + slotStack + leaderVel.y * AltitudeLeadSeconds),
                25f, 3000f);

            // Nose: hold the leader's heading on station; otherwise let the helicopter point
            // where it is going.
            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: agl,
                aimDirection: onStation ? heading : Vector3.zero,
                targetVelocity: targetVel,
                followTerrain: true);
        }
    }
}
