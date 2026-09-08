namespace WingCommand
{
    /// <summary>Resolves weapons policy without changing movement orders. Hold permits missile defence
    /// only; Tight protects the leader and wingman from aircraft; Free permits valid opportunity targets
    /// within the current task.</summary>
    internal static class RoeRules
    {
        /// <summary>Uppercase ROE label for the UI.</summary>
        public static string Label(WingRoe roe)
        {
            switch (roe)
            {
                case WingRoe.Tight: return "TIGHT";
                case WingRoe.Free:  return "FREE";
                default:            return "HOLD";
            }
        }

        public static WingRoe Next(WingRoe roe) => (WingRoe)(((int)roe + 1) % 3);

        /// <summary>Set FREE for Engage unless already selected.</summary>
        public static void EnsureFree(WingRegistry wing)
        {
            if (wing == null || wing.Roe == WingRoe.Free) return;
            wing.Roe = WingRoe.Free;
            WingCommandManager.Instance?.Toast("ROE: FREE");
        }

        /// <summary>Shared ROE accessor and fallback for every combat path.</summary>
        public static WingRoe Current =>
            WingCommandManager.Instance?.Wing?.Roe ?? WingRoe.Hold;

        /// <summary>Allowed target classes under the current ROE.</summary>
        public static WingWeapons.Allow WeaponsFree(WingRoe roe, Aircraft aircraft)
        {
            // Missile defence takes priority over ROE and designations; the standing directive resumes
            // afterward.
            if (WingWeapons.HasMissileDefence(aircraft) &&
                MissileDefenceProtectee(aircraft) != null)
            {
                return WingWeapons.Allow.MissilesOnly;
            }

            // Tight protects against aircraft; incidental ground attack requires Free.
            if (roe == WingRoe.Tight) return WingWeapons.Allow.AirOnly;
            if (roe == WingRoe.Free) return WingWeapons.Allow.AirAndGround;

            // Hold permits only the missile defence checked above. Explicit Attack and Splash orders
            // use separate authority.
            return WingWeapons.Allow.None;
        }

        /// <summary>Choose an aircraft under missile attack, or null. Tight checks the leader first; other
        /// postures check self first, then the leader. The intercept search needs this aircraft's inbound
        /// missile as an anchor.</summary>
        public static Aircraft MissileDefenceProtectee(Aircraft aircraft)
        {
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;

            // Tight prioritises the leader's missiles; other postures prioritise self-defence.
            bool leaderFirst = wing != null && wing.Roe == WingRoe.Tight;

            if (leaderFirst && leader != null && UnderMissileAttack(leader)) return leader;
            if (UnderMissileAttack(aircraft)) return aircraft;
            if (leader != null && UnderMissileAttack(leader)) return leader;

            // If self and leader are safe, defend another wing member.
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

        /// <summary>Choose Tight's protective target, checking threats to the leader before self. Target
        /// selection does not alter movement orders.</summary>
        public static Unit PriorityTarget(WingRoe roe, Aircraft aircraft, Aircraft leader,
                                          float range)
        {
            if (roe != WingRoe.Tight) return null;

            Unit target = WingWeapons.NearestThreatTo(leader, range);
            return target ?? WingWeapons.NearestThreatTo(aircraft, range);
        }

        /// <summary>ROE engagement range in metres.</summary>
        public static float EngageRange(WingRoe roe)
        {
            // Hold and Tight use a weapons-range limit, without manoeuvring to engage.
            return roe == WingRoe.Free
                ? WingTuning.FreeEngageRange
                : WingTuning.HoldEngageRange;
        }

        /// <summary>Slot-spacing scale: Hold tightens, Free widens, Tight uses baseline. FormationFlyState
        /// prevents compounding with reactive threat spacing.</summary>
        public static float SpacingScale(WingRoe roe)
        {
            switch (roe)
            {
                case WingRoe.Free:  return WingTuning.RoeSpacingFree;
                case WingRoe.Tight: return WingTuning.RoeSpacingTight;
                default:            return WingTuning.RoeSpacingHold;
            }
        }

        /// <summary>Range cap for player-designated attacks, independent of ROE. The weapon envelope still
        /// determines whether a shot is valid.</summary>
        public static float ExplicitOrderRange() =>
            UnityEngine.Mathf.Max(WingTuning.HoldEngageRange,
                                  WingTuning.FreeEngageRange);

        /// <summary>Hint beneath the ROE selector.</summary>
        public static string Hint(WingRoe roe)
        {
            switch (roe)
            {
                case WingRoe.Tight:
                    return "Holds the slot. Engages aircraft, guarding you first.";
                case WingRoe.Free:
                    return "Flies a loose spread. Weapons free from the current task. Engage authorises pursuit.";
                default:
                    return "Holds a tight slot. Intercepts missiles. Holds fire otherwise.";
            }
        }

        private static bool UnderMissileAttack(Aircraft aircraft)
        {
            MissileWarning warning = aircraft != null ? aircraft.GetMissileWarningSystem() : null;
            return warning != null && warning.IsWarning();
        }
    }
}
