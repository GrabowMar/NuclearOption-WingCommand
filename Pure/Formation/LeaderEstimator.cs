using System;

namespace WingCommand
{
    /// <summary>What the wing can observe about its leader this tick.</summary>
    internal struct LeaderSample
    {
        public Vec3 Pos, Vel;
        public float BankDeg;
        /// <summary>False when the leader is gone (dead, ejected, despawned); the last estimate is kept.</summary>
        public bool Present;
        public bool Airborne;
        /// <summary>A player leader gets the faster filters (0.12 s, 360°/s).</summary>
        public bool IsPlayer;
    }

    /// <summary>The wing's smoothed, one-tick-projected view of its leader, shared by every member.</summary>
    internal struct LeaderEstimate
    {
        public Vec3 Pos, Vel, Acc;
        /// <summary>Unit horizontal heading; keeps its last valid value in vertical flight.</summary>
        public Vec3 Track;
        public float BankDeg, BankRateDps;
        /// <summary>Horizontal turn rate, rad/s, positive right (heading increasing).</summary>
        public float TurnRate;
        public float TurnAccel;
        public bool Flying;

        public float Speed => Vel.Length;
    }

    /// <summary>Filters the leader once per wing per tick. Acceleration passes a first-order filter
    /// (τ 0.25 s, 0.12 s for a player) and a 40 m/s³ jerk limit. Bank is rate-limited to 180°/s (360°/s for
    /// a player). Position and velocity are projected one tick (plus any latency) ahead to cover the
    /// control latency.</summary>
    internal sealed class LeaderEstimator
    {
        public const float AccelTau = 0.25f, PlayerAccelTau = 0.12f, JerkLimit = 40f;
        public const float BankRateLimit = 180f, PlayerBankRateLimit = 360f, MinFlyingSpeed = 25f;

        private Vec3 lastVel, filtered, acc, track = Vec3.Forward;
        private float bank, turnRate;
        private bool primed;

        public LeaderEstimate Estimate;

        public LeaderEstimate Update(in LeaderSample s, float dt, float latency = 0f)
        {
            if (!s.Present)
            {
                Estimate.Flying = false;
                return Estimate;
            }
            if (!primed)
            {
                lastVel = s.Vel;
                bank = s.BankDeg;
                primed = true;
            }
            if (dt > 0f)
            {
                Vec3 raw = (s.Vel - lastVel) / dt;
                float k = 1f - (float)Math.Exp(-dt / (s.IsPlayer ? PlayerAccelTau : AccelTau));
                filtered += (raw - filtered) * k;
                Vec3 step = filtered - acc;
                float maxStep = JerkLimit * dt, length = step.Length;
                acc += length > maxStep ? step * (maxStep / length) : step;

                float previousBank = bank;
                bank = ConstraintChain.SlewBank(bank, s.BankDeg, (s.IsPlayer ? PlayerBankRateLimit : BankRateLimit) * dt);
                Estimate.BankRateDps = Scalar.Wrap180(bank - previousBank) / dt;
                float previousRate = turnRate;
                turnRate = TurnRateOf(s.Vel, acc);
                Estimate.TurnAccel = (turnRate - previousRate) / dt;
            }
            lastVel = s.Vel;
            Vec3 horizontal = s.Vel.Horizontal;
            if (horizontal.SqrLength > 1f) track = horizontal.Normalized;

            float d = Math.Max(0f, dt + latency);
            Estimate.Pos = s.Pos + s.Vel * d + acc * (0.5f * d * d);
            Estimate.Vel = s.Vel + acc * d;
            Estimate.Acc = acc;
            Estimate.Track = track;
            Estimate.BankDeg = bank;
            Estimate.TurnRate = turnRate;
            Estimate.Flying = s.Airborne && s.Vel.Length >= MinFlyingSpeed;
            return Estimate;
        }

        private static float TurnRateOf(Vec3 vel, Vec3 acc)
        {
            Vec3 horizontal = vel.Horizontal;
            float speed = horizontal.Length;
            if (speed < 1f) return 0f;
            return Vec3.Dot(acc, Vec3.Cross(Vec3.Up, horizontal / speed)) / speed;
        }
    }
}
