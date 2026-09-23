using System;

namespace WingCommand.FlightSim
{
    /// <summary>Kinematic leader: coordinated turns from a bank schedule (90°/s roll), speed changes at
    /// 8 m/s² and flight-path changes at 5°/s. Provides exact slot references in its level heading frame (M1a
    /// scenarios) and a <see cref="LeaderSample"/> for the formation layer.</summary>
    internal sealed class VirtualLeader
    {
        private const float RollRate = 90f;
        public const float SpeedRate = 8f, GammaRate = 5f;
        private float heading;
        /// <summary>A helicopter leader: it may slow to a hover, and the wing reads its nose.</summary>
        public bool CanHover;
        /// <summary>Speed change rate, m/s² (a helicopter brakes far more gently than 8).</summary>
        public float SpeedChangeRate = SpeedRate;

        public VirtualLeader(Vec3 position, float speed, float headingDeg)
        {
            Position = position;
            Speed = speed;
            heading = headingDeg * Scalar.Deg2Rad;
        }

        public Vec3 Position { get; private set; }
        public float Speed { get; private set; }
        public float GammaDeg { get; private set; }
        public float BankDeg { get; private set; }
        /// <summary>Turn rate, rad/s, positive right (heading increasing).</summary>
        public float TurnRate { get; private set; }
        /// <summary>Rate of change of the turn rate, rad/s², from the last step (roll-in and roll-out).</summary>
        public float TurnAccel { get; private set; }
        public float HeadingDeg => heading * Scalar.Rad2Deg;

        public Vec3 Velocity =>
            Vec3.FromHeading(HeadingDeg, HorizontalSpeed) + Vec3.Up * (Speed * (float)Math.Sin(GammaDeg * Scalar.Deg2Rad));

        private float HorizontalSpeed => Speed * (float)Math.Cos(GammaDeg * Scalar.Deg2Rad);

        public void Step(float targetBankDeg, float dt) => Step(targetBankDeg, dt, Speed, GammaDeg);

        public void Step(float targetBankDeg, float dt, float targetSpeed, float targetGammaDeg)
        {
            BankDeg = ConstraintChain.SlewBank(BankDeg, targetBankDeg, RollRate * dt);
            Speed = Slew.Step(Speed, targetSpeed, SpeedChangeRate, dt);
            GammaDeg = Slew.Step(GammaDeg, targetGammaDeg, GammaRate, dt);
            float previous = TurnRate;
            TurnRate = Scalar.G * (float)Math.Tan(BankDeg * Scalar.Deg2Rad) / Math.Max(1f, HorizontalSpeed);
            TurnAccel = (TurnRate - previous) / dt;
            heading += TurnRate * dt;
            Position += Velocity * dt;
        }

        /// <summary>What the formation layer observes: a present, airborne player leader.</summary>
        public LeaderSample Sample() => new LeaderSample
        {
            Pos = Position, Vel = Velocity, BankDeg = BankDeg, Present = true, Airborne = true, IsPlayer = true,
            Fwd = Vec3.FromHeading(HeadingDeg), CanHover = CanHover,
        };

        /// <summary>Slot reference: <paramref name="right"/> m to the right, <paramref name="aft"/> m behind,
        /// <paramref name="up"/> m above, fixed in the leader's level heading frame. Velocity and
        /// acceleration are exact: v = t̂(V − ω·right) − ĉ(ω·aft),
        /// a = ĉ·ω(V − ω·right) + t̂·ω²·aft − ω̇·(right·t̂ + aft·ĉ), the last term from changing turn rate.</summary>
        public RefState Slot(float right, float aft, float up)
        {
            Vec3 t = Vec3.FromHeading(HeadingDeg);
            Vec3 c = Vec3.Cross(Vec3.Up, t);
            float w = TurnRate, v = HorizontalSpeed;
            Vec3 pos = Position + c * right - t * aft + Vec3.Up * up;
            Vec3 vel = t * (v - w * right) - c * (w * aft);
            Vec3 acc = c * (w * (v - w * right)) + t * (w * w * aft) - (t * right + c * aft) * TurnAccel;
            return new RefState(pos, vel, acc);
        }
    }
}
