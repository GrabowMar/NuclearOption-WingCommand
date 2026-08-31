namespace WingCommand
{
    /// <summary>
    /// Standing rules of engagement for the whole wing.
    ///
    /// The division of labour with <see cref="WingOrder"/> is the point, and it used not to
    /// hold: <b>an order says where a wingman flies, rules of engagement say what it
    /// shoots</b>. Before this split, both answered both questions. The Aggressive posture
    /// made a wingman leave formation to hunt, which is what the Engage order is for,
    /// reached by a different path with different recovery; and Cover Me was an *order*
    /// meaning "hold station but shoot what is hunting the leader", which is a weapons
    /// policy wearing an order's clothes.
    ///
    /// The three rungs are an escalation inside the current task. None of them is allowed
    /// to invent an Engage order or replace a point/target directive:
    ///
    /// <list type="bullet">
    /// <item>Hold fires only for missile defence or when mirroring the player's attack.</item>
    /// <item>Escort prioritises aircraft threatening the leader or wingman.</item>
    /// <item>Free may fire at any valid opportunity target while maintaining the task.</item>
    /// </list>
    /// </summary>
    /// <summary>Resolves what a wingman may shoot at, given the rules of engagement.</summary>
    internal static class RoeRules
    {
        /// <summary>Player-facing name; Hold remains only as the legacy config enum value.</summary>
        public static string Label(WingRoe roe) => roe == WingRoe.Hold ? "Defend" : roe.ToString();

        /// <summary>
        /// The wing's current rules of engagement.
        ///
        /// One accessor, one fallback. Callers used to reach for
        /// <c>Wing?.Posture ?? something</c> individually and disagreed about the
        /// something — the formation path assumed the cautious rung and the attack path
        /// assumed the aggressive one, so the same question had two answers depending on
        /// which file happened to ask it.
        /// </summary>
        public static WingRoe Current =>
            WingCommandManager.Instance?.Wing?.Roe ?? WingRoe.Hold;

        /// <summary>What the wingman may currently fire at.</summary>
        public static WingWeapons.Allow WeaponsFree(WingRoe roe, Aircraft aircraft)
        {
            // Missile defence is immediate self-preservation and therefore precedes both
            // incidental ROE fire and explicit target selection. It does not replace the
            // standing directive; the defensive state resumes that exact directive.
            if (Plugin.Config2.MissileDefence.Value &&
                WingWeapons.HasMissileDefence(aircraft) &&
                MissileDefenceProtectee(aircraft) != null)
            {
                return WingWeapons.Allow.MissilesOnly;
            }

            // Escort protects the leader from hostile aircraft while staying in its slot.
            // Ground attack is deliberately reserved for Free (or the player's mirrored
            // attack in Hold), so Escort remains a useful middle rung instead of a second
            // Free posture with a different label.
            if (roe == WingRoe.Escort) return WingWeapons.Allow.AirOnly;
            if (roe == WingRoe.Free) return WingWeapons.Allow.AirAndGround;

            // Hold: mirror the player's ground attack, and otherwise hold fire. Shooting
            // at whatever aircraft happens to be in range is Escort/Free behaviour, not
            // Hold's — a Hold wingman that fired at every enemy in range was exactly the
            // "they shoot missiles very aggressively" complaint.
            if (PlayerFireWatcher.GroundAttackOpen) return WingWeapons.Allow.GroundOnly;

            return WingWeapons.Allow.None;
        }

        /// <summary>
        /// The aircraft under missile attack that a wingman should defend, or null.
        ///
        /// Own missiles first, then the leader's — the two directions that make the cautious
        /// rungs worth having. The caller needs the *aircraft*, not just the yes/no, because
        /// the game's intercept search anchors on a specific inbound missile and cannot look
        /// one up from nothing.
        /// </summary>
        public static Aircraft MissileDefenceProtectee(Aircraft aircraft)
        {
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;

            // Escort is the rung that exists to guard the leader, so it answers the
            // leader's missiles before its own. The other cautious rungs see to themselves
            // first, then the leader.
            bool leaderFirst = wing != null && wing.Roe == WingRoe.Escort;

            if (leaderFirst && leader != null && UnderMissileAttack(leader)) return leader;
            if (UnderMissileAttack(aircraft)) return aircraft;
            if (leader != null && UnderMissileAttack(leader)) return leader;

            // Nothing on us or the leader: a formation still defends its members, so a
            // missile aimed at a fellow wingman is worth more than none at all.
            if (wing != null)
            {
                foreach (WingMember m in wing.Members)
                {
                    Aircraft other = m.Aircraft;
                    if (other == null || other == aircraft) continue;
                    if (UnderMissileAttack(other)) return other;
                }
            }

            return null;
        }

        /// <summary>
        /// Pick the target that gives Escort its protective character. Unlike Free's broad
        /// opportunity search, Escort anchors the search on the leader first and the firing
        /// wingman second, and never turns that target choice into a movement order.
        /// </summary>
        public static Unit PriorityTarget(WingRoe roe, Aircraft aircraft, Aircraft leader,
                                          float range)
        {
            if (roe != WingRoe.Escort) return null;

            Unit target = WingWeapons.NearestThreatTo(leader, range);
            return target ?? WingWeapons.NearestThreatTo(aircraft, range);
        }

        /// <summary>
        /// Whether the generic opportunity search may run after a priority target was not
        /// found. Escort deliberately says no: it protects the package; Free says yes and
        /// may shoot any valid contact; Defend's mirrored ground-fire allowance also needs
        /// the generic selector to find the player's kind of target.
        /// </summary>
        public static bool MayChooseOpportunityTarget(WingRoe roe) => roe != WingRoe.Escort;

        /// <summary>Engagement range for a rung, in metres.</summary>
        public static float EngageRange(WingRoe roe)
        {
            // Hold and Escort share a range because neither manoeuvres to engage, so for
            // both of them it is purely a weapons-range limit.
            return roe == WingRoe.Free
                ? Plugin.Config2.FreeEngageRange.Value
                : Plugin.Config2.HoldEngageRange.Value;
        }

        /// <summary>
        /// Range cap for a target explicitly designated by the player. ROE must not shorten
        /// an Attack or Fire For Effect order; the selected weapon's own envelope remains
        /// the final authority on whether a shot is valid.
        /// </summary>
        public static float ExplicitOrderRange() =>
            UnityEngine.Mathf.Max(Plugin.Config2.HoldEngageRange.Value,
                                  Plugin.Config2.FreeEngageRange.Value);

        /// <summary>The one-line hint shown under the selector.</summary>
        public static string Hint(WingRoe roe)
        {
            switch (roe)
            {
                case WingRoe.Escort:
                    return "Holds the slot. Engages aircraft, guarding you first.";
                case WingRoe.Free:
                    return "Weapons free from the current task. Engage authorises pursuit.";
                default:
                    return "Holds the slot. Intercepts missiles. Ground fire only when you fire.";
            }
        }

        private static bool UnderMissileAttack(Aircraft aircraft)
        {
            MissileWarning warning = aircraft != null ? aircraft.GetMissileWarningSystem() : null;
            return warning != null && warning.IsWarning();
        }
    }
}
