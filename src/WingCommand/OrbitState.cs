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
        private float lastEngageCheck;
        private float lastFiredTime;

        private const float EngageInterval = 0.35f;

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
            lastEngageCheck = 0f;
            lastFiredTime = 0f;

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
            RunEngagement();
        }

        /// <summary>Apply the standing ROE while holding instead of orbiting inertly.</summary>
        private void RunEngagement()
        {
            if (Time.timeSinceLevelLoad - lastEngageCheck < EngageInterval) return;
            lastEngageCheck = Time.timeSinceLevelLoad;

            WingRoe roe = RoeRules.Current;
            WingWeapons.Allow allow = RoeRules.WeaponsFree(roe, aircraft);
            float range = RoeRules.EngageRange(roe);
            bool fired = false;

            if (allow == WingWeapons.Allow.MissilesOnly)
            {
                if (Time.timeSinceLevelLoad - lastFiredTime >= 1f)
                    fired = WingWeapons.Engage(aircraft, pilot, allow, range);
                if (fired) WingComms.Say(member, WingComms.Call.Defending);
            }
            else if (Time.timeSinceLevelLoad - lastFiredTime >= WingWeapons.FireInterval(aircraft))
            {
                Unit target = null;
                Aircraft leader = member.Leader;
                if (leader != null && RoeRules.GuardsLeader(roe))
                    target = WingWeapons.NearestThreatTo(leader, range);

                fired = target != null
                    ? WingWeapons.EngageSpecific(aircraft, pilot, target, range)
                    : WingWeapons.Engage(aircraft, pilot, allow, range);
            }

            if (fired) lastFiredTime = Time.timeSinceLevelLoad;
        }
    }
}
