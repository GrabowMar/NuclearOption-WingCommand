using UnityEngine;

namespace WingCommand
{
 /// <summary>Land at a local or designated point instead of routing to a base. Native Hover holds
 /// position while a decreasing altitude command descends to the surface.</summary>
    internal class LandInPlaceState : WingPilotState
    {
        private enum Phase { Transit, Settle, Descend, Down }

     /// <summary>Commanded descent rate in metres per second.</summary>
        private const float DescentRate = 3f;

     /// <summary>Radar-altitude touchdown threshold.</summary>
        private const float TouchdownAlt = 1.5f;

     /// <summary>Maximum ground speed before descent begins.</summary>
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
            // Bind controls directly because this state manages gear and preserves hover during
            // landing/search.
            BindControls(pilot);
            aircraft.SetGear(deployed: !hasRequestedSpot);

            // Anchor to ground elevation so reducing Hover altitudeHold commands actual descent to the
            // surface.
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

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose(
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
                // Hold brakes and zero collective after touchdown to prevent creeping or relaunch.
                controlInputs.throttle = 0f;
                controlInputs.brake = 1f;
                return;
            }

            if (aircraft.radarAlt <= TouchdownAlt)
            {
                phase = Phase.Down;
                WingComms.Say(member, WingComms.Call.Down);

                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.LogVerbose($"[Wing] {aircraft.unitName} is down");

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
            // Release hover during transit so vectoring aircraft can accelerate toward the landing
            // spot.
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

     /// <summary>Find the nearest reasonably level static landing surface around the requested
     /// point.</summary>
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
