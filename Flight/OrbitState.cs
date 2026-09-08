using UnityEngine;

namespace WingCommand
{
    /// <summary>Orbit until recalled: fixed anchors for explicit holds, moving leader anchors for deck
    /// holds.</summary>
    internal class OrbitState : WingPilotState
    {
        internal override bool RestartOnOrderChange => false;
        private GlobalPosition anchor;
        private float radius;
        private const float EngageInterval = 0.35f;

        /// <summary>Shared station-keeping weapons handler respects active behaviour authority.</summary>
        private readonly SlotEngagement engagement = new SlotEngagement(EngageInterval);

        public OrbitState(WingMember member) : base(member)
        {
            stateDisplayName = "orbiting";
        }

        /// <summary>Track a taxiing leader only for deck holds; explicit holds remain fixed.</summary>
        private bool followLeader;

        /// <summary>Set anchor, radius, and optional leader tracking before entry.</summary>
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

            // Weapons handling leaves orbit attitude and throttle untouched.
            engagement.Run(member, aircraft, pilot, member.Leader);
        }
    }
}
