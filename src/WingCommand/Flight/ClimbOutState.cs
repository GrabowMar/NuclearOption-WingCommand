using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Short departure and energy recovery. Below clearance it flies ahead; once clear,
    /// a shallow, bounded turn starts the rendezvous while the aircraft builds speed.
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
                Plugin.Logger.LogInfo("[Wing] " + aircraft.unitName + " departure: accelerating and climbing toward leader");
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
            bool isRotary = WingRegistry.IsRotary(aircraft);
            float climb = isRotary ? ClimbBias : lookAhead * Mathf.Tan(WingTuning.DeparturePitch * Mathf.Deg2Rad);
            Aircraft leader = member.Leader;
            if (isRotary && leader != null && !leader.disabled)
                climb = Mathf.Max(climb, Mathf.Min(leader.radarAlt - aircraft.radarAlt, 600f));
            if (climb < 80f) climb = 80f;

            AircraftParameters parameters = aircraft.GetAircraftParameters();
            if (!isRotary && ClimbOutPolicy.ShouldAccelerateLevel(
                    aircraft.radarAlt, aircraft.speed,
                    parameters != null ? parameters.takeoffSpeed : 70f))
                climb = 0f;

            // Start the rendezvous during the safety climb. Below flying speed or
            // terrain clearance, retain the straight departure heading.
            float bank = Bank;
            if (!isRotary && leader != null && !leader.disabled &&
                aircraft.radarAlt >= WingTuning.FixedWingAirborneAlt &&
                aircraft.speed >= (parameters != null ? parameters.takeoffSpeed : 70f) * WingTuning.LaunchSpeedMargin)
            {
                Vector3 toLeader = leader.GlobalPosition() - aircraft.GlobalPosition();
                FormationControlRules.SafeRejoinDirection(
                    forward.x, 0f, forward.z, toLeader.x, 0f, toLeader.z,
                    WingTuning.DepartureTurnBank, 0f, 0f, aircraft.radarAlt,
                    out float x, out _, out float z);
                Vector3 course = new Vector3(x, 0f, z);
                bank = Mathf.Clamp(Vector3.Angle(forward, course) * 2f, Bank, WingTuning.DepartureTurnBank);
                forward = course;
            }

            GlobalPosition destination = aircraft.GlobalPosition()
                                         + forward * lookAhead
                                         + Vector3.up * climb;

            if (isRotary)
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
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: FormationControlRules.BankInput(bank, aircraft.radarAlt),
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
