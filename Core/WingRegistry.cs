using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Owns wing membership, slots, and standing orders; recruits from the native live aircraft
    /// registry.</summary>
    internal class WingRegistry
    {
        private readonly List<WingMember> members = new List<WingMember>();

        public Aircraft Leader { get; private set; }
        private PersistentID? takeoverPilotAircraftId;

        /// <summary>Wing-wide standing weapons policy.</summary>
        public WingRoe Roe { get; set; } = WingRoe.Hold;

        public IReadOnlyList<WingMember> Members => members;
        public int Count => members.Count;

        private WingMember flightLead;

        /// <summary>Temporary flight lead, or null to form on the player. The lead follows the player;
        /// others follow it. The getter clears removed, dead, or uncommandable leads; CheckReserves
        /// reports the change.</summary>
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

        /// <summary>Assign flight lead only to a commandable roster member compatible with the wing's
        /// airframe class; otherwise return a reason.</summary>
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

        /// <summary>Clear temporary lead so members form on the player.</summary>
        public void ClearFlightLead() => flightLead = null;

        public void SetLeader(Aircraft leader)
        {
            // Check the primary pilot because GetLocalAircraft can briefly return a dead or ejected
            // leader before its disabled flag updates.
            if (leader != null)
            {
                Pilot pilot = PrimaryPilot(leader);
                if (leader.disabled || pilot == null || pilot.dead || pilot.ejected)
                    leader = null;
            }

            if (Leader == leader && !(leader == null && takeoverPilotAircraftId.HasValue)) return;
            Aircraft previous = Leader;
            if (takeoverPilotAircraftId.HasValue)
            {
                Pilot pilot = PrimaryPilot(previous);
                PersonnelFacade.Roster.Retire(takeoverPilotAircraftId.Value,
                    survived: previous != null && !previous.disabled &&
                              pilot != null && !pilot.dead && !pilot.ejected);
                takeoverPilotAircraftId = null;
            }
            Leader = leader;

            // Invalidate host profiles on every leader transition so a stale companion registration
            // cannot describe the next vehicle.
            WingHost.NoteLeader(leader);

            // Reset deck hold when changing leader; it belongs to the previous aircraft.
            LeaderOnDeck = false;

            if (leader == null)
            {
                // LeaderLost holds members temporarily without replacing their directives; a new leader
                // resumes their orders.
                if (previous == null || !PersonnelFacade.Takeover.Begin(this, previous))
                    DisbandAll("leader gone");
            }
            else if (previous == null && members.Count > 0)
            {
                // Close takeover on native respawn; restoring the leader releases LeaderLost without
                // reissuing orders.
                PersonnelFacade.Takeover.LeaderRestored(leader);
            }
        }

        /// <summary>Replace the selected AI member with its player-spawned copy. Do not switch the old
        /// pilot state; the server destroys that object after success.</summary>
        public bool ReplaceWithLeader(WingMember member, Aircraft newLeader)
        {
            if (member == null || newLeader == null || !members.Remove(member)) return false;

            EconomyFacade.DepartureLane.Release(member);

            // Keep the same squadron pilot in the player-controlled replacement seat.
            WingPilot pilot = PersonnelFacade.Roster.Of(member);
            PersonnelFacade.Roster.Retire(member, survived: true);
            PersonnelFacade.Roster.Assign(newLeader, pilot);
            takeoverPilotAircraftId = newLeader.persistentID;

            Leader = newLeader;
            WingMarkers.Repaint(member.Aircraft);

            // Retain the other members' directives; restoring a leader releases their temporary hold.
            return true;
        }

        private float nextReserveCheck;

        /// <summary>Periodically check fuel, ammunition, damage, and cargo completion. Share the pass
        /// because these checks traverse member equipment and change slowly.</summary>
        public void CheckReserves()
        {
            if (Time.timeSinceLevelLoad < nextReserveCheck) return;

            // Scale equipment housekeeping by fidelity mode; avoid per-frame station and part scans.
            nextReserveCheck = Time.timeSinceLevelLoad + WingFidelity.Interval(1f);

            for (int i = 0; i < members.Count; i++)
            {
                members[i].CheckCargoRun();
                members[i].CheckDamage();
                members[i].CheckReserves();
                members[i].CheckEngageIdle();
            }

            // Report the lead invalidation that the property handles silently.
            if (flightLead != null && FlightLead == null)
                WingCommandManager.Instance?.Toast("Flight lead off station - wing re-forming on you");

            CheckLeaderOnDeck();
        }

        // Leader ground state.

        /// <summary>Radar altitude below which extended gear indicates a grounded leader.</summary>
        private const float DeckAltitude = 10f;

        /// <summary>Radar altitude required to leave deck hold and rejoin.</summary>
        private const float AirborneAltitude = 40f;

        /// <summary>Leader ground-state flag consumed by deck-hold reflexes without altering standing
        /// directives.</summary>
        public bool LeaderOnDeck { get; private set; }

        /// <summary>Whether an active member has RTB or Land orders.</summary>
        public bool HasAnyLandingOrder()
        {
            for (int i = 0; i < members.Count; i++)
            {
                WingMember m = members[i];
                if (m.Alive && (m.Order == WingOrder.ReturnToBase || m.Order == WingOrder.LandHere))
                    return true;
            }
            return false;
        }

        /// <summary>Distinguish landing from low flight using gear and altitude. Separate entry and exit
        /// heights prevent touchdown bounces from toggling hold and rejoin.</summary>
        private bool LeaderIsOnDeck()
        {
            Aircraft leader = Leader;
            if (leader == null || leader.disabled) return false;

            // Surface hosts require overwatch; absent landing gear must not make sea-level slots look
            // airborne.
            if (WingHost.Current.Overwatch) return true;

            if (LeaderOnDeck) return leader.radarAlt < AirborneAltitude;
            return leader.gearDeployed && leader.radarAlt < DeckAltitude;
        }

        /// <summary>Update and announce leader deck state. The deck-hold reflex moves formation members
        /// overhead while preserving explicit tasks until the leader is airborne.</summary>
        public void CheckLeaderOnDeck()
        {
            bool onDeck = LeaderIsOnDeck();
            if (onDeck == LeaderOnDeck) return;

            LeaderOnDeck = onDeck;
            if (!onDeck) return;

            WingCommandManager.Instance?.Toast(
                WingHost.Current.OverwatchToast ?? "Leader on the deck - wing holding overhead");
        }

        /// <summary>Clear the roster at mission end without touching aircraft already being
        /// destroyed.</summary>
        public void Clear()
        {
            members.Clear();
            takeoverPilotAircraftId = null;
            LeaderOnDeck = false;
            Leader = null;
        }

        /// <summary>Distribute scoped attacks by target coverage before concentration. Choose the nearest
        /// free shooter each pass; a single designation concentrates the eligible scope.</summary>
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
                if (m != null && m.Alive && members.Contains(m)) free.Add(m);
            }
            if (free.Count == 0) return 0;

            var seen = new HashSet<Unit>();
            var assigned = new Dictionary<Unit, int>();
            int ordered = 0;

            // Whole-wing radial attacks assign every live member, spreading targets round-robin. Scoped
            // WMC attacks retain useful-attacker caps.
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
                    int capacity = CombatFacade.Weapons.RecommendedAttackers(free[0].Aircraft, target);
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

                // Stop if no live target received an assignment.
                if (!assignedThisPass) break;
            }

            // Preserve unallocated members' routes and orders when limiting attack concurrency.

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

        /// <summary>Take the free member nearest the target.</summary>
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
        /// <summary>Resolve each member once per frame; reflex bands and scores determine
        /// precedence.</summary>
        public void Tick()
        {
            for (int i = 0; i < members.Count; i++) members[i].Tick();
        }

        /// <summary>Remove members lost to death, ejection, or despawn.</summary>
        public void Prune()
        {
            for (int i = members.Count - 1; i >= 0; i--)
            {
                WingMember m = members[i];
                if (m.Alive) continue;
                if (PersonnelFacade.Recovery.HoldsDeath(m)) continue;

                Plugin.Logger.LogWarning("[Wing] lost " + m.Name + ": " + LostReason(m) +
                    " [order=" + m.Order + ", damage=" + (m.Crew?.LossCause ?? "unknown") + "]");

                WingComms.ReportLoss(m, members);

                // Recovery claims successful returns before this pass; remaining removals are losses.
                PersonnelFacade.Roster.Retire(m, survived: false);
                EconomyFacade.LoadoutBook.Forget(m.Aircraft);
                // Release taxi ownership for ejected pilots even if no state transition occurs.
                EconomyFacade.DepartureLane.Release(m);
                CombatFacade.Tactical.Release(m.Aircraft);
                members.RemoveAt(i);
            }
        }

        /// <summary>Describe loss cause and observed flight state for diagnostics.</summary>
        private static string LostReason(WingMember m)
        {
            Aircraft a = m.Aircraft;
            if (a == null) return "aircraft destroyed or despawned";

            string state = string.Format(
                " (alt {0:F0} m, speed {1:F0} m/s, slot error {2:F0} m)",
                a.radarAlt, a.speed, m.SlotError);
            Hangar origin = a.NetworkspawningHangar;
            state += " [id=" + a.GetInstanceID() +
                     ", deliveryPending=" + m.DeliveryPending +
                     ", nativeState=" + (m.Pilot?.currentState?.GetType().Name ?? "none") +
                     ", origin=" + (origin != null && origin.parentAirbase != null
                         ? origin.parentAirbase.name : "none") +
                     ", position=" + a.GlobalPosition() + "]";

            // Check pilot death before disabled airframe: ApplyDamage disables the aircraft too and
            // would otherwise hide the cause.
            Pilot p = m.Pilot;
            if (p == null) return "pilot missing" + state;
            if (p.dead) return "pilot killed" + state;
            if (p.ejected) return "pilot ejected" + state;

            if (a.disabled) return "airframe destroyed" + state;

            return "unknown" + state;
        }

        /// <summary>Use an allocation-free loop on the per-icon colour-update path; LINQ Any would capture
        /// a predicate.</summary>
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

        /// <summary>Shared capacity gate for purchases, recruitment, and delayed deliveries, including the
        /// debug bypass.</summary>
        public static bool HasRoom(int occupied) =>
            Plugin.Settings.CheatNoWingLimit ||
            occupied + EconomyFacade.Shop.PendingWingSlots < WingFormation.MaxWingSize;

        /// <summary>Count label when the wing-limit bypass is active.</summary>
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
            // Surface vehicles need no takeoff; aircraft require the native lifecycle flag because taxi
            // altitude may be nonzero.
            if (!IsSurface(candidate) && (pilot.flightInfo == null || !pilot.flightInfo.HasTakenOff))
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

        /// <summary>Require matching rotary/fixed-wing classes for formation; their autopilots and speed
        /// envelopes are incompatible.</summary>
        private bool TypeMatchesLeader(Aircraft candidate)
        {
            if (Leader == null || candidate == null) return false;

            // Check surface class first; a missing autopilot also satisfies IsRotary.
            if (IsSurface(candidate)) return WingHost.Current.AllowSurfaceWingmen;

            // Overwatch permits mixed classes because each aircraft flies its own orbit.
            if (WingHost.Current.AllowMixedAirframes) return true;

            return IsRotary(candidate) == IsRotary(Leader);
        }

        public WingMember Add(Aircraft aircraft, bool deferCommand = false, WingPilot preferredPilot = null)
        {
            if (aircraft == null) return null;
            // Allow deferred deliveries before LocalSim settles; ActivateWhenAirborne verifies
            // authority before command transfer.
            if (!deferCommand && !aircraft.LocalSim) return null;
            if (!HasRoom(members.Count)) return null;

            // Repeat class validation here for direct map and debug additions that bypass CanRecruit.
            if (!TypeMatchesLeader(aircraft))
            {
                // Do not toast each deferred retry; recruitment logs the first failure and reports
                // prolonged waits.
                if (!deferCommand)
                {
                    WingCommandManager.Instance?.Toast(
                        IsSurface(aircraft)
                            ? aircraft.unitName + " is a surface vehicle - it cannot join this wing"
                            : IsRotary(aircraft)
                                ? aircraft.unitName + " is rotary - it cannot formate on a fixed-wing leader"
                                : aircraft.unitName + " is fixed-wing - it cannot formate on a rotary leader");
                }
                return null;
            }

            Pilot pilot = PrimaryPilot(aircraft);
            if (pilot == null) return null;
            if (!deferCommand && !IsSurface(aircraft) &&
                (pilot.flightInfo == null || !pilot.flightInfo.HasTakenOff)) return null;

            var member = new WingMember(this, aircraft, pilot, NearestFreeSlot(aircraft),
                                        deferCommand);
            members.Add(member);

            // Assign crew centrally for purchases, active-aircraft recruitment, and debug spawns.
            PersonnelFacade.Roster.Assign(aircraft, preferredPilot);
            if (!deferCommand) member.Apply(WingOrder.Formation);
            WingMarkers.Repaint(aircraft);
            WarnIfTooSlow(aircraft);
            return member;
        }

        /// <summary>Warn at recruitment when airframe speed is unlikely to support formation.</summary>
        private void WarnIfTooSlow(Aircraft recruit)
        {
            if (Leader == null) return;

            // Skip speed comparison for surface vehicles, which do not follow aircraft slots.
            if (IsSurface(recruit) || IsSurface(Leader)) return;

            float mine = recruit.GetAircraftParameters().maxSpeed;
            float leader = Leader.GetAircraftParameters().maxSpeed;
            if (mine <= 0f || leader <= 0f || mine >= leader * 0.7f) return;

            WingCommandManager.Instance?.Toast(
                recruit.unitName + " is much slower than you - it will fall behind");
            Plugin.LogVerbose(
                $"[Wing] {recruit.unitName} max speed {mine:F0} vs leader {leader:F0} - cannot hold station");
        }

        /// <summary>Dismiss a member through SendHome rather than native combat AI.</summary>
        public void Remove(WingMember member, string reason)
        {
            if (member == null) return;
            Aircraft released = member.Aircraft;
            bool awaitingNativeDeparture = member.DeliveryPending;

            // Sign off before removing the command slot; keep the pilot assigned throughout RTB to
            // prevent double seating.
            member.SendHome(reason);
            members.Remove(member);

            // Pending deliveries retain native launch control and have no return settlement; release
            // their crew immediately on cancellation.
            if (awaitingNativeDeparture)
                PersonnelFacade.Roster.Retire(member, survived: true);

            WingMarkers.Repaint(released);
        }

        /// <summary>Remove a recovered member without changing its pilot state; the settled aircraft is
        /// about to despawn.</summary>
        public void Recover(WingMember member)
        {
            if (member == null || !members.Remove(member)) return;

            EconomyFacade.DepartureLane.Release(member);

            // Retire the surviving pilot before despawn makes its aircraft ID unavailable.
            PersonnelFacade.Roster.Retire(member, survived: true);
            CombatFacade.Tactical.Release(member.Aircraft);
            WingMarkers.Repaint(member.Aircraft);
        }

        public void DisbandAll(string reason)
        {
            var released = new List<Aircraft>();
            foreach (WingMember m in members.ToList())
            {
                released.Add(m.Aircraft);
                PersonnelFacade.Roster.Retire(m, survived: m.Alive);
                if (m.Alive) m.ReleaseToCombat(reason);
            }
            members.Clear();

            // Repaint released icons after clearing the roster to remove wing tint.
            foreach (Aircraft a in released) WingMarkers.Repaint(a);
        }

        /// <summary>Assign the nearest free slot to reduce crossing on joins. Preserve surviving slot
        /// numbers after losses; later recruits fill gaps.</summary>
        private int NearestFreeSlot(Aircraft joining)
        {
            // Count + 1 bounds the unlimited search: that many slots must include a free one, even with
            // sparse surviving indices.
            int max = Plugin.Settings.CheatNoWingLimit
                ? members.Count + 1
                : WingFormation.MaxWingSize;

            if (Leader == null || joining == null)
                return members.Count + 1;

            bool surface = IsSurface(joining);

            float spacing = WingFormation.SlotSpacing;
            if (surface) spacing *= WingTuning.SurfaceSpacingScale;
            else if (IsRotary(joining)) spacing *= WingTuning.RotarySpacingScale;

            // Surface members use Trail with zero vertical stack for a flat column; preserve the
            // player's aircraft formation choice.
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

        public bool SlotTaken(int slot)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Slot == slot) return true;
            }
            return false;
        }

        /// <summary>Whether the autopilot uses rotary/tiltwing control rather than fixed-wing
        /// control.</summary>
        public static bool IsRotary(Aircraft aircraft)
        {
            return aircraft != null && !(aircraft.autopilot is AutopilotPlane);
        }

        /// <summary>Whether the airframe lacks an autopilot and needs surface handling. IsRotary alone
        /// cannot distinguish hulls from helicopters.</summary>
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
