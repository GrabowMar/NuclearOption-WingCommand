using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Emergency disengagement: scatter, flares, run, hold.
    ///
    /// Three phases, because a retreat that is one long turn away looks like a manoeuvre
    /// and a retreat that starts with the whole wing breaking on different headings looks
    /// like a reaction:
    ///
    /// 1. <b>Break</b> — hard turn away from the threat with flares running. Each wingman
    ///    takes a different heading, fanned by slot, so the wing scatters rather than
    ///    wheeling as one block.
    /// 2. <b>Egress</b> — run for the rally point at full power and low altitude.
    /// 3. <b>Hold</b> — orbit the rally point until recalled, via <see cref="OrbitSteering"/>.
    ///
    /// The flare handling mirrors the stock AI exactly, including the
    /// <c>countermeasureTrigger</c> check before toggling: <c>Aircraft.Countermeasures</c>
    /// dispenses continuously while held, so it is switched off at the end of the break
    /// rather than left running, which would empty the aircraft.
    /// </summary>
    internal class FallBackState : PilotBaseState
    {
        private enum Phase { Break, Egress, Hold }

        /// <summary>Seconds of hard break before settling into the run.</summary>
        private const float BreakSeconds = 4.5f;

        /// <summary>Seconds of flares from the start of the break.</summary>
        private const float FlareSeconds = 3f;

        /// <summary>Degrees each slot's break heading is fanned from its neighbour's.</summary>
        private const float ScatterSpread = 35f;

        /// <summary>Altitude held during the run out, in metres above ground.</summary>
        private const float EgressAltitude = 200f;

        private readonly WingMember member;

        private Phase phase;
        private float phaseStarted;
        private Vector3 breakDirection;
        private GlobalPosition rally;
        private bool flaring;

        public FallBackState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "falling back";
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

            phase = Phase.Break;
            phaseStarted = Time.timeSinceLevelLoad;

            Vector3 away = AwayFromThreat();
            rally = ChooseRally(away);

            // Fan the break by slot. Slot 1 goes one way, slot 2 the other, slot 3 wider
            // again — the wing splits instead of presenting one turning formation.
            float side = (member.Slot % 2 == 1) ? 1f : -1f;
            float fan = side * ScatterSpread * ((member.Slot + 1) / 2);
            breakDirection = Quaternion.AngleAxis(fan, Vector3.up) * away;

            StartFlares();

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[Wing] {aircraft.unitName} falling back, breaking {fan:F0} deg off the threat axis");
            }
        }

        public override void LeaveState()
        {
            StopFlares();
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            float elapsed = Time.timeSinceLevelLoad - phaseStarted;

            if (flaring && elapsed > FlareSeconds) StopFlares();

            switch (phase)
            {
                case Phase.Break:
                    Break();
                    if (elapsed > BreakSeconds) Advance(Phase.Egress);
                    break;

                case Phase.Egress:
                    Egress();
                    if (ReachedStandoff()) Advance(Phase.Hold);
                    break;

                case Phase.Hold:
                    OrbitSteering.Fly(aircraft, controlInputs, rally,
                                      Plugin.Config2.OrbitRadius.Value, member.Slot * 120f);
                    break;
            }
        }

        private void Advance(Phase next)
        {
            phase = next;
            phaseStarted = Time.timeSinceLevelLoad;

            if (next == Phase.Hold)
            {
                StopFlares();
                WingComms.Say(member, WingComms.Call.Holding);
            }
        }

        // ------------------------------------------------------------------- phases

        /// <summary>Hard turn away, full power, maximum bank authority.</summary>
        private void Break()
        {
            controlInputs.throttle = 1f;

            GlobalPosition destination = aircraft.GlobalPosition() + breakDirection * 8000f;

            if (!(aircraft.autopilot is AutopilotPlane))
            {
                RotaryRun(destination);
                return;
            }

            aircraft.autopilot.AutoAim(
                destination: destination,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: Plugin.Config2.PursuitBankDegrees.Value,
                followTerrain: false,
                altitudeHold: Mathf.Clamp(aircraft.radarAlt, aircraft.maxRadius, 8000f),
                targetVelocity: Vector3.zero);
        }

        /// <summary>Run for the rally point, low and fast.</summary>
        private void Egress()
        {
            controlInputs.throttle = 1f;

            if (!(aircraft.autopilot is AutopilotPlane))
            {
                RotaryRun(rally);
                return;
            }

            aircraft.autopilot.AutoAim(
                destination: rally,
                aimVelocity: true,
                ignoreCollisions: false,
                runwayAlign: false,
                effort: 2f,
                bankAllowed: 140f,
                followTerrain: true,
                altitudeHold: EgressAltitude,
                targetVelocity: Vector3.zero);
        }

        private void RotaryRun(GlobalPosition destination)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            float agl = Mathf.Clamp(Mathf.Max(p.minimumRadarAlt, EgressAltitude * 0.5f), 25f, 3000f);

            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: agl,
                aimDirection: Vector3.zero,
                targetVelocity: Vector3.zero,
                followTerrain: true);
        }

        private bool ReachedStandoff()
        {
            float standoff = Plugin.Config2.FallBackStandoff.Value;

            // Either far enough from the threat, or close enough to the rally point that
            // there is nothing left to run towards.
            return FastMath.SquareDistance(aircraft.GlobalPosition(), rally) < standoff * standoff * 0.25f
                   || Time.timeSinceLevelLoad - phaseStarted > 90f;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// A horizontal unit vector pointing away from the nearest known threat.
        ///
        /// Falls back progressively, because a retreat has to work even with an empty
        /// track picture: the faction's nearest known ground enemy first, then the
        /// reciprocal of the leader's heading, which at least takes the wing back the way
        /// it came.
        /// </summary>
        private Vector3 AwayFromThreat()
        {
            Vector3 away = Vector3.zero;

            FactionHQ hq = aircraft.NetworkHQ;
            if (hq != null && hq.TryGetNearestGroundEnemy(aircraft.GlobalPosition(), out TrackingInfo enemy))
                away = aircraft.GlobalPosition() - enemy.lastKnownPosition;

            if (away.sqrMagnitude < 1f)
            {
                Aircraft leader = member.Leader;
                away = leader != null ? -leader.transform.forward : -aircraft.transform.forward;
            }

            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;

            return away.normalized;
        }

        /// <summary>
        /// Where to run to: the nearest friendly airbase, else a ship, else simply a long
        /// way down the retreat axis. Something is always returned — a fall-back order that
        /// silently does nothing because no airbase was found is worse than one that runs
        /// in a sensible direction.
        /// </summary>
        private GlobalPosition ChooseRally(Vector3 away)
        {
            FactionHQ hq = aircraft.NetworkHQ;
            float standoff = Plugin.Config2.FallBackStandoff.Value;

            if (hq != null)
            {
                Airbase airbase = hq.GetNearestAirbase(aircraft.transform.position);
                if (airbase != null)
                    return airbase.transform.GlobalPosition() + Vector3.up * EgressAltitude;

                if (hq.TryGetNearestShip(aircraft.GlobalPosition(), out Ship ship, out float _) && ship != null)
                    return ship.GlobalPosition() + Vector3.up * EgressAltitude;
            }

            return aircraft.GlobalPosition() + away * standoff;
        }

        // -------------------------------------------------------------------- flares

        private void StartFlares()
        {
            if (aircraft == null || aircraft.countermeasureManager == null) return;
            if (aircraft.countermeasureTrigger) { flaring = true; return; }

            aircraft.Countermeasures(active: true, aircraft.countermeasureManager.activeIndex);
            flaring = true;
        }

        private void StopFlares()
        {
            if (!flaring || aircraft == null || aircraft.countermeasureManager == null) return;

            if (aircraft.countermeasureTrigger)
                aircraft.Countermeasures(active: false, aircraft.countermeasureManager.activeIndex);

            flaring = false;
        }
    }
}
