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
        public WingRoe Roe { get; set; } = WingRoe.Hold;

        public IReadOnlyList<WingMember> Members => members;
        public int Count => members.Count;

        public void SetLeader(Aircraft leader)
        {
            // GetLocalAircraft keeps returning the old airframe briefly after death or
            // ejection. Treat its primary pilot as authoritative rather than waiting for the
            // networked disabled flag, otherwise a dead leader can keep the formation flying
            // on its wreck for several frames.
            if (leader != null)
            {
                Pilot pilot = PrimaryPilot(leader);
                if (leader.disabled || pilot == null || pilot.dead || pilot.ejected)
                    leader = null;
            }

            if (Leader == leader) return;
            Aircraft previous = Leader;
            Leader = leader;

            // The deck hold describes one leader's situation. Carrying it across a change of
            // seat would leave the wing orbiting for an aircraft that is no longer theirs.
            heldOnDeck.Clear();
            leaderOnDeck = false;

            if (leader == null)
            {
                if (previous != null && WingTakeover.Begin(this, previous))
                    HoldForTakeover();
                else
                    DisbandAll("leader gone");
            }
            else if (previous == null && members.Count > 0)
            {
                // Covers a normal game respawn while the takeover prompt is open: the old
                // wing follows the newly spawned aircraft and the prompt closes.
                WingTakeover.LeaderRestored(leader);
                OrderAll(WingOrder.Formation);
            }
        }

        /// <summary>Keep candidates safely airborne while the player chooses a new seat.</summary>
        private void HoldForTakeover()
        {
            foreach (WingMember member in members)
            {
                if (member.Alive) member.Apply(WingOrder.OrbitHere);
            }
        }

        /// <summary>
        /// Replace a chosen AI member with the fresh player-controlled aircraft spawned from
        /// it. The original member is removed without switching its pilot state because the
        /// server destroys that network object immediately after the replacement succeeds.
        /// </summary>
        public bool ReplaceWithLeader(WingMember member, Aircraft newLeader)
        {
            if (member == null || newLeader == null || !members.Remove(member)) return false;

            // The player is now in that seat, so its pilot goes back on the squadron list
            // rather than being written off with the AI airframe that is about to be removed.
            WingPilotRoster.Retire(member, survived: true);

            Leader = newLeader;
            WingMarkers.Repaint(member.Aircraft);
            OrderAll(WingOrder.Formation);
            return true;
        }

        private float nextReserveCheck;

        /// <summary>
        /// The once-a-second housekeeping pass: send home any member out of fuel or
        /// ammunition, and follow any supply run to its end.
        ///
        /// Throttled together because both walk every weapon station on every member, which
        /// is wasted work at frame rate for quantities that change slowly.
        /// </summary>
        public void CheckReserves()
        {
            if (Time.timeSinceLevelLoad < nextReserveCheck) return;
            nextReserveCheck = Time.timeSinceLevelLoad + 1f;

            for (int i = 0; i < members.Count; i++)
            {
                members[i].CheckCargoRun();
                members[i].CheckDamage();
                members[i].CheckReserves();
            }

            CheckLeaderOnDeck();
        }

        // ------------------------------------------------------- leader on the ground

        /// <summary>Radar altitude below which a gear-down leader is treated as on the deck.</summary>
        private const float DeckAltitude = 10f;

        /// <summary>Radar altitude the leader must regain before the wing rejoins.</summary>
        private const float AirborneAltitude = 40f;

        /// <summary>Members holding overhead because the leader is on the ground.</summary>
        private readonly HashSet<WingMember> heldOnDeck = new HashSet<WingMember>();

        private bool leaderOnDeck;

        /// <summary>
        /// Whether the leader is on the ground rather than merely low.
        ///
        /// The distinction is the whole point: flying an ingress at ten metres is ordinary
        /// in this game and must not disband the formation, whereas an approach, a landing
        /// roll or a parked aircraft must. Extended gear separates the two — nobody runs a
        /// low-level attack with the gear hanging out — and the exit threshold sits far
        /// enough above the entry one that a bounce on touchdown cannot flap the wing back
        /// and forth between holding and rejoining.
        /// </summary>
        private bool LeaderIsOnDeck()
        {
            Aircraft leader = Leader;
            if (leader == null || leader.disabled) return false;

            if (leaderOnDeck) return leader.radarAlt < AirborneAltitude;
            return leader.gearDeployed && leader.radarAlt < DeckAltitude;
        }

        /// <summary>
        /// Hold the wing overhead while the player is landing, landed or taxiing.
        ///
        /// A formation slot is defined relative to the leader, so a leader on the runway
        /// puts every slot on the runway too — and the wingmen flew at them, which is a
        /// wing of aircraft diving into the ground the moment the player touches down. They
        /// orbit the field instead until the leader is airborne again, then rejoin.
        ///
        /// Only members actually trying to hold formation are moved, and only they are
        /// given back: an explicit order — an attack, a hold somewhere else, an RTB — is the
        /// player's, and outlives their landing.
        /// </summary>
        public void CheckLeaderOnDeck()
        {
            bool onDeck = LeaderIsOnDeck();

            if (!onDeck)
            {
                if (!leaderOnDeck) return;
                leaderOnDeck = false;

                foreach (WingMember member in heldOnDeck)
                {
                    if (member == null || !members.Contains(member)) continue;
                    if (!member.IsCommandable || member.Order != WingOrder.OrbitHere) continue;
                    member.Apply(WingOrder.Formation);
                }

                heldOnDeck.Clear();
                return;
            }

            // Runs on every pass, not only on the transition: a wingman that finishes an
            // attack while the player is still on the ground rejoins into a formation order
            // of its own accord, and would fly the same slot into the same runway.
            GlobalPosition overhead = Leader.GlobalPosition();
            bool announced = leaderOnDeck;
            leaderOnDeck = true;

            foreach (WingMember member in members)
            {
                if (member == null || !member.IsCommandable) continue;
                if (member.Order != WingOrder.Formation) continue;

                member.Apply(WingDirective.AtPoint(WingOrder.OrbitHere, overhead));
                heldOnDeck.Add(member);

                if (!announced)
                {
                    announced = true;
                    WingCommandManager.Instance?.Toast(
                        "Leader on the deck - wing holding overhead");
                }
            }
        }

        /// <summary>
        /// Drop the roster without touching the aircraft, for when a mission ends and they
        /// are being destroyed anyway. Leaving them in place carried dead members into the
        /// next mission, where they surfaced as "lost (gone)".
        /// </summary>
        public void Clear()
        {
            members.Clear();
            heldOnDeck.Clear();
            leaderOnDeck = false;
            Leader = null;
        }

        /// <summary>Put a useful number of wingmen onto one target.</summary>
        public int AttackTarget(Unit target)
        {
            if (target == null) return 0;

            int ordered = 0;
            foreach (WingMember m in members)
            {
                if (!m.IsCommandable) continue;
                int capacity = WingWeapons.RecommendedAttackers(m.Aircraft, target);
                if (ordered >= capacity)
                {
                    m.Apply(WingOrder.Formation);
                    continue;
                }
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
            return AttackTargets(members, targets, out covered, forceAll: false);
        }

        /// <summary>Distribute designated targets across an explicit command scope.</summary>
        public int AttackTargets(IReadOnlyList<WingMember> candidates,
                                 IReadOnlyList<Unit> targets, out int covered,
                                 bool forceAll = false,
                                 List<WingMember> orderedMembers = null)
        {
            covered = 0;
            if (candidates == null || targets == null || targets.Count == 0) return 0;

            var free = new List<WingMember>();
            foreach (WingMember m in candidates)
            {
                if (m != null && m.IsCommandable && members.Contains(m)) free.Add(m);
            }
            if (free.Count == 0) return 0;

            var seen = new HashSet<Unit>();
            var assigned = new Dictionary<Unit, int>();
            int ordered = 0;

            // Whole-wing radial attacks are an explicit "everyone attack" command. The
            // scoped WMC attack keeps useful-target caps and leaves surplus aircraft as
            // cover, but the radial must not silently leave one member on Form Up when the
            // player asked the wing to attack. Spread multiple designations round-robin so
            // every live member receives a concrete attack directive.
            if (forceAll)
            {
                int targetIndex = 0;
                for (int i = 0; i < free.Count; i++)
                {
                    Unit target = NextLiveTarget(targets, ref targetIndex);
                    if (target == null) break;

                    free[i].AttackTarget(target, report: false);
                    orderedMembers?.Add(free[i]);
                    ordered++;
                    if (seen.Add(target)) covered++;
                }
                return ordered;
            }

            while (free.Count > 0)
            {
                bool assignedThisPass = false;

                foreach (Unit target in targets)
                {
                    if (target == null || target.disabled) continue;
                    if (free.Count == 0) break;

                    int already = assigned.TryGetValue(target, out int count) ? count : 0;
                    int capacity = WingWeapons.RecommendedAttackers(free[0].Aircraft, target);
                    if (already >= capacity) continue;

                    WingMember nearest = TakeNearest(free, target);
                    if (nearest == null) continue;

                    nearest.AttackTarget(target, report: false);
                    orderedMembers?.Add(nearest);
                    ordered++;
                    assignedThisPass = true;
                    assigned[target] = already + 1;
                    if (seen.Add(target)) covered++;
                }

                // No live target in the list: stop rather than spin.
                if (!assignedThisPass) break;
            }

            // Aircraft beyond the useful simultaneous attack count remain as cover instead
            // of queueing behind the same target and wasting the whole wing's weapons.
            for (int i = 0; i < free.Count; i++)
                free[i].Apply(WingOrder.Formation);

            return ordered;
        }

        private static Unit NextLiveTarget(IReadOnlyList<Unit> targets, ref int index)
        {
            if (targets == null || targets.Count == 0) return null;

            for (int i = 0; i < targets.Count; i++)
            {
                Unit target = targets[index % targets.Count];
                index++;
                if (target != null && !target.disabled) return target;
            }

            return null;
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

        /// <summary>Let missile self-preservation interrupt any standing wing order.</summary>
        public void CheckThreats()
        {
            for (int i = 0; i < members.Count; i++) members[i].CheckThreats();
        }

        /// <summary>Drop members that died, ejected, or despawned.</summary>
        public void Prune()
        {
            for (int i = members.Count - 1; i >= 0; i--)
            {
                WingMember m = members[i];
                if (m.Alive) continue;
                if (WingRecovery.IsPending(m)) continue;

                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("[Wing] lost " + m.Name + ": " + LostReason(m));

                WingComms.ReportLoss(m, members);

                // Prune only ever sees losses; a wingman that recovered at base was claimed
                // by WingRecovery a moment earlier and never reaches here.
                WingPilotRoster.Retire(m, survived: false);
                WingLoadoutBook.Forget(m.Aircraft);
                TacticalCoordinator.Release(m.Aircraft);
                members.RemoveAt(i);
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

        public bool Contains(WingMember member) => member != null && members.Contains(member);

        public WingMember Find(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Aircraft == aircraft) return members[i];
            }
            return null;
        }

        /// <summary>
        /// Whether another member may be added at the supplied occupancy.
        ///
        /// The debug bypass is deliberately centralised here. Purchases, active-aircraft
        /// assignment and delayed hangar deliveries all reach the roster through different
        /// paths; letting any one of them keep its own MaxWingSize check would make the F1
        /// option appear to work only some of the time.
        /// </summary>
        public static bool HasRoom(int occupied) =>
            Plugin.Settings.CheatNoWingLimit ||
            occupied + WingShop.PendingWingSlots < Plugin.Settings.MaxWingSize.Value;

        /// <summary>Text used beside the live count when the unsafe bypass is enabled.</summary>
        public static string WingLimitLabel =>
            Plugin.Settings.CheatNoWingLimit
                ? "NO LIMIT"
                : Plugin.Settings.MaxWingSize.Value.ToString();

        public bool CanRecruit(Aircraft candidate, out string reason)
        {
            reason = null;
            if (Leader == null) { reason = "Not flying"; return false; }
            if (!HasRoom(members.Count))
            { reason = "Wing is full"; return false; }
            if (candidate == null || candidate.disabled)
            { reason = "Aircraft is no longer available"; return false; }
            if (candidate == Leader || candidate.Player != null)
            { reason = "Player aircraft cannot be assigned"; return false; }
            if (Leader.NetworkHQ == null || candidate.NetworkHQ != Leader.NetworkHQ)
            { reason = "Aircraft is not in your faction"; return false; }
            if (Contains(candidate)) { reason = "Aircraft is already in the wing"; return false; }

            Pilot pilot = PrimaryPilot(candidate);
            if (pilot == null || pilot.dead || pilot.ejected)
            { reason = "Aircraft has no available AI pilot"; return false; }
            if (candidate.radarAlt < 10f)
            { reason = "Aircraft must be airborne"; return false; }
            if (!candidate.LocalSim)
            { reason = "Aircraft is not controlled by this host"; return false; }
            if (!TypeMatchesLeader(candidate))
            {
                reason = IsRotary(candidate)
                    ? "Helicopters cannot formate on a fixed-wing leader"
                    : "Fixed-wing aircraft cannot formate on a rotary leader";
                return false;
            }
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

        public WingMember Add(Aircraft aircraft, bool deferCommand = false)
        {
            if (aircraft == null || !aircraft.LocalSim) return null;
            if (!HasRoom(members.Count)) return null;

            // Checked here as well as in CanRecruit, because map selection and the debug
            // spawn can add directly without passing through the eligibility filter.
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

            var member = new WingMember(this, aircraft, pilot, NearestFreeSlot(aircraft),
                                        deferCommand);
            members.Add(member);

            // Someone has to be flying it. Assigning here rather than at each call site
            // covers requisition, active-AI assignment and the debug spawn alike.
            WingPilotRoster.Assign(aircraft);
            if (!deferCommand) member.Apply(WingOrder.Formation);
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
            if (Leader == null) return;

            float mine = recruit.GetAircraftParameters().maxSpeed;
            float leader = Leader.GetAircraftParameters().maxSpeed;
            if (mine <= 0f || leader <= 0f || mine >= leader * 0.7f) return;

            WingCommandManager.Instance?.Toast(
                recruit.unitName + " is much slower than you - it will fall behind");
            Plugin.Logger.LogInfo(
                $"[Wing] {recruit.unitName} max speed {mine:F0} vs leader {leader:F0} - cannot hold station");
        }

        /// <summary>
        /// Release one member at the player's request.
        ///
        /// Sends it home rather than to the combat AI: see <see cref="WingMember.SendHome"/>
        /// for why a dismissal and an automatic break want opposite endings.
        /// </summary>
        public void Remove(WingMember member, string reason)
        {
            if (member == null) return;
            Aircraft released = member.Aircraft;

            // Sign off before retiring the pilot, not after. Retire clears the seat
            // assignment, and a sign-off from an aircraft with nobody assigned to it comes
            // out as an anonymous slot number instead of the pilot the player knows.
            member.SendHome(reason);
            WingPilotRoster.Retire(member, survived: true);
            members.Remove(member);
            heldOnDeck.Remove(member);
            WingMarkers.Repaint(released);
        }

        /// <summary>
        /// Drop a member whose aircraft is about to stop existing.
        ///
        /// Unlike <see cref="Remove"/> this does not hand the pilot back to the combat AI:
        /// <see cref="WingRecovery"/> calls it for an aircraft that has landed and is being
        /// destroyed, and switching a parked pilot into a combat state on its way out would
        /// only put it briefly back in the air.
        /// </summary>
        public void Recover(WingMember member)
        {
            if (member == null || !members.Remove(member)) return;

            WingPilotRoster.Retire(member, survived: true);
            TacticalCoordinator.Release(member.Aircraft);
            WingMarkers.Repaint(member.Aircraft);
        }

        public void DisbandAll(string reason)
        {
            var released = new List<Aircraft>();
            foreach (WingMember m in members.ToList())
            {
                released.Add(m.Aircraft);
                WingPilotRoster.Retire(m, survived: m.Alive);
                if (m.Alive) m.ReleaseToCombat(reason);
            }
            members.Clear();

            // Icons must be repainted after the roster empties, or they keep the tint.
            foreach (Aircraft a in released) WingMarkers.Repaint(a);
        }

        public void OrderAll(WingOrder order)
        {
            foreach (WingMember m in members)
            {
                if (m.IsCommandable) m.Apply(order);
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
            // Unlimited cannot be represented as a loop bound. Count + 1 is sufficient:
            // among that many 1-based slots there must be a free one, even when surviving
            // members retain high slot numbers after losses.
            int max = Plugin.Settings.CheatNoWingLimit
                ? members.Count + 1
                : Plugin.Settings.MaxWingSize.Value;

            if (Leader == null || joining == null)
                return members.Count + 1;

            float spacing = Plugin.Settings.SlotSpacing.Value;
            if (IsRotary(joining)) spacing *= WingTuning.RotarySpacingScale;

            Vector3 from = joining.transform.position;
            Vector3 leaderPos = Leader.transform.position;
            Vector3 leaderForward = Leader.transform.forward;

            int bestSlot = members.Count + 1;
            float bestDistance = float.MaxValue;

            for (int slot = 1; slot <= max; slot++)
            {
                if (SlotTaken(slot)) continue;

                Vector3 slotPos = leaderPos + FormationSolver.SlotOffset(
                    leaderForward, slot, Plugin.Settings.Shape.Value,
                    spacing, WingTuning.SlotStack);

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
