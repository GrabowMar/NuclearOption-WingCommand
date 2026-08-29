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
    /// The three rungs are an escalation, and each has its own answer to the same event —
    /// the leader being shot at. That is the test of whether the model earns its place:
    ///
    /// <list type="bullet">
    /// <item>Hold shoots the missile down and stays put.</item>
    /// <item>Escort shoots the aircraft that launched it, and stays put.</item>
    /// <item>Free breaks formation and goes after the shooter.</item>
    /// </list>
    /// </summary>
    internal enum WingRoe
    {
        /// <summary>
        /// Holds the slot no matter what. Intercepts inbound missiles at itself or the
        /// leader, and attacks ground targets only while the player is attacking ground.
        /// </summary>
        Hold,

        /// <summary>
        /// Holds the slot, but may engage hostile aircraft while looking after the leader:
        /// targets whatever is threatening the leader in preference to what is nearest.
        /// </summary>
        Escort,

        /// <summary>
        /// Weapons free, and the only rung that will leave formation on its own — and then
        /// only for the emergency of the leader being under missile attack. Routine hunting
        /// is an explicit Engage order, not a posture.
        /// </summary>
        Free,
    }

    /// <summary>Resolves what a wingman may shoot at, given the rules of engagement.</summary>
    internal static class RoeRules
    {
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
            // Missile defence outranks everything below Free: a missile in the air is the
            // most time-critical thing on the battlefield, and both cautious rungs exist to
            // keep the leader alive.
            if (roe != WingRoe.Free &&
                Plugin.Config2.MissileDefence.Value &&
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

        /// <summary>True when this rung prefers threats to the leader over its own.</summary>
        public static bool GuardsLeader(WingRoe roe) => roe == WingRoe.Escort;

        /// <summary>
        /// True when this rung may leave formation on its own.
        ///
        /// Only Free, and only for the leader-under-missile emergency. The generic "break
        /// for any air threat in range" that used to live here is gone: it made Aggressive
        /// plus Formation behave almost identically to an Engage order, which is why the
        /// two were impossible to tell apart.
        /// </summary>
        public static bool MayBreakForEmergency(WingRoe roe) => roe == WingRoe.Free;

        /// <summary>Engagement range for a rung, in metres.</summary>
        public static float EngageRange(WingRoe roe)
        {
            // Hold and Escort share a range because neither manoeuvres to engage, so for
            // both of them it is purely a weapons-range limit.
            return roe == WingRoe.Free
                ? Plugin.Config2.FreeEngageRange.Value
                : Plugin.Config2.HoldEngageRange.Value;
        }

        /// <summary>The one-line hint shown under the selector.</summary>
        public static string Hint(WingRoe roe)
        {
            switch (roe)
            {
                case WingRoe.Escort:
                    return "Holds the slot. Engages aircraft, guarding you first.";
                case WingRoe.Free:
                    return "Weapons free. Breaks formation only if you are shot at.";
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
