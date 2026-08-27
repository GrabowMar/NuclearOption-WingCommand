using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Set a helicopter down where it is.
    ///
    /// Distinct from Return To Base, which uses the stock <c>AIHeloLandingState</c> and
    /// always routes to an airbase. This is for putting an aircraft on the ground here —
    /// behind a ridge, beside a position — and there is no stock state for it.
    ///
    /// <c>Autopilot.Hover</c> does the work: it is a proper position hold with a clamped
    /// positional term, a derivative term, collective from altitude error and yaw from the
    /// aim direction, and it is what the stock landing and transport states use to sit
    /// precisely on a point. Feeding it a descending altitude walks the aircraft down;
    /// there is nothing else to write.
    /// </summary>
    internal class LandInPlaceState : PilotBaseState
    {
        /// <summary>Descent rate, in metres per second.</summary>
        private const float DescentRate = 3f;

        /// <summary>Radar altitude at which the aircraft is considered down.</summary>
        private const float TouchdownAlt = 1.5f;

        /// <summary>Ground speed below which the aircraft is allowed to start descending.</summary>
        private const float SettleSpeed = 6f;

        private readonly WingMember member;

        private GlobalPosition spot;
        private Vector3 facing;
        private float hold;
        private bool down;

        public LandInPlaceState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "landing";
        }

        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();

            aircraft.SetFlightAssist(enabled: true);
            aircraft.SetGear(deployed: true);

            // Anchor at the ground beneath the aircraft, not at the aircraft itself.
            // Hover adds altitudeHold to the destination's own height difference, so with
            // the anchor on the deck the held height IS the altitudeHold argument, and
            // winding it down to zero is the descent.
            spot = aircraft.GlobalPosition() - Vector3.up * aircraft.radarAlt;
            hold = Mathf.Max(aircraft.radarAlt, 5f);

            facing = aircraft.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            down = false;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {aircraft.unitName} landing in place from {hold:F0} m");
        }

        public override void LeaveState()
        {
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            if (down)
            {
                // Stay put. Collective at zero with the brake on, so it does not creep or
                // get nudged back into the air by its own rotor wash.
                controlInputs.throttle = 0f;
                controlInputs.brake = 1f;
                return;
            }

            if (aircraft.radarAlt <= TouchdownAlt)
            {
                down = true;
                WingComms.Say(member, WingComms.Call.Down);

                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"[Wing] {aircraft.unitName} is down");

                return;
            }

            // Come to a stop before descending. Descending while still translating is how a
            // helicopter arrives somewhere other than where it was aimed, and fast.
            if (aircraft.speed < SettleSpeed)
                hold = Mathf.Max(0f, hold - DescentRate * Time.fixedDeltaTime);

            aircraft.autopilot.Hover(spot, hold, facing);
        }
    }
}
