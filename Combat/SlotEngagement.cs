using UnityEngine;

namespace WingCommand
{
 /// <summary>Handles station-keeping weapons without changing flight controls. Each state owns its
 /// cadence; active behaviour and target determine firing authority.</summary>
    internal sealed class SlotEngagement
    {
     /// <summary>Seconds between checks, scaled by fidelity mode.</summary>
        private readonly float checkInterval;

        private float lastCheck;
        private float lastFired;

        public SlotEngagement(float checkInterval)
        {
            this.checkInterval = checkInterval;
        }

     /// <summary>Attempt a shot; return true if fired.</summary>
        public bool Run(WingMember member, Aircraft aircraft, Pilot pilot, Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastCheck < WingFidelity.Interval(checkInterval))
                return false;
            lastCheck = Time.timeSinceLevelLoad;

            // Use active behaviour: a recalled Engage wingman must obey station-keeping weapons
            // authority.
            OrderEngagementAuthority authority = member.EngagementAuthority;

            // Enforce the configured interval so repeated checks cannot empty the loadout immediately.
            bool mayFire = Time.timeSinceLevelLoad - lastFired >= WingWeapons.FireInterval(aircraft);

            WingRoe roe = RoeRules.Current;
            WingWeapons.Allow roeAllow = RoeRules.WeaponsFree(roe, aircraft);
            StationFireMode mode = OrderRoePolicy.StationFire(authority, roe,
                roeAllow == WingWeapons.Allow.MissilesOnly, WingFidelity.OpportunityFire);
            if (mode == StationFireMode.None) return false;

            bool orderOwnsWeapons = authority == OrderEngagementAuthority.ExplicitTarget ||
                                    authority == OrderEngagementAuthority.AutonomousCombat;
            float range = orderOwnsWeapons
                ? RoeRules.ExplicitOrderRange()
                : RoeRules.EngageRange(roe);

            bool fired = false;
            bool coveringLeader = false;
            if (mode == StationFireMode.MissileDefence)
            {
                // Interception still obeys the mode-scaled check interval. DefensiveManeuverState
                // handles evasion without throttling.
                fired = Time.timeSinceLevelLoad - lastFired >= 1f &&
                        WingWeapons.Engage(aircraft, pilot, WingWeapons.Allow.MissilesOnly, range);
                if (fired) WingComms.Say(member, WingComms.Call.Defending);
            }
            else if (mayFire)
            {
                switch (mode)
                {
                    case StationFireMode.DesignatedTarget:
                        Unit assigned = member.AssignedTarget;
                        if (assigned != null && !assigned.disabled)
                            fired = WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
                        break;
                    case StationFireMode.ProtectWing:
                        Unit threat = RoeRules.PriorityTarget(roe, aircraft, leader, range);
                        if (threat != null)
                        {
                            fired = WingWeapons.EngageSpecific(aircraft, pilot, threat, range);
                            coveringLeader = fired;
                        }
                        break;
                    case StationFireMode.Opportunity:
                        fired = WingWeapons.Engage(aircraft, pilot,
                            WingWeapons.Allow.AirAndGround, range);
                        break;
                }
            }

            if (!fired) return false;

            lastFired = Time.timeSinceLevelLoad;
            if (coveringLeader) WingComms.Say(member, WingComms.Call.Covering);
            return true;
        }
    }
}
