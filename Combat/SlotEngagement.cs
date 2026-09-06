using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Weapons handling for station-keeping states; never changes attitude or throttle.
    /// Each state owns an instance for its firing cadence. Authority follows the active
    /// behaviour and explicit target, rather than the standing order alone.
    /// </summary>
    internal sealed class SlotEngagement
    {
        /// <summary>Seconds between reconsiderations. Scaled by the mode, like every other periodic check.</summary>
        private readonly float checkInterval;

        private float lastCheck;
        private float lastFired;

        public SlotEngagement(float checkInterval)
        {
            this.checkInterval = checkInterval;
        }

        /// <summary>
        /// Consider taking a shot. Returns true when one was taken, so a caller can log or
        /// pace against it.
        /// </summary>
        public bool Run(WingMember member, Aircraft aircraft, Pilot pilot, Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastCheck < WingFidelity.Interval(checkInterval))
                return false;
            lastCheck = Time.timeSinceLevelLoad;

            // What the wingman is doing, not what it was told to do. A recalled wingman is
            // flying its slot even though its order still reads Engage, and asking the order
            // was how it came to be granted autonomous-combat weapons from the slot.
            OrderEngagementAuthority authority = member.EngagementAuthority;

            // A weapon that passes its own checks would otherwise be fired on every tick,
            // emptying the aircraft in seconds. The stock AI leaves five seconds between
            // launches; this is the same idea, exposed so it can be tuned.
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
                // Interception paces faster than ordinary fire, but it is still behind this
                // method's own check interval, which the mode stretches. That is deliberate:
                // Performance mode is a cheaper, slower-witted wingman, and a late intercept
                // is part of what it buys. Evasion is the half that keeps the squadron
                // alive, and that runs unthrottled in DefensiveManeuverState.
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
