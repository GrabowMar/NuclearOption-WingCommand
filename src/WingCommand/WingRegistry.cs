using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Owns the player's wing: who is in it, what slot they hold, and what they were
    /// last told to do. Recruitment reads <see cref="UnitRegistry.allAircraft"/>, the
    /// game's own live list.
    /// </summary>
    internal class WingRegistry
    {
        private readonly List<WingMember> members = new List<WingMember>();

        public Aircraft Leader { get; private set; }

        /// <summary>Standing rules of engagement for the whole wing.</summary>
        public WingPosture Posture { get; set; } = WingPosture.Defensive;

        public IReadOnlyList<WingMember> Members => members;
        public int Count => members.Count;

        public void SetLeader(Aircraft leader)
        {
            if (Leader == leader) return;
            Leader = leader;
            if (leader == null) DisbandAll("leader gone");
        }

        /// <summary>Send home any member out of fuel or ammunition.</summary>
        public void CheckReserves()
        {
            for (int i = 0; i < members.Count; i++) members[i].CheckReserves();
        }

        /// <summary>Put the whole wing onto one target.</summary>
        public int AttackTarget(Unit target)
        {
            if (target == null) return 0;

            int ordered = 0;
            foreach (WingMember m in members)
            {
                if (!m.Alive) continue;
                m.AttackTarget(target);
                ordered++;
            }
            return ordered;
        }

        /// <summary>Pull back any member that has strayed past the leash while engaging.</summary>
        public void CheckLeashes()
        {
            for (int i = 0; i < members.Count; i++) members[i].CheckLeash();
        }

        /// <summary>Drop members that died, ejected, or despawned.</summary>
        public void Prune()
        {
            int removed = 0;

            for (int i = members.Count - 1; i >= 0; i--)
            {
                WingMember m = members[i];
                if (m.Alive) continue;

                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("[Wing] lost " + m.Name + ": " + LostReason(m));

                members.RemoveAt(i);
                removed++;
            }

            if (removed > 0) Renumber();
        }

        /// <summary>
        /// Why a member stopped being flyable, with the flight state at the moment it was
        /// noticed. "Pruned N members" on its own says nothing about whether they were shot
        /// down, hit terrain, or were killed by G — which are very different bugs.
        /// </summary>
        private static string LostReason(WingMember m)
        {
            Aircraft a = m.Aircraft;
            if (a == null) return "aircraft destroyed or despawned";

            string state = string.Format(
                " (alt {0:F0} m, speed {1:F0} m/s, slot error {2:F0} m)",
                a.radarAlt, a.speed, m.SlotError);

            // Pilot state is checked first on purpose: Pilot.ApplyDamage calls
            // aircraft.DisableUnit() when the pilot dies, so "disabled" is also true for a
            // pilot kill. Reading disabled first reports every death as airframe loss and
            // hides the distinction that matters.
            Pilot p = m.Pilot;
            if (p == null) return "pilot missing" + state;
            if (p.dead) return "pilot killed" + state;
            if (p.ejected) return "pilot ejected" + state;

            if (a.disabled) return "airframe destroyed" + state;

            return "unknown" + state;
        }

        public bool Contains(Aircraft aircraft) => members.Any(m => m.Aircraft == aircraft);

        /// <summary>
        /// Recruit the nearest eligible friendly AI aircraft. Returns null when there is
        /// nothing to recruit (out of range, wing full, or no AI aircraft on our side).
        /// </summary>
        public WingMember RecruitNearest()
        {
            if (Leader == null) return null;
            if (members.Count >= Plugin.Config2.MaxWingSize.Value) return null;

            float bestSq = float.MaxValue;
            Aircraft best = null;

            float range = Plugin.Config2.RecruitRange.Value;
            float rangeSq = range * range;

            foreach (Aircraft candidate in UnitRegistry.allAircraft)
            {
                if (!IsEligible(candidate)) continue;

                float sq = FastMath.SquareDistance(candidate.GlobalPosition(), Leader.GlobalPosition());
                if (sq < bestSq && sq <= rangeSq)
                {
                    bestSq = sq;
                    best = candidate;
                }
            }

            return best == null ? null : Add(best);
        }

        public IEnumerable<Aircraft> EligibleCandidates()
        {
            if (Leader == null) yield break;
            float range = Plugin.Config2.RecruitRange.Value;
            float rangeSq = range * range;

            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (!IsEligible(a)) continue;
                if (FastMath.SquareDistance(a.GlobalPosition(), Leader.GlobalPosition()) <= rangeSq)
                    yield return a;
            }
        }

        private bool IsEligible(Aircraft candidate)
        {
            if (candidate == null || candidate.disabled) return false;
            if (candidate == Leader) return false;
            if (candidate.Player != null) return false;             // never commandeer another human
            if (Leader.NetworkHQ == null) return false;
            if (candidate.NetworkHQ != Leader.NetworkHQ) return false;
            if (Contains(candidate)) return false;

            Pilot pilot = PrimaryPilot(candidate);
            if (pilot == null || pilot.dead || pilot.ejected) return false;

            // Only aircraft that are actually airborne and under AI control.
            if (candidate.radarAlt < 10f) return false;

            // AI pilot states only tick where the aircraft is simulated. On a non-host
            // client a remote aircraft would accept the state switch and then never run
            // it, so refuse rather than appear to work and silently do nothing.
            if (!candidate.LocalSim) return false;

            return true;
        }

        public WingMember Add(Aircraft aircraft)
        {
            if (aircraft == null || !aircraft.LocalSim) return null;

            Pilot pilot = PrimaryPilot(aircraft);
            if (pilot == null) return null;

            var member = new WingMember(this, aircraft, pilot, members.Count + 1);
            members.Add(member);
            member.Apply(WingOrder.Formation);
            WingMapTint.Refresh(aircraft);
            WarnIfTooSlow(aircraft);
            return member;
        }

        /// <summary>
        /// Say so up front when a recruit has no hope of holding station — a helicopter
        /// cannot formate on a jet, and the failure otherwise only shows up much later as
        /// a wingman disappearing off the back of the formation.
        /// </summary>
        private void WarnIfTooSlow(Aircraft recruit)
        {
            if (!Plugin.Config2.WarnOnSlowRecruit.Value || Leader == null) return;

            float mine = recruit.GetAircraftParameters().maxSpeed;
            float leader = Leader.GetAircraftParameters().maxSpeed;
            if (mine <= 0f || leader <= 0f || mine >= leader * 0.7f) return;

            WingCommandManager.Instance?.Toast(
                recruit.unitName + " is much slower than you - it will fall behind");
            Plugin.Logger.LogInfo(
                $"[Wing] {recruit.unitName} max speed {mine:F0} vs leader {leader:F0} - cannot hold station");
        }

        public void Remove(WingMember member, string reason)
        {
            if (member == null) return;
            Aircraft released = member.Aircraft;
            member.ReleaseToCombat(reason);
            members.Remove(member);
            Renumber();
            WingMapTint.Refresh(released);
        }

        public void DisbandAll(string reason)
        {
            var released = new List<Aircraft>();
            foreach (WingMember m in members.ToList())
            {
                released.Add(m.Aircraft);
                if (m.Alive) m.ReleaseToCombat(reason);
            }
            members.Clear();

            // Icons must be repainted after the roster empties, or they keep the tint.
            foreach (Aircraft a in released) WingMapTint.Refresh(a);
        }

        public void OrderAll(WingOrder order)
        {
            foreach (WingMember m in members)
            {
                if (m.Alive) m.Apply(order);
            }
        }

        private void Renumber()
        {
            for (int i = 0; i < members.Count; i++)
                members[i].Slot = i + 1;
        }

        public static Pilot PrimaryPilot(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null || aircraft.pilots.Length == 0)
                return null;
            return aircraft.pilots[0];
        }
    }
}
