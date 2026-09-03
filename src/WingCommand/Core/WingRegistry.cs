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

        private WingMember flightLead;

        /// <summary>
        /// One wingman the player has granted temporary flight lead, or null for the
        /// default where every member forms on <see cref="Leader"/> (the player).
        ///
        /// A follower reads this through <see cref="WingMember.Leader"/> and formates on the
        /// designated aircraft instead of the player, so the player can peel off while the
        /// rest of the flight proceeds as one. The lead itself still forms on, and takes its
        /// orders from, the player.
        ///
        /// The getter self-heals: a lead that has left the roster, died, or lost
        /// commandability stops being the lead the moment it is asked for, so the removal
        /// paths do not each have to remember to clear it. <see cref="CheckReserves"/> is
        /// what turns that silent drop into a toast.
        /// </summary>
        public WingMember FlightLead
        {
            get
            {
                if (flightLead != null &&
                    (!members.Contains(flightLead) || !flightLead.Alive ||
                     !flightLead.IsCommandable))
                {
                    flightLead = null;
                }
                return flightLead;
            }
        }

        /// <summary>
        /// Grant flight lead to a member, or report why not. Rejects an aircraft that is not
        /// on the roster, is not commandable, or is the wrong airframe class to be formated
        /// on by the rest of the wing.
        /// </summary>
        public bool TrySetFlightLead(WingMember member, out string reason)
        {
            if (member == null || !members.Contains(member))
            {
                reason = "Not in the wing";
                return false;
            }
            if (!member.IsCommandable)
            {
                reason = member.Name + " is not taking orders";
                return false;
            }
            if (Leader != null && member.Aircraft != null &&
                IsRotary(member.Aircraft) != IsRotary(Leader))
            {
                reason = IsRotary(member.Aircraft)
                    ? "A rotary lead cannot head a fixed-wing flight"
                    : "A fixed-wing lead cannot head a rotary flight";
                return false;
            }

            flightLead = member;
            reason = null;
            return true;
        }

        /// <summary>Return the wing to forming on the player.</summary>
        public void ClearFlightLead() => flightLead = null;

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

            // A host profile describes one vehicle, and this is the whole of its liveness
            // story: every route out of a seat - death, ejection, mission end, taking over a
            // wingman - passes through here, so a companion plugin that never unregisters
            // still cannot leave its profile applied to an aircraft it did not describe.
            WingHost.NoteLeader(leader);

            // The deck hold describes one leader's situation. Carrying it across a change of
            // seat would leave the wing orbiting for an aircraft that is no longer theirs.
            LeaderOnDeck = false;

            if (leader == null)
            {
                // Nothing is ordered here any more. With no leader the LeaderLost reflex
                // holds every member overhead on its own, and — the point of the change —
                // each one keeps the order the player actually gave it, so when a new seat
                // is taken the wing resumes rather than being flattened to Formation.
                if (previous == null || !WingTakeover.Begin(this, previous))
                    DisbandAll("leader gone");
            }
            else if (previous == null && members.Count > 0)
            {
                // Covers a normal game respawn while the takeover prompt is open: the old
                // wing follows the newly spawned aircraft and the prompt closes. The reflex
                // stops scoring the moment a leader exists, so no order is needed.
                WingTakeover.LeaderRestored(leader);
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

            // No blanket re-order. The rest of the wing has been holding overhead on the
            // LeaderLost reflex with its orders intact; naming a leader stops that reflex
            // scoring and each member resumes what the player actually told it to do. Doing
            // otherwise here would flatten the orders the takeover path was just changed to
            // preserve, on the one route where the player is most likely to have left
            // something running.
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

            // Scaled by the mode. This is the one throttle in the mod actually worth having:
            // the pass below walks every weapon station and every airframe part for every
            // member, and none of the quantities it reads - fuel, ammunition, damage, cargo
            // - moves fast enough to notice the difference on a busy host.
            nextReserveCheck = Time.timeSinceLevelLoad + WingBrain.Interval(1f);

            for (int i = 0; i < members.Count; i++)
            {
                members[i].CheckCargoRun();
                members[i].CheckDamage();
                members[i].CheckReserves();
                members[i].CheckEngageIdle();
            }

            // The property self-heals silently; this is the one place that notices the drop
            // and says so, so a flight losing its lead is not a mystery.
            if (flightLead != null && FlightLead == null)
                WingCommandManager.Instance?.Toast("Flight lead off station - wing re-forming on you");

            CheckLeaderOnDeck();
        }

        // ------------------------------------------------------- leader on the ground

        /// <summary>Radar altitude below which a gear-down leader is treated as on the deck.</summary>
        private const float DeckAltitude = 10f;

        /// <summary>Radar altitude the leader must regain before the wing rejoins.</summary>
        private const float AirborneAltitude = 40f;

        /// <summary>
        /// True while the leader is on the runway rather than merely low.
        ///
        /// Just a flag now. It used to come with a set of the members that had been moved
        /// overhead and a rewrite of each one's standing directive, because there was no way
        /// to make a wingman do something temporarily without overwriting what it had been
        /// told to do. The deck-hold reflex reads this and the directive is left alone.
        /// </summary>
        public bool LeaderOnDeck { get; private set; }

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

            // A surface host is on the deck for as long as the player is in it, and the
            // gear test below would never say so: a ship or a ground vehicle has no landing
            // gear to extend, so it reads as an aircraft skimming the waves at zero feet
            // and the wing flies formation slots into the sea.
            if (WingHost.Current.Overwatch) return true;

            if (LeaderOnDeck) return leader.radarAlt < AirborneAltitude;
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
        /// Only members actually trying to hold formation are moved: an explicit order — an
        /// attack, a hold somewhere else, an RTB — is the player's, and outlives their
        /// landing. That rule now lives in the deck-hold reflex; this only decides whether
        /// the leader is on the deck at all, and says so once.
        /// </summary>
        public void CheckLeaderOnDeck()
        {
            bool onDeck = LeaderIsOnDeck();
            if (onDeck == LeaderOnDeck) return;

            LeaderOnDeck = onDeck;
            if (!onDeck) return;

            WingCommandManager.Instance?.Toast(
                WingHost.Current.OverwatchToast ?? "Leader on the deck - wing holding overhead");
        }

        /// <summary>
        /// Drop the roster without touching the aircraft, for when a mission ends and they
        /// are being destroyed anyway. Leaving them in place carried dead members into the
        /// next mission, where they surfaced as "lost (gone)".
        /// </summary>
        public void Clear()
        {
            members.Clear();
            LeaderOnDeck = false;
            Leader = null;
        }

        /// <summary>
        /// Distribute designated targets across an explicit command scope.
        ///
        /// One target means the whole scope goes for it — massed fire on a single
        /// designation is the point of that order. Several targets are spread instead, so
        /// four wingmen with four targets designated prosecute four contacts rather than
        /// queueing up behind the first one.
        ///
        /// Coverage comes before concentration: every target gets a shooter before any
        /// target gets a second one. Within each pass the nearest free member takes the
        /// target, which keeps wingmen from crossing the formation to reach something a
        /// neighbour was already beside.
        /// </summary>
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
        /// <summary>
        /// Resolve every member's behaviour for this frame.
        ///
        /// One pass replaces the three that used to run here in a load-bearing order —
        /// threats, then leashes, then reserves — where "which behaviour wins" was decided
        /// by which check happened to run last. Precedence is now a property of the reflex
        /// ladder rather than of this loop.
        /// </summary>
        public void Tick()
        {
            for (int i = 0; i < members.Count; i++) members[i].Tick();
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
            occupied + WingShop.PendingWingSlots < WingFormation.MaxWingSize;

        /// <summary>Text used beside the live count when the unsafe bypass is enabled.</summary>
        public static string WingLimitLabel =>
            Plugin.Settings.CheatNoWingLimit
                ? "NO LIMIT"
                : WingFormation.MaxWingSize.ToString();

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
            // A hull is never airborne and never will be. The gate exists to stop a wingman
            // being recruited out of a hangar mid-taxi, which a surface unit is not doing.
            if (!IsSurface(candidate) && candidate.radarAlt < 10f)
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

            // Surface is a class of its own, and it is the one class this test must never
            // wave through on the rotary rule: IsRotary answers "rotary" for anything with
            // no autopilot, so without this a ship and a helicopter read as the same kind.
            if (IsSurface(candidate)) return WingHost.Current.AllowSurfaceWingmen;

            // Nobody is holding a slot under overwatch - each aircraft steers its own orbit
            // - so the reason for the refusal is gone, and a surface leader has no airframe
            // class of its own to match against anyway.
            if (WingHost.Current.AllowMixedAirframes) return true;

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
                    IsSurface(aircraft)
                        ? aircraft.unitName + " is a surface vehicle - it cannot join this wing"
                        : IsRotary(aircraft)
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

            // Meaningless against a hull, and it would fire on every single recruit: a ship
            // is always an order of magnitude slower than a jet and is not trying to keep up.
            if (IsSurface(recruit) || IsSurface(Leader)) return;

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
                : WingFormation.MaxWingSize;

            if (Leader == null || joining == null)
                return members.Count + 1;

            bool surface = IsSurface(joining);

            float spacing = WingFormation.SlotSpacing;
            if (surface) spacing *= WingTuning.SurfaceSpacingScale;
            else if (IsRotary(joining)) spacing *= WingTuning.RotarySpacingScale;

            // Hulls hold a column astern with no vertical stagger. The horizontal geometry
            // is already 2D and the vertical is a separate term, so a stack of zero flattens
            // any shape onto the surface - and Trail, at 90 degrees of sweep, is a column
            // already, which is how ships and vehicle columns actually manoeuvre. The
            // formation the player picked for the aircraft is left alone.
            FormationShape shape = surface ? FormationShape.Trail : WingFormation.Shape;
            float stack = surface ? 0f : WingTuning.SlotStack;

            Vector3 from = joining.transform.position;
            Vector3 leaderPos = Leader.transform.position;
            Vector3 leaderForward = Leader.transform.forward;

            int bestSlot = members.Count + 1;
            float bestDistance = float.MaxValue;

            for (int slot = 1; slot <= max; slot++)
            {
                if (SlotTaken(slot)) continue;

                Vector3 slotPos = leaderPos + FormationSolver.SlotOffset(
                    leaderForward, slot, shape, spacing, stack);

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

        /// <summary>
        /// Whether this unit is a ship or a ground vehicle rather than an aircraft.
        ///
        /// The same question <see cref="WingMember.IsSurface"/> asks, in the static form the
        /// recruitment gates need before there is a member to ask it of. Note that it is not
        /// simply "not rotary": <see cref="IsRotary"/> answers true for a null autopilot, so
        /// a hull and a helicopter are indistinguishable to it.
        /// </summary>
        public static bool IsSurface(Aircraft aircraft) =>
            aircraft != null && aircraft.autopilot == null;

        public static Pilot PrimaryPilot(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null || aircraft.pilots.Length == 0)
                return null;
            return aircraft.pilots[0];
        }
    }
}
