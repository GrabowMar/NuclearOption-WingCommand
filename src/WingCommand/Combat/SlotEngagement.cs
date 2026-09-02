using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Shooting from a station-keeping slot, for every state that keeps station.
    ///
    /// Nothing here touches attitude or throttle, so a wingman can fire without ever
    /// compromising the slot it is holding. That is the whole reason this is separable from
    /// the flying: formation and orbit disagree completely about where the aircraft should
    /// be and not at all about what it may shoot.
    ///
    /// It used to exist twice. <see cref="FormationFlyState"/> had the full version;
    /// <see cref="OrbitState"/> had a copy that had drifted — a different check interval, no
    /// handling of an explicitly assigned target, and, worst, it asked the standing rules of
    /// engagement directly instead of asking what the wingman was actually doing. That last
    /// difference made it the only shooting state in the mod that could not be silenced by a
    /// <see cref="OrderEngagementAuthority.DefensiveOnly"/> behaviour and could not honour an
    /// explicit designation. It was correct in practice only because both routes into an
    /// orbit happened to resolve to the standing rules anyway.
    ///
    /// One instance per state, holding that state's own firing cadence.
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

        /// <summary>When this slot last actually fired. Read by callers that pace on it.</summary>
        public float LastFired => lastFired;

        /// <summary>
        /// Consider taking a shot. Returns true when one was taken, so a caller can log or
        /// pace against it.
        /// </summary>
        public bool Run(WingMember member, Aircraft aircraft, Pilot pilot, Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastCheck < WingBrain.Interval(checkInterval))
                return false;
            lastCheck = Time.timeSinceLevelLoad;

            // What the wingman is doing, not what it was told to do. A recalled wingman is
            // flying its slot even though its order still reads Engage, and asking the order
            // was how it came to be granted autonomous-combat weapons from the slot.
            OrderEngagementAuthority authority = member.EngagementAuthority;
            if (authority == OrderEngagementAuthority.DefensiveOnly) return false;

            // A weapon that passes its own checks would otherwise be fired on every tick,
            // emptying the aircraft in seconds. The stock AI leaves five seconds between
            // launches; this is the same idea, exposed so it can be tuned.
            bool mayFire = Time.timeSinceLevelLoad - lastFired >= WingWeapons.FireInterval(aircraft);

            WingRoe roe = RoeRules.Current;

            WingWeapons.Allow allow = authority == OrderEngagementAuthority.AutonomousCombat
                ? WingWeapons.Allow.AirAndGround
                : RoeRules.WeaponsFree(roe, aircraft);
            bool orderOwnsWeapons = authority == OrderEngagementAuthority.ExplicitTarget ||
                                    authority == OrderEngagementAuthority.AutonomousCombat;
            float range = orderOwnsWeapons
                ? RoeRules.ExplicitOrderRange()
                : RoeRules.EngageRange(roe);

            // Performance mode: a station-keeping wingman flies its slot and defends only.
            // Explicit attack/engage orders and inbound-missile interception still run; the
            // opportunity/priority-target hunt - which does the all-aircraft scans - does not.
            if (!WingBrain.OpportunityFire && !orderOwnsWeapons &&
                allow != WingWeapons.Allow.MissilesOnly)
                return false;

            // An explicitly assigned target outranks whatever the wingman would pick, and
            // survives until it dies.
            Unit assigned = member.AssignedTarget;
            if (assigned != null && assigned.disabled)
            {
                WingComms.Say(member, WingComms.Call.Splash, assigned.unitName);
                member.ClearAssignedTarget();
                assigned = null;
            }

            // Escort: with no explicit order standing, shoot at what is hunting the leader
            // rather than at whatever is nearest to us. This is the entire difference
            // between Escort and Hold - station-keeping and fire gating are untouched, only
            // the choice of target changes.
            bool coveringLeader = false;
            if (assigned == null)
            {
                assigned = RoeRules.PriorityTarget(roe, aircraft, leader, range);
                coveringLeader = assigned != null;
            }

            bool fired;
            if (assigned != null && allow != WingWeapons.Allow.MissilesOnly)
            {
                fired = mayFire && WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
            }
            else if (allow == WingWeapons.Allow.MissilesOnly)
            {
                // Interception paces faster than ordinary fire, but it is still behind this
                // method's own check interval, which the mode stretches. That is deliberate:
                // Performance mode is a cheaper, slower-witted wingman, and a late intercept
                // is part of what it buys. Evasion is the half that keeps the squadron
                // alive, and that runs unthrottled in DefensiveManeuverState.
                fired = Time.timeSinceLevelLoad - lastFired >= 1f &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
                if (fired) WingComms.Say(member, WingComms.Call.Defending);
            }
            else
            {
                fired = mayFire &&
                        (authority == OrderEngagementAuthority.AutonomousCombat ||
                         RoeRules.MayChooseOpportunityTarget(roe)) &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
            }

            if (!fired) return false;

            lastFired = Time.timeSinceLevelLoad;
            if (coveringLeader) WingComms.Say(member, WingComms.Call.Covering);
            return true;
        }

        /// <summary>Let a caller that fired through another path share the cadence.</summary>
        public void NoteFired() => lastFired = Time.timeSinceLevelLoad;

        /// <summary>Seconds since this slot last fired.</summary>
        public float SinceFired => Time.timeSinceLevelLoad - lastFired;
    }
}
