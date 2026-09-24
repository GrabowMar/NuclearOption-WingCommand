namespace WingCommand
{
    /// <summary>Helicopters land here and take off (spec M4 §5): each rotary member flying with the wing settles at its
    /// slot's ground point around the anchor and waits; Take Off or Form Up lifts it off to rejoin.</summary>
    internal sealed partial class WingService
    {
        /// <summary>Settles every rotary member flying with the wing. Returns how many; <paramref name="refusal"/> says why
        /// none did.</summary>
        public int LandHere(out string refusal)
        {
            refusal = null;
            if (Wing == null || Wing.Frame == null)
            {
                refusal = "no formation to land from";
                return 0;
            }
            int rotary = 0, free = 0, n = 0;
            foreach (WingMember m in Members)
            {
                if (m.Profile.Class == AirframeClass.FixedWing) continue;
                rotary++;
                if (m.Released || !m.Alive || m.Engaged || m.Recovery != null || m.OnGround || m.Settle != null) continue;
                if (m.Brain.Slot < 0 || m.Brain.Slot >= Wing.Frame.Slots.Length) continue;
                free++;
                Vec3 slot = Wing.Frame.Slots[m.Brain.Slot].Ref.Pos;
                // Dry, level ground only (review M4c I5): no settle onto water or a slope.
                if (!TerrainProbe.Landing(slot, out float groundY, out float normalY) || !SettlePilot.Landable(true, normalY)) continue;
                EndDefence(m);   // review M4c I6: no countermeasure trigger held through the landing
                m.Settle = new SettlePilot(new Vec3(slot.X, groundY, slot.Z), Vec3.HeadingDeg(m.Last.Fwd), missionTime);
                n++;
            }
            if (rotary == 0) refusal = "no helicopters in the wing";
            else if (free == 0) refusal = "no helicopter free to land";
            else if (n == 0) refusal = "no dry, level ground here";
            Plugin.Logger.LogInfo($"[Wing] land here: {n} of {rotary} helicopters{(refusal != null ? " (" + refusal + ")" : "")}");
            return n;
        }

        /// <summary>Every settled member lifts off. Returns how many.</summary>
        public int TakeOff()
        {
            int n = 0;
            foreach (WingMember m in Members)
                if (m.Settle != null && m.Settle.Phase != SettlePhase.Done)
                {
                    m.Settle.TakeOff();
                    n++;
                }
            return n;
        }

        /// <summary>The wing as snapshot entries (spec M6 §8). Returns how many were written.</summary>
        public int FillSnapshot(SnapshotMember[] into)
        {
            int n = 0;
            foreach (WingMember m in Members)
            {
                if (n >= into.Length || m.Released || !m.Alive) continue;
                MemberDuty duty = m.Engaged ? MemberDuty.Engaged
                    : m.Recovery != null ? MemberDuty.Recovering
                    : m.Brain.Mind.Current == BehaviourId.Defend ? MemberDuty.Defending
                    : m.Settle != null ? MemberDuty.Settled
                    : m.OnGround ? MemberDuty.Grounded
                    : MemberDuty.Formation;
                float ammo = AmmoFraction(m.Aircraft);
                into[n++] = SnapshotBuilder.Member(m.Aircraft.persistentID.Id, m.Brain.Slot, (byte)m.Brain.Mind.Current, duty,
                    m.Aircraft.GetFuelLevel(), ammo, m.Brain.LastRejoin.FallingBehind, m.Bingo.Bingo, m.Bingo.Joker, ammo <= 0f);
            }
            return n;
        }

        /// <summary>Members per settle phase (automation reads it).</summary>
        public int Settled(SettlePhase phase)
        {
            int n = 0;
            foreach (WingMember m in Members)
                if (m.Settle != null && m.Settle.Phase == phase) n++;
            return n;
        }

        /// <summary>A settled member flies its settle instead of the formation (and is on the ground with the fly-by-wire
        /// off, so the no-FBW release must not fire); once lifted off it rejoins. True when it was flown here.</summary>
        private bool StepSettle(WingMember m, WingFrame frame, float dt)
        {
            SettlePilot s = m.Settle;
            if (s == null) return false;
            if (s.Phase == SettlePhase.Done)
            {
                m.Settle = null;
                m.NoFbwSeconds = 0f;
                m.Brain.FormUp(missionTime, Events);
                return false;
            }
            // The approach from afar flies under the wing's terrain floor and collision bias (review M4c I4).
            int slot = m.Brain.Slot;
            bool near = frame.HasNearFloor[slot];
            var approach = new LimitContext
            {
                FloorY = near ? frame.NearFloorY[slot] : frame.FloorY, NearFloorY = frame.NearFloorY[slot], HasNearFloor = near,
                Clearance = m.Brain.Clearance, Aggression = 0.3f, CollisionBias = frame.Bias[slot],
            };
            ControlWriter.Fly(m.Aircraft, s.Step(m.Last, m.Profile, m.Brain.Pipeline, missionTime, dt, Events, slot, approach), m.Profile.Class);
            return true;
        }
    }
}
