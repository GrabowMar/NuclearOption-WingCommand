using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Hold a circle over a fixed point until recalled.
    ///
    /// Explicit holds keep their commanded anchor; deck holds track the leader.
    /// </summary>
    internal class OrbitState : WingPilotState
    {
        internal override bool RestartOnOrderChange => false;
        private GlobalPosition anchor;
        private float radius;
        private const float EngageInterval = 0.35f;

        /// <summary>
        /// Shares formation weapons handling so active behaviours can suppress fire.
        /// </summary>
        private readonly SlotEngagement engagement = new SlotEngagement(EngageInterval);

        public OrbitState(WingMember member) : base(member)
        {
            stateDisplayName = "orbiting";
        }

        /// <summary>
        /// Deck holds track the leader while it taxis; explicit Hold orders keep a fixed anchor.
        /// </summary>
        private bool followLeader;

        /// <summary>Set the point to hold over. Call before switching to this state.</summary>
        public void SetAnchor(GlobalPosition point, float orbitRadius, bool trackLeader = false)
        {
            anchor = point;
            radius = Mathf.Max(orbitRadius, 200f);
            followLeader = trackLeader;
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);

            if (radius <= 0f) radius = WingTuning.OrbitRadius;

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose($"[Wing] {aircraft.unitName} orbiting at {radius:F0} m");
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

            if (followLeader)
            {
                Aircraft leader = member.Leader;
                if (leader != null && !leader.disabled) anchor = leader.GlobalPosition();
            }

            OrbitSteering.Fly(aircraft, controlInputs, anchor, radius, member.Slot);

            if (member.HasFollowOn)
            {
                Vector3 delta = anchor - aircraft.GlobalPosition();
                delta.y = 0f;
                float arrival = Mathf.Max(140f, aircraft.speed * 1.5f);
                if (delta.sqrMagnitude <= arrival * arrival)
                    member.CompleteHoldForQueue(OrderRevision);
            }

            // Nothing here touches attitude or throttle, so holding the ring and shooting
            // from it never compete.
            engagement.Run(member, aircraft, pilot, member.Leader);
        }
    }
}
