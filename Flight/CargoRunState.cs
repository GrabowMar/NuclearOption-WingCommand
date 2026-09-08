using UnityEngine;

namespace WingCommand
{
 /// <summary>Delivers cargo to a map point: fixed-wing aircraft release overhead, helicopters descend.
 /// Confirm drops through ammunition changes. If release stalls, relinquish the point and use native
 /// transport where supported.</summary>
    internal class CargoRunState : WingPilotState
    {
        private enum Phase { Transit, Deliver, Egress }

     /// <summary>Transit height to the drop point, in metres.</summary>
        private const float TransitAltitude = 140f;

     /// <summary>Helicopter stabilisation height before descent.</summary>
        private const float SettleAltitude = 24f;

     /// <summary>Settled descent rate in metres per second.</summary>
        private const float DescentRate = 3f;

     /// <summary>Maximum radar altitude for helicopter release.</summary>
        private const float ReleaseAltitude = 8f;

     /// <summary>Fixed-wing drop-run height.</summary>
        private const float DropRunAltitude = 260f;

     /// <summary>Distance in metres considered arrival over the point.</summary>
        private const float ArrivalRadius = 120f;

     /// <summary>Maximum distance from the point for fixed-wing release.</summary>
        private const float DropRadius = 250f;

     /// <summary>Delay between cargo release attempts, in seconds.</summary>
        private const float ReleaseInterval = 1.5f;

     /// <summary>Delivery timeout before handing a stalled load to native transport.</summary>
        private const float DeliverTimeout = 45f;

     /// <summary>Climb-out height after cargo release.</summary>
        private const float EgressAltitude = 220f;

        private GlobalPosition point;
        private Vector3 facing;
        private Phase phase;
        private float hold;
        private float lastRelease;
        private readonly CargoProgressTracker cargoProgress = new CargoProgressTracker();

        public CargoRunState(WingMember member) : base(member)
        {
            stateDisplayName = "delivering";
        }

     /// <summary>Set the drop destination before entering this state.</summary>
        public void SetDestination(GlobalPosition destination) => point = destination;

        public override void EnterState(Pilot pilot)
        {
            // Retain hover configuration for descent to the drop point.
            BeginFlight(pilot, releaseHover: false);

            // Resolve ground height beneath the point; hover adds its AGL hold to destination
            // elevation.
            point = GroundUnder(point);

            facing = aircraft.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            phase = Phase.Transit;
            hold = Mathf.Max(TransitAltitude, aircraft.radarAlt);
            lastRelease = 0f;
            cargoProgress.Reset(member.CargoAmmo, Time.timeSinceLevelLoad);

            WingComms.Say(member, WingComms.Call.Delivering);

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose(
                    $"[Cargo] {aircraft.unitName} running {cargoProgress.LastAmount} load(s) to the drop point");
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

            // After abandoning the point, wait quietly for arbitration to enter the native supply
            // route.
            if (!member.Directive.HasPoint) return;

            bool rotary = WingRegistry.IsRotary(aircraft);

            // Fly egress after unloading; WingMember.CheckCargoRun owns delivery confirmation and order
            // completion.
            if (member.CargoAmmo <= 0) phase = Phase.Egress;

            switch (phase)
            {
                case Phase.Transit:
                    Transit(rotary);
                    if (HorizontalDistance(aircraft.GlobalPosition(), point) <= ArrivalRadius)
                    {
                        phase = Phase.Deliver;
                        cargoProgress.Reset(member.CargoAmmo, Time.timeSinceLevelLoad);
                        hold = rotary ? SettleAltitude : hold;
                        if (rotary) aircraft.SetGear(deployed: true);
                    }
                    break;

                case Phase.Deliver:
                    if (rotary) DeliverRotary();
                    else DeliverFixedWing();
                    break;

                case Phase.Egress:
                    Egress(rotary);
                    break;
            }
        }

        // Transit flight.

        private void Transit(bool rotary)
        {
            // Release hover for both cruise routes to the drop point.
            HoverAssist.Release(aircraft);

            if (rotary)
            {
                aircraft.autopilot.AutoAim(
                    destination: point + Vector3.up * TransitAltitude,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, TransitAltitude, 40f, 1000f),
                    aimDirection: Vector3.zero,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
                return;
            }

            controlInputs.throttle = 0.75f;
            aircraft.autopilot.AutoAim(
                destination: point + Vector3.up * DropRunAltitude,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 1f,
                bankAllowed: Mathf.Min(WingTuning.StationBank,
                                       FixedWingFormation.MaxSafeBank),
                followTerrain: true,
                altitudeHold: Mathf.Max(DropRunAltitude, aircraft.maxRadius),
                targetVelocity: Vector3.zero);
        }

        // Cargo release.

     /// <summary>Stabilise overhead, descend, and release below the altitude threshold.</summary>
        private void DeliverRotary()
        {
            hold = Mathf.Max(0f, hold - DescentRate * Time.fixedDeltaTime);
            HoverAssist.Hover(aircraft, point, hold, facing);

            if (aircraft.radarAlt <= ReleaseAltitude) TryRelease();
            CheckStalled();
        }

     /// <summary>Fly over the point and release within the drop radius.</summary>
        private void DeliverFixedWing()
        {
            Transit(rotary: false);

            if (HorizontalDistance(aircraft.GlobalPosition(), point) <= DropRadius) TryRelease();
            CheckStalled();
        }

        private void TryRelease()
        {
            if (Time.timeSinceLevelLoad - lastRelease < ReleaseInterval) return;
            lastRelease = Time.timeSinceLevelLoad;

            WingWeapons.ReleaseCargo(aircraft, pilot);
        }

     /// <summary>Report a stalled drop and relinquish the point for native transport
     /// fallback.</summary>
        private void CheckStalled()
        {
            cargoProgress.Observe(member.CargoAmmo, Time.timeSinceLevelLoad);
            if (!cargoProgress.IsStalled(Time.timeSinceLevelLoad, DeliverTimeout)) return;

            if (pilot.AIHeloTransportState != null)
            {
                WingCommandManager.Instance?.Toast(
                    member.Name + " could not release at the drop point - running the " +
                    "standard supply route instead");
                Plugin.Logger.LogWarning(
                    "[Cargo] " + aircraft.unitName + " released nothing at the drop point; " +
                    "handing over to the stock transport state");

                // Clear the point through task completion so the arbiter performs the handoff and later
                // defence resumes the correct route.
                CompleteTask(WingDirective.Simple(WingOrder.DeliverCargo));
                return;
            }

            WingComms.Say(member, WingComms.Call.NoDropOff);
            WingCommandManager.Instance?.Toast(
                member.Name + " could not release its cargo at that point");
            CompleteTask(WingOrder.Formation);
        }

        // Departure from drop.

     /// <summary>Climb clear while waiting for order completion.</summary>
        private void Egress(bool rotary)
        {
            if (rotary)
            {
                if (aircraft.gearState != LandingGear.GearState.LockedRetracted &&
                    aircraft.radarAlt > ReleaseAltitude * 2f)
                    aircraft.SetGear(deployed: false);

                hold = Mathf.Min(EgressAltitude, hold + DescentRate * 2f * Time.fixedDeltaTime);
                HoverAssist.Hover(aircraft, point, hold, facing);
                return;
            }

            Transit(rotary: false);
        }

        // Drop geometry.

        private static float HorizontalDistance(GlobalPosition a, GlobalPosition b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

     /// <summary>Resolve terrain beneath the map point; use the point itself over water or on a missed
     /// raycast.</summary>
        private static GlobalPosition GroundUnder(GlobalPosition requested)
        {
            Vector3 local = requested.ToLocalPosition();
            Vector3 from = new Vector3(local.x, Datum.LocalSeaY + 3000f, local.z);

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6000f,
                                PhysicsLayers.StaticsMask))
                return hit.point.ToGlobalPosition();

            local.y = Datum.LocalSeaY;
            return local.ToGlobalPosition();
        }
    }
}
