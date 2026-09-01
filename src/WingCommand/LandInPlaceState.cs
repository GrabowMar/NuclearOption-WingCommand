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
    internal class LandInPlaceState : WingPilotState
    {
        private enum Phase { Transit, Settle, Descend, Down }

        /// <summary>Descent rate, in metres per second.</summary>
        private const float DescentRate = 3f;

        /// <summary>Radar altitude at which the aircraft is considered down.</summary>
        private const float TouchdownAlt = 1.5f;

        /// <summary>Ground speed below which the aircraft is allowed to start descending.</summary>
        private const float SettleSpeed = 6f;

        private const float TransitAltitude = 120f;
        private const float SettleAltitude = 22f;
        private const float ArrivalRadius = 90f;
        private const float MaximumSlope = 18f;

        private GlobalPosition spot;
        private GlobalPosition requestedSpot;
        private bool hasRequestedSpot;
        private Vector3 facing;
        private float hold;
        private Phase phase;

        public LandInPlaceState(WingMember member) : base(member)
        {
            stateDisplayName = "landing";
        }

        public void SetDestination(GlobalPosition point)
        {
            requestedSpot = point;
            hasRequestedSpot = true;
        }

        public void ClearDestination()
        {
            requestedSpot = default(GlobalPosition);
            hasRequestedSpot = false;
        }

        public override void EnterState(Pilot pilot)
        {
            // This state configures its own gear (down, unless it will hover-and-search
            // first) and keeps whatever hover regime it arrived with - so it binds the
            // controls directly rather than through BeginFlight.
            BindControls(pilot);
            aircraft.SetGear(deployed: !hasRequestedSpot);

            // Anchor at the ground beneath the aircraft, not at the aircraft itself.
            // Hover adds altitudeHold to the destination's own height difference, so with
            // the anchor on the deck the held height IS the altitudeHold argument, and
            // winding it down to zero is the descent.
            bool safe = hasRequestedSpot && TryFindLandingSpot(requestedSpot, out spot);
            if (!safe)
            {
                spot = aircraft.GlobalPosition() - Vector3.up * aircraft.radarAlt;
                if (hasRequestedSpot)
                    WingCommandManager.Instance?.Toast(
                        "No safe landing surface at that point - landing below current position");
            }

            float horizontal = HorizontalDistance(aircraft.GlobalPosition(), spot);
            phase = safe && horizontal > ArrivalRadius ? Phase.Transit : Phase.Settle;
            hold = phase == Phase.Transit
                ? Mathf.Max(TransitAltitude, aircraft.radarAlt)
                : Mathf.Max(SettleAltitude, aircraft.radarAlt);

            facing = aircraft.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
                    $"[Wing] {aircraft.unitName} landing ({phase}) from {hold:F0} m");
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

            if (phase == Phase.Down)
            {
                // Stay put. Collective at zero with the brake on, so it does not creep or
                // get nudged back into the air by its own rotor wash.
                controlInputs.throttle = 0f;
                controlInputs.brake = 1f;
                return;
            }

            if (aircraft.radarAlt <= TouchdownAlt)
            {
                phase = Phase.Down;
                WingComms.Say(member, WingComms.Call.Down);

                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"[Wing] {aircraft.unitName} is down");

                return;
            }

            switch (phase)
            {
                case Phase.Transit:
                    Transit();
                    if (HorizontalDistance(aircraft.GlobalPosition(), spot) <= ArrivalRadius)
                    {
                        phase = Phase.Settle;
                        hold = SettleAltitude;
                        aircraft.SetGear(deployed: true);
                    }
                    break;

                case Phase.Settle:
                    HoverAssist.Hover(aircraft, spot, hold, facing);
                    if (aircraft.speed < SettleSpeed &&
                        HorizontalDistance(aircraft.GlobalPosition(), spot) < 30f)
                        phase = Phase.Descend;
                    break;

                case Phase.Descend:
                    hold = Mathf.Max(0f, hold - DescentRate * Time.fixedDeltaTime);
                    HoverAssist.Hover(aircraft, spot, hold, facing);
                    break;
            }
        }

        private void Transit()
        {
            // Transit is flown, not hovered. Holding the hovering configuration across the
            // cruise out to the spot would stop a thrust-vectoring aircraft ever getting
            // there.
            HoverAssist.Release(aircraft);

            aircraft.autopilot.AutoAim(
                destination: spot + Vector3.up * TransitAltitude,
                altitudeHold: AutopilotMath.RotaryAgl(aircraft, TransitAltitude, 40f, 1000f),
                aimDirection: Vector3.zero,
                targetVelocity: Vector3.zero,
                followTerrain: true);
        }

        private static float HorizontalDistance(GlobalPosition a, GlobalPosition b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

        /// <summary>Choose the nearest reasonably flat static surface around a map click.</summary>
        private static bool TryFindLandingSpot(GlobalPosition requested, out GlobalPosition result)
        {
            Vector3 centre = requested.ToLocalPosition();
            float[] offsets = { 0f, 45f, -45f, 90f, -90f };
            float bestDistance = float.MaxValue;
            Vector3 best = Vector3.zero;
            bool found = false;

            for (int x = 0; x < offsets.Length; x++)
            {
                for (int z = 0; z < offsets.Length; z++)
                {
                    Vector3 sample = new Vector3(
                        centre.x + offsets[x], Datum.LocalSeaY + 3000f, centre.z + offsets[z]);
                    if (!Physics.Raycast(sample, Vector3.down, out RaycastHit hit, 6000f,
                                         PhysicsLayers.StaticsMask))
                        continue;
                    if (Vector3.Angle(hit.normal, Vector3.up) > MaximumSlope) continue;

                    float distance = offsets[x] * offsets[x] + offsets[z] * offsets[z];
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = hit.point;
                    found = true;
                }
            }

            result = found ? best.ToGlobalPosition() : default(GlobalPosition);
            return found;
        }
    }
}
