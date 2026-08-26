using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Formation flight for helicopters and tiltwings, written against how the rotary
    /// autopilot actually behaves rather than as a variation on the fixed-wing path.
    ///
    /// Four previous attempts failed because they reused aeroplane concepts — a capture
    /// radius, a fixed look-ahead, drift damping — on a controller that does not work that
    /// way. Two facts drive everything here:
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
    /// So there are two regimes, chosen by how fast the leader is going, and they fly
    /// genuinely differently: hold a point when slow, fly a heading when fast.
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

        public static Mode Fly(Aircraft aircraft, Aircraft leader, GlobalPosition slotPos,
                               Vector3 toSlot, float distance, float slotStack)
        {
            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;

            Vector3 heading = leader.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();

            bool hover = leader.speed < Plugin.Config2.RotaryHoverSpeed.Value;

            if (hover)
            {
                HoldPoint(aircraft, slotPos, heading);
                return Mode.Hover;
            }

            Cruise(aircraft, leader, toSlot, distance, slotStack, leaderVel, heading);
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
        /// Cruising leader: fly the leader's heading, and treat distance as the power
        /// command it actually is.
        /// </summary>
        private static void Cruise(Aircraft aircraft, Aircraft leader, Vector3 toSlot,
                                   float distance, float slotStack, Vector3 leaderVel,
                                   Vector3 heading)
        {
            Vector3 leaderDir = leaderVel.sqrMagnitude > 1f ? leaderVel.normalized : heading;

            // Distance sets collective. Twenty seconds of travel makes the autopilot's two
            // collective terms cancel, so it rests at hover power and can hold any speed;
            // anything shorter caps the speed a wingman can sustain.
            float sustain = leader.speed * Plugin.Config2.RotaryLookAheadSeconds.Value;

            // Being behind adds distance, which asks for more power — that is how a wingman
            // catches up instead of settling into the gap.
            float alongTrack = Vector3.Dot(toSlot, leaderDir);
            float behind = Mathf.Max(0f, alongTrack);

            float lookAhead = Mathf.Max(Plugin.Config2.RotaryMinLookAhead.Value, sustain) + behind;

            // Only across-track error steers. The along-track part is already expressed in
            // the look-ahead, and feeding it in twice had the destination fighting itself.
            Vector3 across = toSlot - leaderDir * alongTrack;
            Vector3 correction = Vector3.ClampMagnitude(
                across * Plugin.Config2.RotaryCrossGain.Value,
                Plugin.Config2.RotaryMaxCross.Value);

            GlobalPosition destination = aircraft.GlobalPosition() + leaderDir * lookAhead + correction;

            // altitudeHold is a height above ground here, used both for the forward-flight
            // waypoint and the collective error, so it has to describe where the slot sits
            // above terrain rather than where the leader does.
            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Clamp(
                Mathf.Max(p.minimumRadarAlt, leader.radarAlt + slotStack), 25f, 3000f);

            // Hold the leader's heading only once roughly in place. While still closing,
            // leaving yaw to the autopilot lets the aircraft point where it is going rather
            // than crab sideways across the gap, which costs it speed it cannot spare.
            bool onStation = distance < Plugin.Config2.RotaryStationDistance.Value;

            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: agl,
                aimDirection: onStation ? heading : Vector3.zero,
                targetVelocity: leaderVel,
                followTerrain: true);
        }
    }
}
