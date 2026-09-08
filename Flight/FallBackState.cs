using UnityEngine;

namespace WingCommand
{
    /// <summary>Scatter by slot with a hard break and brief flares, egress low toward rally, then rejoin.
    /// Native countermeasures dispense continuously while triggered, so release the trigger after the
    /// flare phase.</summary>
    internal class FallBackState : WingPilotState
    {
        private enum Phase { Break, Egress, Hold }

        /// <summary>Duration of the initial hard break, in seconds.</summary>
        private const float BreakSeconds = 4.5f;

        /// <summary>Flare duration from break entry, in seconds.</summary>
        private const float FlareSeconds = 3f;

        /// <summary>Angular separation between slot break headings, in degrees.</summary>
        private const float ScatterSpread = 35f;

        /// <summary>Egress altitude in metres AGL.</summary>
        private const float EgressAltitude = 200f;

        private Phase phase;
        private float phaseStarted;
        private Vector3 breakDirection;
        private GlobalPosition rally;
        private bool flaring;

        public FallBackState(WingMember member) : base(member)
        {
            stateDisplayName = "falling back";
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);

            phase = Phase.Break;
            phaseStarted = Time.timeSinceLevelLoad;

            Vector3 away = AwayFromThreat();
            rally = ChooseRally(away);

            // Alternate and widen break headings by slot to scatter the wing.
            float side = (member.Slot % 2 == 1) ? 1f : -1f;
            float fan = side * ScatterSpread * ((member.Slot + 1) / 2);
            breakDirection = Quaternion.AngleAxis(fan, Vector3.up) * away;

            StartFlares();

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Plugin.LogVerbose(
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
                    // Defensive completion if the phase transition has already finished.
                    CompleteTask(WingOrder.Formation);
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
                WingComms.Say(member, WingComms.Call.Rejoining);
                CompleteTask(WingOrder.Formation);
            }
        }

        // Retreat phases.

        /// <summary>Break away at full power with maximum permitted bank.</summary>
        private void Break()
        {
            controlInputs.throttle = 1f;

            GlobalPosition destination = aircraft.GlobalPosition() + breakDirection * 8000f;

            if (WingRegistry.IsRotary(aircraft))
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
                bankAllowed: AutopilotMath.PursuitBank(),
                followTerrain: false,
                altitudeHold: AutopilotMath.CruiseHold(aircraft, aircraft.radarAlt),
                targetVelocity: Vector3.zero);
        }

        /// <summary>Fly low and fast toward rally.</summary>
        private void Egress()
        {
            controlInputs.throttle = 1f;

            if (WingRegistry.IsRotary(aircraft))
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
                bankAllowed: FixedWingFormation.MaxSafeBank,
                followTerrain: true,
                altitudeHold: EgressAltitude,
                targetVelocity: Vector3.zero);
        }

        private void RotaryRun(GlobalPosition destination)
        {
            aircraft.autopilot.AutoAim(
                destination: destination,
                altitudeHold: AutopilotMath.RotaryAgl(aircraft, EgressAltitude * 0.5f),
                aimDirection: Vector3.zero,
                targetVelocity: Vector3.zero,
                followTerrain: true);
        }

        private bool ReachedStandoff()
        {
            float standoff = WingTuning.FallBackStandoff;

            // Finish once sufficiently clear of the threat or near rally.
            return FastMath.SquareDistance(aircraft.GlobalPosition(), rally) < standoff * standoff * 0.25f
                   || Time.timeSinceLevelLoad - phaseStarted > 90f;
        }

        // Retreat geometry.

        /// <summary>Horizontal direction away from a known threat. Fall back to the nearest known ground
        /// enemy, then opposite the leader's heading when tracks are unavailable.</summary>
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

        /// <summary>Friendly loiter point: nearest base, then ship, then stand-off along away. Shared with
        /// Stand Down.</summary>
        internal static GlobalPosition FriendlyLoiterPoint(Aircraft aircraft, Vector3 away)
        {
            if (aircraft == null) return default;
            FactionHQ hq = aircraft.NetworkHQ;
            float standoff = WingTuning.FallBackStandoff;
            Vector3 lift = Vector3.up * EgressAltitude;

            if (hq != null)
            {
                Airbase airbase = hq.GetNearestAirbase(aircraft.transform.position);
                if (airbase != null)
                    return airbase.transform.GlobalPosition() + lift;

                if (hq.TryGetNearestShip(aircraft.GlobalPosition(), out Ship ship, out float _) &&
                    ship != null)
                    return ship.GlobalPosition() + lift;
            }

            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            return aircraft.GlobalPosition() + away.normalized * standoff + lift;
        }

        private GlobalPosition ChooseRally(Vector3 away) => FriendlyLoiterPoint(aircraft, away);

        // Retreat countermeasures.

        /// <summary>Flare station resolved on entry; -1 if absent.</summary>
        private int flareIndex = -1;

        /// <summary>Trigger the actual flare station during the break; the previously active station may
        /// be ECM.</summary>
        private void StartFlares()
        {
            if (aircraft == null || aircraft.countermeasureManager == null) return;

            if (!CountermeasureAccess.TryFindExpendable(
                    aircraft.countermeasureManager, "IR", out flareIndex, out _))
            {
                flareIndex = -1;
                return;
            }

            if (flareIndex > byte.MaxValue) { flareIndex = -1; return; }

            aircraft.Countermeasures(active: true, (byte)flareIndex);
            flaring = true;
        }

        private void StopFlares()
        {
            if (!flaring || aircraft == null || aircraft.countermeasureManager == null) return;

            if (aircraft.countermeasureTrigger && flareIndex >= 0)
                aircraft.Countermeasures(active: false, (byte)flareIndex);

            flaring = false;
        }
    }
}
