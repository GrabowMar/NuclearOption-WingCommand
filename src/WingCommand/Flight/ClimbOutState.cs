using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Wings-level climb. No slot chase, no orbit, no intercept.
    ///
    /// The formation law is a station-keeper: it rotates velocity toward the slot and
    /// banks to chase it. That is the wrong controller for an aircraft that has just left
    /// the runway 10 km from the leader. This state aims ahead and up, with bank authority
    /// small enough that AutoAim cannot roll through the horizon, until the climb-out
    /// reflex releases.
    /// </summary>
    internal class ClimbOutState : WingPilotState
    {
        /// <summary>Seconds of flight to aim ahead. Far enough that AutoAim's gain stays low.</summary>
        private const float LookAheadSeconds = 8f;

        /// <summary>Shortest look-ahead, so a slow rotary still has a destination.</summary>
        private const float MinLookAhead = 600f;

        /// <summary>Metres of extra height put on the aim point each tick.</summary>
        private const float ClimbBias = 250f;

        /// <summary>Bank the autopilot may use. Enough to stay wings-level, not to turn.</summary>
        private const float Bank = 12f;

        public ClimbOutState(WingMember member) : base(member)
        {
            stateDisplayName = "Climbing";
        }

        public override void EnterState(Pilot pilot)
        {
            BindControls(pilot);
            HoverAssist.Release(aircraft);
            RetractGearIfClear();
            if (pilot != null && pilot.flightInfo != null)
                pilot.flightInfo.HasTakenOff = true;
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo("[Wing] " + aircraft.unitName + " climbing out before joining");
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

            RetractGearIfClear();

            Vector3 forward = aircraft.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float lookAhead = Mathf.Max(aircraft.speed * LookAheadSeconds, MinLookAhead);
            float climb = ClimbBias;
            Aircraft leader = member.Leader;
            if (leader != null && !leader.disabled)
                climb = Mathf.Max(climb, Mathf.Min(leader.radarAlt - aircraft.radarAlt, 600f));
            if (climb < 80f) climb = 80f;

            GlobalPosition destination = aircraft.GlobalPosition()
                                         + forward * lookAhead
                                         + Vector3.up * climb;

            if (WingRegistry.IsRotary(aircraft))
            {
                float agl = AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt + climb);
                aircraft.autopilot.AutoAim(
                    destination: destination,
                    altitudeHold: agl,
                    aimDirection: Vector3.zero,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
                return;
            }

            if (controlInputs != null)
                controlInputs.throttle = 1f;

            aircraft.autopilot.AutoAim(
                destination: destination,
                aimVelocity: true,
                ignoreCollisions: true,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: Bank,
                followTerrain: false,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, aircraft.radarAlt + climb),
                targetVelocity: Vector3.zero);
        }

        /// <summary>
        /// Gear stays down on the runway. Retracting it here is only safe once the
        /// airframe has actually left the ground.
        /// </summary>
        private void RetractGearIfClear()
        {
            if (aircraft == null || aircraft.autopilot == null) return;
            if (aircraft.radarAlt < WingTuning.ClimbOutGearAlt) return;
            if (aircraft.gearState == LandingGear.GearState.LockedRetracted) return;
            aircraft.SetGear(deployed: false);
        }
    }
}
