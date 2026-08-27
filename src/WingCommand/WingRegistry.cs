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
        /// <summary>Standing rules of engagement for the whole wing.</summary>
        public WingRoe Roe { get; set; } = WingRoe.Hold;

        public IReadOnlyList<WingMember> Members => members;
        public int Count => members.Count;

        public void SetLeader(Aircraft leader)
        {
            if (Leader == leader) return;
            Leader = leader;
            if (leader == null) DisbandAll("leader gone");
        }

        private float nextReserveCheck;

        /// <summary>
        /// Send home any member out of fuel or ammunition. Throttled: reading fuel walks
        /// the tanks and reading ammunition walks every weapon station, which is wasted
        /// work at frame rate for a quantity that changes slowly.
        /// </summary>
        public void CheckReserves()
        {
            if (Time.timeSinceLevelLoad < nextReserveCheck) return;
            nextReserveCheck = Time.timeSinceLevelLoad + 1f;

            for (int i = 0; i < members.Count; i++) members[i].CheckReserves();
        }

        /// <summary>
        /// Drop the roster without touching the aircraft, for when a mission ends and they
        /// are being destroyed anyway. Leaving them in place carried dead members into the
        /// next mission, where they surfaced as "lost (gone)".
        /// </summary>
        public void Clear()
        {
            members.Clear();
            Leader = null;
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

        /// <summary>
        /// Distribute several designated targets across the wing.
        ///
        /// One target means the whole wing goes for it — massed fire on a single
        /// designation is the point of that order. Several targets are spread instead, so
        /// four wingmen with four targets designated prosecute four contacts rather than
        /// queueing up behind the first one.
        ///
        /// Coverage comes before concentration: every target gets a shooter before any
        /// target gets a second one. Within each pass the nearest free member takes the
        /// target, which keeps wingmen from crossing the formation to reach something a
        /// neighbour was already beside.
        /// </summary>
        /// <param name="targets">Designated targets, most important first.</param>
        /// <param name="covered">Number of distinct targets that got at least one shooter.</param>
        /// <returns>Number of members given an order.</returns>
        public int AttackTargets(IReadOnlyList<Unit> targets, out int covered)
        {
            covered = 0;
            if (targets == null || targets.Count == 0) return 0;

            if (targets.Count == 1)
            {
                int all = AttackTarget(targets[0]);
                covered = all > 0 ? 1 : 0;
                return all;
            }

            var free = new List<WingMember>();
            foreach (WingMember m in members)
            {
                if (m.Alive) free.Add(m);
            }
            if (free.Count == 0) return 0;

            var seen = new HashSet<Unit>();
            int ordered = 0;

            while (free.Count > 0)
            {
                bool assignedThisPass = false;

                foreach (Unit target in targets)
                {
                    if (target == null || target.disabled) continue;
                    if (free.Count == 0) break;

                    WingMember nearest = TakeNearest(free, target);
                    if (nearest == null) continue;

                    nearest.AttackTarget(target);
                    ordered++;
                    assignedThisPass = true;
                    if (seen.Add(target)) covered++;
                }

                // No live target in the list: stop rather than spin.
                if (!assignedThisPass) break;
            }

            return ordered;
        }

        /// <summary>Remove and return the member closest to a target.</summary>
        private static WingMember TakeNearest(List<WingMember> free, Unit target)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            GlobalPosition targetPos = target.GlobalPosition();

            for (int i = 0; i < free.Count; i++)
            {
                Aircraft a = free[i].Aircraft;
                if (a == null) continue;

                float d = FastMath.SquareDistance(a.GlobalPosition(), targetPos);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = i;
            }

            if (best < 0) return null;

            WingMember member = free[best];
            free.RemoveAt(best);
            return member;
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

        /// <summary>
        /// Plain loop rather than LINQ: this is called from the MapIcon.UpdateColor postfix,
        /// which the game fires for every icon on the map whenever selection or theme
        /// changes, and Any() allocates a closure and an enumerator on each call.
        /// </summary>
        public bool Contains(Aircraft aircraft)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Aircraft == aircraft) return true;
            }
            return false;
        }

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

            if (!TypeMatchesLeader(candidate)) return false;

            return true;
        }

        /// <summary>
        /// Rotary and fixed-wing aircraft cannot share a formation. They fly different
        /// autopilots, hold station by different means, and differ in speed by a factor of
        /// three or more — a helicopter told to formate on a jet simply falls behind until
        /// it gives up or flies into something.
        /// </summary>
        private bool TypeMatchesLeader(Aircraft candidate)
        {
            if (Leader == null || candidate == null) return false;
            return IsRotary(candidate) == IsRotary(Leader);
        }

        public WingMember Add(Aircraft aircraft)
        {
            if (aircraft == null || !aircraft.LocalSim) return null;

            // Checked here as well as in IsEligible, because map selection and the debug
            // spawn both add directly without passing through the eligibility filter.
            if (!TypeMatchesLeader(aircraft))
            {
                WingCommandManager.Instance?.Toast(
                    IsRotary(aircraft)
                        ? aircraft.unitName + " is rotary - it cannot formate on a fixed-wing leader"
                        : aircraft.unitName + " is fixed-wing - it cannot formate on a rotary leader");
                return null;
            }

            Pilot pilot = PrimaryPilot(aircraft);
            if (pilot == null) return null;

            var member = new WingMember(this, aircraft, pilot, NearestFreeSlot(aircraft));
            members.Add(member);
            member.Apply(WingOrder.Formation);
            WingMarkers.Repaint(aircraft);
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
            if (!Plugin.Config2.KeepUpReports.Value || Leader == null) return;

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
            WingMarkers.Repaint(released);
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
            foreach (Aircraft a in released) WingMarkers.Repaint(a);
        }

        /// <summary>
        /// Give an order only to the members that can carry it out, and say how many that
        /// was. Cargo runs and landing in place are airframe-dependent, and an order that
        /// silently applies to nobody looks identical to one that failed.
        /// </summary>
        public int OrderCapable(WingOrder order, System.Func<WingMember, bool> capable)
        {
            int applied = 0;
            foreach (WingMember m in members)
            {
                if (!m.Alive || !capable(m)) continue;
                m.Apply(order);
                applied++;
            }
            return applied;
        }

        public void OrderAll(WingOrder order)
        {
            foreach (WingMember m in members)
            {
                if (m.Alive) m.Apply(order);
            }
        }

        /// <summary>
        /// Pick the free slot closest to a joining aircraft, rather than simply the next
        /// number.
        ///
        /// Handing slots out in join order means a wingman off your right wing can be given
        /// the left-hand slot and has to cross underneath you to reach it — the "characters
        /// scrabbling over each other to reach the formation" that formation-motion
        /// references warn about. Choosing by proximity shortens every rejoin and removes
        /// most crossings outright.
        ///
        /// Slots are never renumbered after a loss, either: closing the gap would make
        /// every surviving wingman physically swap position in mid-air, which looks far
        /// worse than simply flying with a hole in the formation. The next joiner fills
        /// that hole here instead.
        /// </summary>
        private int NearestFreeSlot(Aircraft joining)
        {
            int max = Plugin.Config2.MaxWingSize.Value;

            if (Leader == null || joining == null)
                return members.Count + 1;

            float spacing = Plugin.Config2.SlotSpacing.Value;
            if (IsRotary(joining)) spacing *= Plugin.Config2.RotarySpacingScale.Value;

            Vector3 from = joining.transform.position;
            Vector3 leaderPos = Leader.transform.position;
            Vector3 leaderForward = Leader.transform.forward;

            int bestSlot = members.Count + 1;
            float bestDistance = float.MaxValue;

            for (int slot = 1; slot <= max; slot++)
            {
                if (SlotTaken(slot)) continue;

                Vector3 slotPos = leaderPos + FormationSolver.SlotOffset(
                    leaderForward, slot, Plugin.Config2.Shape.Value,
                    spacing, Plugin.Config2.SlotStack.Value);

                float d = (slotPos - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestSlot = slot;
                }
            }

            return bestSlot;
        }

        private bool SlotTaken(int slot)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Slot == slot) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this airframe is flown by a rotary or tiltwing autopilot rather than a
        /// fixed-wing one. The two use different Autopilot overloads and have wildly
        /// different speed envelopes, so they are handled separately throughout.
        /// </summary>
        public static bool IsRotary(Aircraft aircraft)
        {
            return aircraft != null && !(aircraft.autopilot is AutopilotPlane);
        }

        public static Pilot PrimaryPilot(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null || aircraft.pilots.Length == 0)
                return null;
            return aircraft.pilots[0];
        }
    }
}
