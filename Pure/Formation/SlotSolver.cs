using System;

namespace WingCommand
{
    /// <summary>One slot's reference for this tick, plus how it was shaped.</summary>
    internal struct SlotTarget
    {
        public RefState Ref;
        /// <summary>Lateral offset in metres after crossover and compression, + right.</summary>
        public float Lateral;
        public float RollFollow;
        public float FrameBankDeg;
        public int Element;
        public bool Crossing;
    }

    /// <summary>Speeds a member can fly now: its usable maximum and its loaded minimum.</summary>
    internal struct MemberCapability
    {
        public float MaxSpeed, MinSpeed;
    }

    /// <summary>Places every slot of one wing each tick through <see cref="TurnFrame"/> and the definition's
    /// modifiers:
    /// <list type="bullet">
    /// <item>TurnCompress scales a slot's lateral offset (critically damped, about 2 s) when its arc speed
    /// V − ω·right would leave the member's speed range.</item>
    /// <item>Crossover mirrors a wide slot after 60° of turn toward it. It takes 8 s, passing one spacing aft
    /// and one stack below; it re-arms when the turn reverses or after 20 s level.</item>
    /// <item>TerrainFlatten keeps slots above floor + clearance.</item>
    /// </list>
    /// Each slot's rolled frame uses its own bank, slewed so that rolling moves the slot at most 20 m/s. The
    /// roll-follow weight comes from the slot's own (compressed) lateral, not from a crossing in progress.
    /// Members beyond the definition's slots trail its last slot.</summary>
    internal sealed class SlotSolver
    {
        public const float SpeedMargin = 5f, CompressOmega = 2.4f, RollSwingMax = 20f;
        public const float CrossoverTriggerDeg = 60f, CrossoverSeconds = 8f, RearmLevelSeconds = 20f;
        public const float LevelTurnRate = 0.02f;

        private const int N = FormationCatalog.MaxSlots;
        private readonly float[] compress = new float[N], compressRate = new float[N];
        private readonly float[] frameBank = new float[N];
        private readonly bool[] framePrimed = new bool[N];
        private readonly float[] crossSign = new float[N], crossTime = new float[N];
        private float turnAccum, levelTime;
        private int turnDir;

        public SlotSolver()
        {
            for (int i = 0; i < N; i++)
            {
                compress[i] = 1f;
                crossSign[i] = 1f;
                crossTime[i] = -1f;
            }
        }

        public void Solve(FormationDefinition def, float spacing, in LeaderEstimate leader, MemberCapability[] caps,
            int count, float floorY, float clearance, float dt, SlotTarget[] output)
        {
            spacing = def.ClampSpacing(spacing);
            count = Math.Min(count, N);
            TrackTurn(leader.TurnRate, dt);
            for (int i = 0; i < count; i++)
            {
                SlotDef slot = SlotFor(def, i);
                float baseRight = slot.Right * spacing;
                float k = (def.Modifiers & FormationModifiers.TurnCompress) != 0
                    ? Compress(i, baseRight * crossSign[i], leader, caps[i], dt) : 1f;
                float sign = Crossover(i, def, baseRight, dt, out float dip, out float extraAft, out bool crossing);
                float right = baseRight * sign * k;
                float w = TurnFrame.RollFollowWeight(baseRight * k, slot.RollFollow);
                float bankRate = FrameBank(i, right, leader.BankDeg, dt);
                RefState r = TurnFrame.Evaluate(leader, frameBank[i], bankRate, right,
                    (slot.Aft + extraAft) * spacing, slot.Up * FormationCatalog.StackMetres + dip, w);
                if ((def.Modifiers & FormationModifiers.TerrainFlatten) != 0 && !float.IsNaN(floorY))
                    r = Flatten(r, floorY + clearance);
                output[i] = new SlotTarget
                {
                    Ref = r,
                    Lateral = right,
                    RollFollow = w,
                    FrameBankDeg = frameBank[i],
                    Element = def.Element.Length == 0 ? 0 : def.Element[Math.Min(i, def.Element.Length - 1)],
                    Crossing = crossing,
                };
            }
        }

        /// <summary>The definition's slot, or for members beyond it the last slot moved one spacing aft and
        /// one stack down per extra member.</summary>
        public static SlotDef SlotFor(FormationDefinition def, int i)
        {
            int n = def.Slots.Length;
            if (i < n) return def.Slots[i];
            SlotDef last = def.Slots[n - 1];
            int extra = i - n + 1;
            return new SlotDef(last.Right, last.Aft + extra, last.Up - extra, last.RollFollow);
        }

        private float FrameBank(int i, float right, float leaderBank, float dt)
        {
            if (!framePrimed[i])
            {
                frameBank[i] = leaderBank;
                framePrimed[i] = true;
                return 0f;
            }
            if (dt <= 0f) return 0f;
            float rate = RollSwingMax / Math.Max(1f, Math.Abs(right)) * Scalar.Rad2Deg;
            float previous = frameBank[i];
            frameBank[i] = ConstraintChain.SlewBank(previous, leaderBank, rate * dt);
            return Scalar.Wrap180(frameBank[i] - previous) / dt;
        }

        private float Compress(int i, float right, in LeaderEstimate leader, MemberCapability cap, float dt)
        {
            float target = 1f, lateral = Math.Abs(right), omega = leader.TurnRate;
            if (Math.Abs(omega) > 1e-3f && lateral > 1f)
            {
                float speed = leader.Vel.Horizontal.Length;
                float arc = speed - omega * right;
                float allowed = lateral;
                if (arc > cap.MaxSpeed - SpeedMargin)
                    allowed = Math.Max(0f, cap.MaxSpeed - SpeedMargin - speed) / Math.Abs(omega);
                else if (arc < cap.MinSpeed + SpeedMargin)
                    allowed = Math.Max(0f, speed - cap.MinSpeed - SpeedMargin) / Math.Abs(omega);
                target = Scalar.Clamp(allowed / lateral, FormationCatalog.CompressMin, 1f);
            }
            if (dt > 0f)
            {
                float accel = CompressOmega * CompressOmega * (target - compress[i]) - 2f * CompressOmega * compressRate[i];
                compressRate[i] += accel * dt;
                compress[i] = Scalar.Clamp(compress[i] + compressRate[i] * dt, FormationCatalog.CompressMin, 1f);
            }
            return compress[i];
        }

        private void TrackTurn(float turnRate, float dt)
        {
            if (Math.Abs(turnRate) < LevelTurnRate)
            {
                levelTime += dt;
                if (levelTime >= RearmLevelSeconds) turnAccum = 0f;
                return;
            }
            levelTime = 0f;
            int dir = Math.Sign(turnRate);
            if (dir != turnDir)
            {
                turnDir = dir;
                turnAccum = 0f;
            }
            turnAccum += Math.Abs(turnRate) * dt * Scalar.Rad2Deg;
        }

        private float Crossover(int i, FormationDefinition def, float baseRight, float dt,
            out float dip, out float extraAft, out bool crossing)
        {
            dip = 0f;
            extraAft = 0f;
            crossing = false;
            if ((def.Modifiers & FormationModifiers.Crossover) == 0) return 1f;
            float right = baseRight * crossSign[i];
            bool wide = TurnFrame.RollFollowWeight(right, -1f) < 0.5f;
            if (crossTime[i] < 0f && wide && turnDir != 0 && Math.Sign(right) == turnDir && turnAccum > CrossoverTriggerDeg)
                crossTime[i] = 0f;
            if (crossTime[i] < 0f) return crossSign[i];

            crossTime[i] += dt;
            float x = Math.Min(1f, crossTime[i] / CrossoverSeconds);
            if (x >= 1f)
            {
                crossSign[i] = -crossSign[i];
                crossTime[i] = -1f;
                return crossSign[i];
            }
            float bump = (float)Math.Sin(Math.PI * x);
            dip = -FormationCatalog.StackMetres * bump;
            extraAft = bump;
            crossing = true;
            return crossSign[i] * (float)Math.Cos(Math.PI * x);
        }

        private static RefState Flatten(in RefState r, float minY)
        {
            if (r.Pos.Y >= minY) return r;
            return new RefState(new Vec3(r.Pos.X, minY, r.Pos.Z),
                new Vec3(r.Vel.X, Math.Max(0f, r.Vel.Y), r.Vel.Z),
                new Vec3(r.Acc.X, Math.Max(0f, r.Acc.Y), r.Acc.Z));
        }
    }
}
