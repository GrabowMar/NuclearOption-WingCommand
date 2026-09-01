using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Take cargo to a point the player chose on the map and put it down there.
    ///
    /// The stock <c>AIHeloTransportState</c> is a complete supply behaviour, but it picks
    /// its own destination — nearest airbase, nearest known ground enemy — so it can never
    /// answer "put it <em>there</em>". That is the whole ask, and it is the same shape as
    /// Hold and Land: arm the cursor, click a point, watch the marker.
    ///
    /// Fixed-wing aircraft are included deliberately. Nothing about a cargo station is
    /// rotary-specific; a transport aircraft with a load runs in over the point and releases
    /// it, while a helicopter descends and sets it down. Only the stock point-less route is
    /// helicopter-only, because that is the state's own limitation rather than ours.
    ///
    /// Whether a cargo station answers <c>Fire</c> is not something a plugin build can
    /// prove, so this never assumes it worked: the station's own ammunition is the only
    /// evidence accepted, and a run that cannot shift its load hands back to the stock
    /// transport behaviour rather than hovering over a field for the rest of the mission.
    /// </summary>
    internal class CargoRunState : WingPilotState
    {
        private enum Phase { Transit, Deliver, Egress }

        /// <summary>Height held while flying to the drop point, in metres.</summary>
        private const float TransitAltitude = 140f;

        /// <summary>Height a helicopter settles to before it starts letting down.</summary>
        private const float SettleAltitude = 24f;

        /// <summary>Descent rate once settled, in metres per second.</summary>
        private const float DescentRate = 3f;

        /// <summary>Radar altitude below which a helicopter may release.</summary>
        private const float ReleaseAltitude = 8f;

        /// <summary>Height a fixed-wing aircraft makes its drop run at.</summary>
        private const float DropRunAltitude = 260f;

        /// <summary>How close to the point counts as over it, in metres.</summary>
        private const float ArrivalRadius = 120f;

        /// <summary>How close a fixed-wing release has to be to the point.</summary>
        private const float DropRadius = 250f;

        /// <summary>Seconds between release attempts.</summary>
        private const float ReleaseInterval = 1.5f;

        /// <summary>
        /// How long the delivery phase may run without the load shifting before the stock
        /// transport behaviour is given the job instead.
        /// </summary>
        private const float DeliverTimeout = 45f;

        /// <summary>Height climbed back to after the load is away.</summary>
        private const float EgressAltitude = 220f;

        private GlobalPosition point;
        private Vector3 facing;
        private Phase phase;
        private float hold;
        private float lastRelease;
        private readonly CargoProgressTracker cargoProgress = new CargoProgressTracker();
        private bool handedOff;

        public CargoRunState(WingMember member) : base(member)
        {
            stateDisplayName = "delivering";
        }

        /// <summary>Where the load is going. Call before switching to this state.</summary>
        public void SetDestination(GlobalPosition destination) => point = destination;

        public override void EnterState(Pilot pilot)
        {
            // Keep the hover configuration: this state lets a helicopter down onto a point.
            BeginFlight(pilot, releaseHover: false);

            // Ground level under the requested point, so a helicopter's hover height is a
            // height above the ground rather than above sea level. Hover adds its hold to
            // the destination's own height, exactly as the landing state relies on.
            point = GroundUnder(point);

            facing = aircraft.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            phase = Phase.Transit;
            hold = Mathf.Max(TransitAltitude, aircraft.radarAlt);
            lastRelease = 0f;
            cargoProgress.Reset(member.CargoAmmo, Time.timeSinceLevelLoad);
            handedOff = false;

            WingComms.Say(member, WingComms.Call.Delivering);

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
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
            if (aircraft == null || aircraft.disabled || handedOff) return;

            bool rotary = WingRegistry.IsRotary(aircraft);

            // The load is away. WingMember.CheckCargoRun owns completing the order - it is
            // the one place that decides a delivery happened - so this only has to fly the
            // aircraft somewhere sensible until it does.
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

        // ------------------------------------------------------------------- transit

        private void Transit(bool rotary)
        {
            // Both routes to the drop point are cruises. Anything left configured to hover
            // from a previous let-down would never make the transit.
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
                bankAllowed: Mathf.Min(Plugin.Settings.StationBankDegrees.Value,
                                       FixedWingFormation.MaxSafeBank),
                followTerrain: true,
                altitudeHold: Mathf.Max(DropRunAltitude, aircraft.maxRadius),
                targetVelocity: Vector3.zero);
        }

        // ------------------------------------------------------------------- delivery

        /// <summary>Settle over the point, let down, and release as soon as it is low enough.</summary>
        private void DeliverRotary()
        {
            hold = Mathf.Max(0f, hold - DescentRate * Time.fixedDeltaTime);
            HoverAssist.Hover(aircraft, point, hold, facing);

            if (aircraft.radarAlt <= ReleaseAltitude) TryRelease();
            CheckStalled();
        }

        /// <summary>Fly the point and release while overhead.</summary>
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

        /// <summary>
        /// Give the job to the stock transport behaviour when nothing has moved.
        ///
        /// This is the honest failure mode. If a cargo station does not release the way
        /// every other station in this mod is fired, the order still has to do something,
        /// and the stock route is what it did before drop points existed. It gives up the
        /// chosen point, so it says so.
        /// </summary>
        private void CheckStalled()
        {
            cargoProgress.Observe(member.CargoAmmo, Time.timeSinceLevelLoad);
            if (!cargoProgress.IsStalled(Time.timeSinceLevelLoad, DeliverTimeout)) return;

            handedOff = true;

            if (pilot.AIHeloTransportState != null)
            {
                WingCommandManager.Instance?.Toast(
                    member.Name + " could not release at the drop point - running the " +
                    "standard supply route instead");
                Plugin.Logger.LogWarning(
                    "[Cargo] " + aircraft.unitName + " released nothing at the drop point; " +
                    "handing over to the stock transport state");
                pilot.SwitchState(pilot.AIHeloTransportState);
                return;
            }

            WingComms.Say(member, WingComms.Call.NoDropOff);
            WingCommandManager.Instance?.Toast(
                member.Name + " could not release its cargo at that point");
            member.Apply(WingOrder.Formation);
        }

        // --------------------------------------------------------------------- egress

        /// <summary>Get off the deck and stay flyable until the order is completed.</summary>
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

        // ------------------------------------------------------------------- geometry

        private static float HorizontalDistance(GlobalPosition a, GlobalPosition b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

        /// <summary>
        /// The ground directly under a map click, or the click itself over water or where
        /// nothing was hit. A map point carries no useful height of its own.
        /// </summary>
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
