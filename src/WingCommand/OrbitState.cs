using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Hold a circle over a fixed point until recalled.
    ///
    /// Used directly by the Orbit Here order, and as the final phase of
    /// <see cref="FallBackState"/> once a wingman has egressed to its rally point. The
    /// anchor is captured when the order is given, not tracked live — that is the whole
    /// point of the order: the wing stays over somewhere while the player goes elsewhere.
    /// </summary>
    internal class OrbitState : PilotBaseState
    {
        private readonly WingMember member;
        private GlobalPosition anchor;
        private float radius;

        public OrbitState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "orbiting";
        }

        /// <summary>Set the point to hold over. Call before switching to this state.</summary>
        public void SetAnchor(GlobalPosition point, float orbitRadius)
        {
            anchor = point;
            radius = Mathf.Max(orbitRadius, 200f);
        }

        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();

            aircraft.SetFlightAssist(enabled: true);
            if (aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            pilot.flightInfo.HasTakenOff = true;

            if (radius <= 0f) radius = Plugin.Config2.OrbitRadius.Value;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {aircraft.unitName} orbiting at {radius:F0} m");
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

            // Spread the wing around the ring by slot, so three aircraft holding the same
            // point do not end up flying the same arc nose to tail.
            float phase = member.Slot * 120f;

            OrbitSteering.Fly(aircraft, controlInputs, anchor, radius, phase);
        }
    }
}
