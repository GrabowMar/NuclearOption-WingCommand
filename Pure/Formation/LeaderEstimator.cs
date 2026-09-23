using System;

namespace WingCommand
{
// Filled by the engine's leader sensor (M1c) and the FlightSim; the mod assembly only reads it until then.
#pragma warning disable CS0649
    /// <summary>What the wing forms on: the player, another aircraft, or a unit on the ground or at sea (escort).</summary>
    internal enum AnchorKind : byte { Player, Aircraft, Ground }

    /// <summary>What the wing can observe about its anchor (its leader) this tick.</summary>
    internal struct AnchorSample
    {
        public AnchorKind Kind;
        public Vec3 Pos, Vel;
        public float BankDeg;
        /// <summary>False when the leader is gone (dead, ejected, despawned); the last estimate is kept.</summary>
        public bool Present;
        public bool Airborne;
        /// <summary>A player leader gets the faster filters (0.12 s, 360°/s).</summary>
        public bool IsPlayer;
        /// <summary>The leader's nose (unit, world; zero = unknown): the wing's frame below
        /// <see cref="LeaderEstimator.TrackHoldSpeed"/>, where the velocity no longer says which way it faces.</summary>
        public Vec3 Fwd;
        /// <summary>A helicopter leader flies at any speed, hovering included.</summary>
        public bool CanHover;
    }
#pragma warning restore CS0649

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
        public static float AccelTau = 0.25f, PlayerAccelTau = 0.12f, JerkLimit = 40f;
        public static float BankRateLimit = 180f, PlayerBankRateLimit = 360f, MinFlyingSpeed = 25f;
        /// <summary>Below this horizontal speed the track is the leader's nose, not its (drifting) velocity.</summary>
        public static float TrackHoldSpeed = 5f;

        private Vec3 lastVel, filtered, acc, track = Vec3.Forward;
        private float bank, turnRate;
        private bool primed;

        public LeaderEstimate Estimate;

        public LeaderEstimate Update(in AnchorSample s, float dt, float latency = 0f)
        {
            if (!s.Present)
            {
                primed = false;
                Estimate.Flying = false;
                return Estimate;
            }
            if (!primed)
            {
                lastVel = s.Vel;
                bank = s.BankDeg;
                filtered = Vec3.Zero;
                acc = Vec3.Zero;
                turnRate = 0f;
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
            Vec3 horizontal = s.Vel.Horizontal, nose = s.Fwd.Horizontal;
            if (horizontal.Length > TrackHoldSpeed) track = horizontal.Normalized;
            else if (nose.SqrLength > 0.25f) track = nose.Normalized;
            else if (horizontal.SqrLength > 1f) track = horizontal.Normalized;

            float d = Math.Max(0f, dt + latency);
            Estimate.Pos = s.Pos + s.Vel * d + acc * (0.5f * d * d);
            Estimate.Vel = s.Vel + acc * d;
            Estimate.Acc = acc;
            Estimate.Track = track;
            Estimate.BankDeg = bank;
            Estimate.TurnRate = turnRate;
            // "Flying" = the wing should form on it: an escorted ground unit always, an aircraft in the air at flying speed
            // (a helicopter at any speed).
            Estimate.Flying = s.Kind == AnchorKind.Ground || s.Airborne && (s.CanHover || s.Vel.Length >= MinFlyingSpeed);
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
