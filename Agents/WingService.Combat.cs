using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Supervised engagement (spec M5 §2): members fight in the game's own combat state; every way out of it is
    /// ours (<see cref="CombatSupervisor"/>).</summary>
    internal sealed partial class WingService
    {
        public int EngagedCount
        {
            get
            {
                int n = 0;
                foreach (WingMember m in Members)
                    if (m.Engaged) n++;
                return n;
            }
        }

        /// <summary>Every member flying with the wing switches to the game's combat state; <paramref name="target"/> (may be
        /// null) is the one each should attack. Returns how many are engaged.</summary>
        public int Engage(Unit target)
        {
            int n = 0;
            foreach (WingMember m in Members)
            {
                if (m.Released || m.OnGround || m.Recovery != null || !m.Alive) continue;
                m.AssignedTarget = target;
                m.Pilot.SetPrimaryTarget(target);
                if (m.Engaged)
                {
                    n++;
                    continue;
                }
                PilotBaseState combat = NativeCombatState(m.Pilot);
                if (combat == null) continue;
                m.Engaged = true;
                m.NoTargetClock = 0f;
                // Without contact the game's no-target mode flies to mission objectives (review M5a I2).
                if (m.Pilot.flightInfo != null) m.Pilot.flightInfo.EnemyContact = true;
                m.Pilot.SwitchState(combat);
                if (!ReferenceEquals(m.Pilot.currentState, combat))
                {
                    m.Engaged = false;
                    continue;
                }
                Events.Push(new WingEvent { Time = missionTime, Member = m.Brain.Slot, Kind = WingEventKind.Engaged, Reason = TransitionReason.Commanded });
                n++;
            }
            return n;
        }

        public static float ReallocateSeconds = 1f;
        private readonly Unit[] attackTargets = new Unit[TargetAllocator.MaxTargets];
        private int attackCount;
        private float reallocateClock;
        private readonly bool[] canAttack = new bool[FormationCatalog.MaxSlots * TargetAllocator.MaxTargets];
        private readonly float[] targetDistance = new float[FormationCatalog.MaxSlots * TargetAllocator.MaxTargets];
        private readonly bool[] targetAlive = new bool[TargetAllocator.MaxTargets];
        private readonly int[] currentTarget = new int[FormationCatalog.MaxSlots], nextTarget = new int[FormationCatalog.MaxSlots];
        private readonly WingMember[] engagedNow = new WingMember[FormationCatalog.MaxSlots];

        /// <summary>Members with an attack order's target (automation reads it).</summary>
        public int AssignedCount
        {
            get
            {
                int n = 0;
                foreach (WingMember m in Members)
                    if (m.Engaged && m.AssignedTarget != null) n++;
                return n;
            }
        }

        /// <summary>Engage on <paramref name="targets"/> (live ones, the first 16), split across the wing
        /// (<see cref="TargetAllocator"/>) and re-allocated each second as they die (spec M5, M5b). Returns how many are
        /// engaged.</summary>
        public int Attack(IReadOnlyList<Unit> targets)
        {
            attackCount = 0;
            if (targets != null)
                foreach (Unit u in targets)
                    if (u != null && !u.disabled && attackCount < attackTargets.Length) attackTargets[attackCount++] = u;
            int n = Engage(null);
            Allocate();
            return n;
        }

        /// <summary>The attack order's targets across the engaged members; with none alive the order ends and members
        /// fight on their own choices.</summary>
        private void Allocate()
        {
            if (attackCount == 0) return;
            bool any = false;
            for (int t = 0; t < attackCount; t++)
            {
                targetAlive[t] = attackTargets[t] != null && !attackTargets[t].disabled;
                any |= targetAlive[t];
            }
            int k = 0;
            foreach (WingMember m in Members)
                if (m.Engaged && !m.Released && k < engagedNow.Length) engagedNow[k++] = m;
            if (!any)
            {
                attackCount = 0;
                for (int i = 0; i < k; i++) Assign(engagedNow[i], null);
                return;
            }
            for (int i = 0; i < k; i++)
            {
                WingMember m = engagedNow[i];
                currentTarget[i] = -1;
                Vec3 at = m.Aircraft.GlobalPosition().ToVec3();
                for (int t = 0; t < attackCount; t++)
                {
                    int cell = i * attackCount + t;
                    canAttack[cell] = targetAlive[t] && CanAttack(m.Aircraft, attackTargets[t]);
                    targetDistance[cell] = targetAlive[t] ? (attackTargets[t].GlobalPosition().ToVec3() - at).Length : float.MaxValue;
                    if (ReferenceEquals(m.AssignedTarget, attackTargets[t])) currentTarget[i] = t;
                }
            }
            TargetAllocator.Assign(k, attackCount, canAttack, targetDistance, targetAlive, currentTarget, nextTarget);
            for (int i = 0; i < k; i++) Assign(engagedNow[i], nextTarget[i] >= 0 ? attackTargets[nextTarget[i]] : null);
        }

        private static void Assign(WingMember m, Unit target)
        {
            if (ReferenceEquals(m.AssignedTarget, target)) return;
            m.AssignedTarget = target;
            m.Pilot?.SetPrimaryTarget(target);
        }

        /// <summary>The member's faction tracks <paramref name="t"/> and one of its loaded non-cargo stations can attack it,
        /// by the game's own analysis.</summary>
        private static bool CanAttack(Aircraft a, Unit t)
        {
            // The game only chooses a target whose position is accurate (review M5a I4).
            TrackingInfo tracking = a.NetworkHQ != null ? a.NetworkHQ.GetTrackingData(t.persistentID) : null;
            if (tracking == null || a.weaponStations == null || !a.NetworkHQ.IsTargetPositionAccurate(t, TargetAccuracyMetres)) return false;
            foreach (WeaponStation w in a.weaponStations)
                if (Usable(a, w) && CombatAI.AnalyzeTarget(w, a, tracking, 0f, -1f, 100f).opportunity > 0f) return true;
            return false;
        }

        public static float TargetAccuracyMetres = 1000f, EnergyChargeMin = 0.6f;

        /// <summary>A loaded, non-cargo, non-nuclear station (an energy weapon only when charged), as the game's own choice
        /// and the aces' hunt require.</summary>
        internal static bool Usable(Aircraft a, WeaponStation w) =>
            w != null && !w.Cargo && w.Ammo > 0 && w.WeaponInfo != null && !w.WeaponInfo.nuclear &&
            (!w.WeaponInfo.energy || (a.GetPowerSupply() != null && a.GetPowerSupply().GetCharge() >= EnergyChargeMin));

        /// <summary>Every engaged member back into formation (Commanded). Returns how many.</summary>
        public int Disengage()
        {
            attackCount = 0;
            int n = 0;
            foreach (WingMember m in Members)
                if (m.Engaged)
                {
                    TakeBack(m, TransitionReason.Commanded);
                    n++;
                }
            return n;
        }

        public Unit AssignedTarget(Aircraft a)
        {
            foreach (WingMember m in Members)
                if (ReferenceEquals(m.Aircraft, a)) return m.Engaged ? m.AssignedTarget : null;
            return null;
        }

        private static bool InNativeCombat(WingMember m) =>
            m.Pilot != null && m.Pilot.currentState != null &&
            (ReferenceEquals(m.Pilot.currentState, m.Pilot.AICombatState) || ReferenceEquals(m.Pilot.currentState, m.Pilot.AIHeloCombatState));

        /// <summary>Every tick: an engaged member at bingo, Winchester, or beyond the leash is taken back (bingo then goes
        /// home).</summary>
        private void SuperviseCombat(float dt)
        {
            AnchorSample anchor = default;
            bool sampled = false;
            foreach (WingMember m in Members)
            {
                if (!m.Engaged || m.Released || !m.Alive || !InNativeCombat(m)) continue;
                if (!sampled)
                {
                    anchor = Planner.Active ? Planner.Sample() : AnchorNow();
                    sampled = true;
                }
                // The sensor is not read here: a second read in a tick would zero its derived acceleration.
                bool bingo = BingoNow(m, dt);
                var s = new CombatSituation
                {
                    AnchorPresent = anchor.Present,
                    AnchorDistance = anchor.Present ? (anchor.Pos - m.Aircraft.GlobalPosition().ToVec3()).Length : 0f,
                    Ammo = AmmoFraction(m.Aircraft), Bingo = bingo,
                    NoTargetSeconds = m.NoTargetClock = NativeTarget(m) != null ? 0f : m.NoTargetClock + dt,
                };
                if (!CombatSupervisor.TakeBack(s, out TransitionReason reason)) continue;
                TakeBack(m, reason);
                if (reason == TransitionReason.Fuel)
                {
                    WingToast.Show($"#{m.Number} bingo fuel; returning to base");
                    Recover(m, RecoveryIntent.Rtb);
                }
            }
            if (attackCount > 0 && (reallocateClock += dt) >= ReallocateSeconds)
            {
                reallocateClock = 0f;
                Allocate();
            }
        }

        /// <summary>The game's combat state leaving for landing or transport: our state instead (null: not ours).</summary>
        public PilotBaseState LeaveNativeCombat(Pilot pilot, PilotBaseState next)
        {
            WingMember m = null;
            foreach (WingMember x in Members)
                if (ReferenceEquals(x.Pilot, pilot)) m = x;
            if (m == null || !m.Engaged || m.Released || !InNativeCombat(m) || ReferenceEquals(next, m.State)) return null;
            NativeExit exit = next != null && (ReferenceEquals(next, pilot.AILandingState) || ReferenceEquals(next, pilot.AIHeloLandingState)) ? NativeExit.Landing
                : next != null && ReferenceEquals(next, pilot.AIHeloTransportState) ? NativeExit.Transport
                : NativeExit.None;
            if (exit == NativeExit.None) return null;
            var s = new CombatSituation { Exit = exit, Ammo = 1f, Bingo = m.Bingo.Bingo };
            CombatSupervisor.TakeBack(s, out TransitionReason reason);
            Disengaged(m, reason);
            return m.State;
        }

        /// <summary>Back into our state (it tracks the aircraft bumplessly) and rejoining.</summary>
        private void TakeBack(WingMember m, TransitionReason reason)
        {
            Disengaged(m, reason);
            if (m.Pilot != null && !m.Pilot.dead && !ReferenceEquals(m.Pilot.currentState, m.State)) m.Pilot.SwitchState(m.State);
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<AIPilotCombatModes, Unit> PlaneTarget =
            HarmonyLib.AccessTools.FieldRefAccess<AIPilotCombatModes, Unit>("currentTarget");
        private static readonly HarmonyLib.AccessTools.FieldRef<AIHeloCombatState, Unit> HeloTarget =
            HarmonyLib.AccessTools.FieldRefAccess<AIHeloCombatState, Unit>("currentTarget");

        /// <summary>The target the game's combat state is after (null: none).</summary>
        private static Unit NativeTarget(WingMember m)
        {
            PilotBaseState state = m.Pilot?.currentState;
            if (state is AIPilotCombatModes plane) return PlaneTarget(plane);
            if (state is AIHeloCombatState helo) return HeloTarget(helo);
            return null;
        }

        private void Disengaged(WingMember m, TransitionReason reason)
        {
            // The game's combat states turn countermeasures on and never off (review M5a I3).
            Aircraft a = m.Aircraft;
            if (a != null && a.countermeasureTrigger) a.Countermeasures(false, a.countermeasureManager.activeIndex);
            m.Engaged = false;
            m.AssignedTarget = null;
            m.Pilot?.SetPrimaryTarget(null);
            m.Brain.FormUp(missionTime, Events);
            Events.Push(new WingEvent { Time = missionTime, Member = m.Brain.Slot, Kind = WingEventKind.Disengaged, Reason = reason });
        }
    }
}
