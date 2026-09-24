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

        private readonly OutnumberedJudge judge = new OutnumberedJudge();
        private float outnumberedClock;
        private readonly Unit[] othersTargets = new Unit[FormationCatalog.MaxSlots];

        /// <summary>Enemy aircraft around the engaged members at the last check (automation reads it).</summary>
        public int LastHostiles { get; private set; }
        /// <summary>Times the wing fell back outnumbered this session.</summary>
        public int FallBacks { get; private set; }

        private static float FallBackRatio => Plugin.Settings != null ? Plugin.Settings.FallBackRatio.Value : 0f;

        /// <summary>A new mission: no attack order, no refusal or override carried over (review M5d I1).</summary>
        private void ResetCombat()
        {
            attackCount = 0;
            reallocateClock = 0f;
            outnumberedClock = 0f;
            judge.Reset();
            LastHostiles = 0;
        }

        /// <summary>The radial Engage asks first (spec M5 §6.3): false while outnumbered, unless this is the second press
        /// within the confirmation window.</summary>
        public bool MayEngage(out int hostiles, out int members)
        {
            hostiles = 0;
            members = 0;
            Vec3 sum = default;
            Aircraft any = null;
            foreach (WingMember m in Members)
            {
                if (m.Released || m.OnGround || m.Recovery != null || !m.Alive) continue;
                members++;
                sum += m.Aircraft.GlobalPosition().ToVec3();
                any = m.Aircraft;
            }
            if (members == 0) return true;
            hostiles = CountHostiles(any, sum / members);
            return judge.AllowEngage(hostiles, members, FallBackRatio, missionTime);
        }

        /// <summary>Enemy aircraft <paramref name="a"/>'s faction tracks within <see cref="OutnumberedJudge.RadiusMetres"/>
        /// of <paramref name="centre"/>.</summary>
        private static int CountHostiles(Aircraft a, Vec3 centre)
        {
            FactionHQ hq = a != null ? a.NetworkHQ : null;
            if (hq == null || hq.trackingDatabase == null) return 0;
            float r2 = OutnumberedJudge.RadiusMetres * OutnumberedJudge.RadiusMetres;
            int n = 0;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !t.TryGetUnit(out Unit u) || !(u is Aircraft enemy) || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq) continue;
                if ((t.GetPosition().ToVec3() - centre).SqrLength > r2) continue;
                // Parked, unarmed or stale tracks are no air threat (review M5d I2).
                float antiAir = enemy.definition != null ? enemy.definition.roleIdentity.antiAir : 0f;
                if (OutnumberedJudge.IsAirThreat(hq.IsTargetPositionAccurate(u, TargetAccuracyMetres), enemy.radarAlt, antiAir)) n++;
            }
            return n;
        }

        /// <summary>Once a second while members fight on their own choices: outnumbered for the dwell → every engaged
        /// member back into formation (spec M5 §6.3).</summary>
        private void JudgeOdds(float dt)
        {
            if ((outnumberedClock += dt) < 1f) return;
            float step = outnumberedClock;
            outnumberedClock = 0f;
            int engaged = 0;
            Vec3 sum = default;
            Aircraft any = null;
            foreach (WingMember m in Members)
            {
                if (!m.Engaged || m.Released || !m.Alive || !InNativeCombat(m)) continue;
                engaged++;
                sum += m.Aircraft.GlobalPosition().ToVec3();
                any = m.Aircraft;
            }
            if (engaged == 0)
            {
                // The override lasts one engagement.
                if (judge.Overridden) judge.Reset();
                return;
            }
            if (attackCount > 0) return;
            LastHostiles = CountHostiles(any, sum / engaged);
            if (!judge.Update(LastHostiles, engaged, FallBackRatio, step)) return;
            foreach (WingMember m in Members)
                if (m.Engaged) TakeBack(m, TransitionReason.Outnumbered);
            FallBacks++;
            judge.Reset();
            WingToast.Show($"Outnumbered {LastHostiles} to {engaged}; falling back");
        }

        /// <summary>The nearest missile guiding on the member (spec M5 §7.1): the game's missile warning; a new missile is
        /// classified once by the game's own choice of countermeasure (which also selects the station): "IR" is
        /// infrared, anything else radar.</summary>
        private static MissileThreat ReadThreat(WingMember m)
        {
            Aircraft a = m.Aircraft;
            MissileWarning warning = a.GetMissileWarningSystem();
            if (warning == null || !warning.TryGetNearestIncoming(out Missile missile) || missile == null || missile.disabled)
            {
                m.ThreatMissile = null;
                return default;
            }
            if (!ReferenceEquals(missile, m.ThreatMissile))
            {
                m.ThreatMissile = missile;
                string seeker = a.countermeasureManager != null ? a.countermeasureManager.ChooseCountermeasure(missile) : "";
                m.ThreatSeeker = seeker == "IR" ? MissileSeeker.Infrared : MissileSeeker.Radar;
            }
            return new MissileThreat
            {
                Present = true, Pos = missile.GlobalPosition().ToVec3(), Seeker = m.ThreatSeeker,
                Vel = missile.rb != null ? missile.rb.velocity.ToVec3() : Vec3.Zero,
            };
        }

        /// <summary>The countermeasure trigger to <paramref name="on"/> (never on with no matching station).</summary>
        private static void Trigger(Aircraft a, bool on)
        {
            if (a.countermeasureManager == null || a.countermeasureTrigger == on) return;
            if (on && a.countermeasureManager.activeIndex == byte.MaxValue) return;
            a.Countermeasures(on, a.countermeasureManager.activeIndex);
        }

        /// <summary>After a take-back or bingo, the doctrine's follow-on (spec M5 §6.1).</summary>
        private void FollowOn(WingMember m, TransitionReason reason)
        {
            WingConfig cfg = Plugin.Settings;
            if (cfg == null || !CombatDoctrine.FollowOn(reason, cfg.AfterWinchester.Value, cfg.AfterBingo.Value, out RecoveryIntent intent)) return;
            string why = reason == TransitionReason.Fuel ? "bingo fuel" : "Winchester";
            // Refit needs a land field; with none (a helicopter whose only field is a carrier) it goes home to the
            // reserve instead of flying on (review M5d I3). The toast says what actually happens.
            bool going = Recover(m, intent);
            if (!going && intent == RecoveryIntent.Refit) going = Recover(m, intent = RecoveryIntent.Rtb);
            WingToast.Show(going
                ? $"#{m.Number} {why}; {(intent == RecoveryIntent.Refit ? "going to refit" : "returning to base")}"
                : $"#{m.Number} {why}; no field to return to");
        }

        /// <summary>Spec M5 §6.4: an engaged member fighting on its own choice spreads off targets other members are already
        /// after and off the player's target: the game's score under <see cref="TargetSpread"/> pressure. The game's
        /// opportunity and bravery gate stand (only a non-null choice is re-scored).</summary>
        public void Spread(Aircraft a, List<WeaponStation> stations, ref CombatAI.TargetSearchResults result)
        {
            WingMember self = null;
            foreach (WingMember m in Members)
                if (ReferenceEquals(m.Aircraft, a)) self = m;
            if (self == null || !self.Engaged || self.AssignedTarget != null || a.NetworkHQ == null || a.NetworkHQ.trackingDatabase == null) return;
            int others = 0;
            foreach (WingMember m in Members)
            {
                if (ReferenceEquals(m, self) || !m.Engaged || others >= othersTargets.Length) continue;
                Unit t = NativeTarget(m);
                if (t != null) othersTargets[others++] = t;
            }
            List<Unit> playerTargets = Player != null && Player.weaponManager != null ? Player.weaponManager.GetTargetList() : null;
            GlobalPosition at = a.GlobalPosition();
            Unit best = null;
            WeaponStation bestStation = null;
            float bestScore = 0f;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in a.NetworkHQ.trackingDatabase)
            {
                TrackingInfo tracking = pair.Value;
                if (tracking == null || !tracking.TryGetUnit(out Unit u) || u == null || u.disabled || u.NetworkHQ == null || u.NetworkHQ == a.NetworkHQ) continue;
                if (!a.NetworkHQ.IsTargetPositionAccurate(u, TargetAccuracyMetres)) continue;
                float range = FastMath.Distance(tracking.GetPosition(), at);
                int committed = 0;
                for (int i = 0; i < others; i++)
                    if (ReferenceEquals(othersTargets[i], u)) committed++;
                bool players = playerTargets != null && playerTargets.Contains(u);
                for (int s = 0; s < stations.Count; s++)
                {
                    WeaponStation w = stations[s];
                    if (!Usable(a, w)) continue;
                    OpportunityThreat ot = CombatAI.AnalyzeTarget(w, a, tracking, 0f, range, 100f);
                    if (ot.opportunity <= 0f) continue;
                    int capacity = u is Missile ? 1 : System.Math.Max(1, System.Math.Min(4, (int)System.Math.Ceiling(w.WeaponInfo.CalcAttacksNeeded(u))));
                    float score = TargetSpread.Score(ot.opportunity, ot.threat, range, w.WeaponInfo.targetRequirements.maxRange, committed, players, capacity);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = u;
                    bestStation = w;
                }
            }
            for (int i = 0; i < others; i++) othersTargets[i] = null;
            if (best == null || (ReferenceEquals(best, result.target) && ReferenceEquals(bestStation, result.chosenWeaponStation))) return;
            result = new CombatAI.TargetSearchResults(best, bestStation, result.opportunity, result.outOfAmmo);
        }

        /// <summary>Every member flying with the wing fights on its own choices (an attack order ends: review M5b I1).
        /// Returns how many are engaged.</summary>
        public int Engage()
        {
            attackCount = 0;
            return EngageAll();
        }

        /// <summary>Every member flying with the wing switches to the game's combat state with no assigned target.</summary>
        private int EngageAll()
        {
            int n = 0;
            foreach (WingMember m in Members)
            {
                if (m.Released || m.OnGround || m.Recovery != null || !m.Alive) continue;
                m.AssignedTarget = null;
                m.Pilot.SetPrimaryTarget(null);
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
        private readonly float[] targetLost = new float[FormationCatalog.MaxSlots];

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
            int n = EngageAll();
            if (n == 0) attackCount = 0;
            reallocateClock = 0f;
            Allocate(0f);
            return n;
        }

        /// <summary>The attack order's targets across the engaged members; with none alive the order ends and members
        /// fight on their own choices.</summary>
        private void Allocate(float dt)
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
                if (m.Engaged && !m.Released && m.Alive && InNativeCombat(m) && k < engagedNow.Length) engagedNow[k++] = m;
            if (k == 0)
            {
                // Everyone taken back: the order ends rather than capturing the next Engage (review M5b I1).
                attackCount = 0;
                return;
            }
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
                targetLost[i] = m.TargetLost;
                Vec3 at = m.Aircraft.GlobalPosition().ToVec3();
                for (int t = 0; t < attackCount; t++)
                {
                    int cell = i * attackCount + t;
                    canAttack[cell] = targetAlive[t] && CanAttack(m.Aircraft, attackTargets[t]);
                    targetDistance[cell] = targetAlive[t] ? (attackTargets[t].GlobalPosition().ToVec3() - at).Length : float.MaxValue;
                    if (ReferenceEquals(m.AssignedTarget, attackTargets[t])) currentTarget[i] = t;
                }
            }
            TargetAllocator.Assign(k, attackCount, canAttack, targetDistance, targetAlive, currentTarget, targetLost, dt, nextTarget);
            for (int i = 0; i < k; i++)
            {
                engagedNow[i].TargetLost = targetLost[i];
                Assign(engagedNow[i], nextTarget[i] >= 0 ? attackTargets[nextTarget[i]] : null);
            }
        }

        private static void Assign(WingMember m, Unit target)
        {
            if (ReferenceEquals(m.AssignedTarget, target)) return;
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} target {(target != null ? target.unitName : "own choice")}");
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
            judge.Reset();
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
                FollowOn(m, reason);
            }
            JudgeOdds(dt);
            if (attackCount > 0 && (reallocateClock += dt) >= ReallocateSeconds)
            {
                float step = reallocateClock;
                reallocateClock = 0f;
                Allocate(step);
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
            // Recover never switches states, so it is safe inside the SwitchState prefix (review focus 1: a native
            // low-fuel exit with our bingo tripped goes home).
            FollowOn(m, reason);
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
