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

        /// <summary>Every engaged member back into formation (Commanded). Returns how many.</summary>
        public int Disengage()
        {
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
                };
                if (!CombatSupervisor.TakeBack(s, out TransitionReason reason)) continue;
                TakeBack(m, reason);
                if (reason == TransitionReason.Fuel)
                {
                    WingToast.Show($"#{m.Number} bingo fuel; returning to base");
                    Recover(m, RecoveryIntent.Rtb);
                }
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

        private void Disengaged(WingMember m, TransitionReason reason)
        {
            m.Engaged = false;
            m.AssignedTarget = null;
            m.Pilot?.SetPrimaryTarget(null);
            m.Brain.FormUp(missionTime, Events);
            Events.Push(new WingEvent { Time = missionTime, Member = m.Brain.Slot, Kind = WingEventKind.Disengaged, Reason = reason });
        }
    }
}
