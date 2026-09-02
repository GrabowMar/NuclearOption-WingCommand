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
    internal class OrbitState : WingPilotState
    {
        private GlobalPosition anchor;
        private float radius;
        private const float EngageInterval = 0.35f;

        /// <summary>
        /// Shooting while holding. The same routine the formation slot uses - this state
        /// used to carry its own copy, which had drifted into asking the standing rules of
        /// engagement directly instead of asking what the wingman was actually doing, and
        /// so was the one shooting state a defensive behaviour could not silence.
        /// </summary>
        private readonly SlotEngagement engagement = new SlotEngagement(EngageInterval);

        public OrbitState(WingMember member) : base(member)
        {
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
            BeginFlight(pilot);

            if (radius <= 0f) radius = WingTuning.OrbitRadius;

            if (Plugin.Settings.VerboseLogging.Value)
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

            // Nothing here touches attitude or throttle, so holding the ring and shooting
            // from it never compete.
            engagement.Run(member, aircraft, pilot, member.Leader);
        }
    }
}
