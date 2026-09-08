using UnityEngine;

namespace WingCommand
{
    /// <summary>Rotary station keeping commands leader velocity plus slot-error correction. Cruise seeds
    /// native waypoint direction to reduce its one-second steering lag; slow near-slot flight uses native
    /// Hover position hold.</summary>
    internal static class RotaryFormation
    {
        internal enum Mode
        {
            /// <summary>Hold the slot as a point near a slow leader.</summary>
            Hover,

            /// <summary>Match moving-leader velocity with slot closure.</summary>
            Cruise,
        }

        /// <summary>Minimum aim distance in metres; shorter destinations make native collective command
        /// descent.</summary>
        private const float MinPowerDistance = 600f;

        /// <summary>On-station error radius in multiples of rotary spacing.</summary>
        private const float StationSpacings = 1.5f;

        /// <summary>Hover/cruise speed hysteresis in m/s to prevent threshold chatter.</summary>
        private const float HoverHysteresis = 3f;

        /// <summary>Seconds of leader climb feed-forward in altitude hold.</summary>
        private const float AltitudeLeadSeconds = 1f;

        /// <summary>Velocity correction per metre of slot error, in (m/s)/m; sets the proportional
        /// position response rate.</summary>
        private const float FollowGain = 0.4f;

        /// <summary>Steer the member using previous mode for hover hysteresis; output horizontal slot
        /// error.</summary>
        public static Mode Fly(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                               Vector3 toSlot, float distance, float spacing,
                               Mode previous, LeaderState leaderState, out float horizontalError)
        {
            Vector3 heading = leader.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();

            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            Vector3 leaderVelFlat = leaderVel;
            leaderVelFlat.y = 0f;

            // Project slot error onto the horizontal plane.
            Vector3 toSlotFlat = toSlot;
            toSlotFlat.y = 0f;
            float flat = toSlotFlat.magnitude;
            horizontalError = flat;

            Vector3 slotDir = flat > 0.5f ? toSlotFlat / flat : heading;

            // Enter hover only near the slot; a stopped leader must not strand distant members in
            // long-range position hold.
            float hoverSpeed = WingTuning.RotaryHoverSpeed;
            bool wasHovering = previous == Mode.Hover;
            bool onStation = flat < spacing * StationSpacings;

            // Use horizontal speed for hover decisions; vertical climb or descent still counts as
            // hovering.
            if (RotaryHoverPolicy.ShouldHover(
                    wasHovering, leaderVelFlat.magnitude, flat, spacing, hoverSpeed,
                    HoverHysteresis, StationSpacings))
            {
                // Use native immediate position hold; face the slot while closing and leader heading
                // once settled.
                Vector3 lookDir = onStation ? heading : slotDir;
                HoverAssist.Hover(aircraft, slotPos, 0f, lookDir);
                return Mode.Hover;
            }

            // Release hover configuration for cruise so vectoring nozzles can return forward.
            HoverAssist.Release(aircraft);

            Cruise(aircraft, leader, slotPos, toSlotFlat, flat, slotDir, heading, leaderVel,
                   leaderVelFlat, spacing, onStation, leaderState);
            return Mode.Cruise;
        }

        /// <summary>Convert predicted leader velocity plus slot correction into native cruise direction
        /// and power.</summary>
        private static void Cruise(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                                   Vector3 toSlotFlat,
                                   float flat, Vector3 slotDir, Vector3 heading,
                                   Vector3 leaderVel, Vector3 leaderVelFlat,
                                   float spacing, bool onStation,
                                   LeaderState leaderState)
        {
            // Add proportional slot closure to predicted leader velocity. Acceleration lead covers
            // rotor tilt and native waypoint-response lag.
            Vector3 vDes = leaderVelFlat
                         + leaderState.FlatAcceleration * WingTuning.RotarySpeedLeadSeconds
                         + toSlotFlat * FollowGain;

            float vDesMag = vDes.magnitude;
            Vector3 moveDir = vDesMag > 1f ? vDes / vDesMag : slotDir;

            // Use destination distance as collective demand: travel-time scaling balances native terms
            // near hover power and adds power with desired speed.
            float sustain = Mathf.Max(vDesMag, leader.speed) * WingTuning.RotaryPowerSeconds;
            float powerDistance = Mathf.Max(MinPowerDistance, sustain);

            GlobalPosition destination = aircraft.GlobalPosition() + moveDir * powerDistance;

            // Choose targetVelocity so native waypoint construction already points along the commanded
            // direction, avoiding its rate-limited turn lag.
            Vector3 targetVel = aircraft.rb.velocity
                              + aircraft.transform.forward * 20f
                              - moveDir * powerDistance;

            // Convert terrain-floored slot height to local AGL because rotary terrain-following ignores
            // destination.y. Lead climbs without anticipating descent through terrain.
            float desiredAgl = RotaryAltitudePolicy.SlotAgl(
                aircraft.GlobalPosition().y, aircraft.radarAlt, slotPos.y,
                WingFidelity.TerrainClearance);
            desiredAgl += Mathf.Max(0f, leaderVel.y) * AltitudeLeadSeconds;
            float agl = AutopilotMath.RotaryAgl(aircraft, desiredAgl);

            // Face leader heading on station; otherwise face the direction of travel.
            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: agl,
                aimDirection: onStation ? heading : Vector3.zero,
                targetVelocity: targetVel,
                followTerrain: true);
        }
    }
}
