using System;

namespace WingCommand.FlightSim
{
    /// <summary>Kinematic leader: constant speed, level, coordinated turns from a bank schedule with a
    /// 90°/s roll rate. Provides exact slot references in its level heading frame.</summary>
    internal sealed class VirtualLeader
    {
        private const float RollRate = 90f;
        private float heading;

        public VirtualLeader(Vec3 position, float speed, float headingDeg)
        {
            Position = position;
            Speed = speed;
            heading = headingDeg * Scalar.Deg2Rad;
        }

        public Vec3 Position { get; private set; }
        public float Speed { get; }
        public float BankDeg { get; private set; }
        /// <summary>Turn rate, rad/s, positive right (heading increasing).</summary>
        public float TurnRate { get; private set; }
        /// <summary>Rate of change of the turn rate, rad/s², from the last step (roll-in and roll-out).</summary>
        public float TurnAccel { get; private set; }
        public float HeadingDeg => heading * Scalar.Rad2Deg;

        public void Step(float targetBankDeg, float dt)
        {
            BankDeg = ConstraintChain.SlewBank(BankDeg, targetBankDeg, RollRate * dt);
            float previous = TurnRate;
            TurnRate = Scalar.G * (float)Math.Tan(BankDeg * Scalar.Deg2Rad) / Speed;
            TurnAccel = (TurnRate - previous) / dt;
            heading += TurnRate * dt;
            Position += Vec3.FromHeading(heading * Scalar.Rad2Deg, Speed) * dt;
        }

        /// <summary>Slot reference: <paramref name="right"/> m to the right, <paramref name="aft"/> m behind,
        /// <paramref name="up"/> m above, fixed in the leader's level heading frame. Velocity and
        /// acceleration are exact: v = t̂(V − ω·right) − ĉ(ω·aft),
        /// a = ĉ·ω(V − ω·right) + t̂·ω²·aft − ω̇·(right·t̂ + aft·ĉ), the last term from changing turn rate.</summary>
        public RefState Slot(float right, float aft, float up)
        {
            Vec3 t = Vec3.FromHeading(heading * Scalar.Rad2Deg);
            Vec3 c = Vec3.Cross(Vec3.Up, t);
            float w = TurnRate;
            Vec3 pos = Position + c * right - t * aft + Vec3.Up * up;
            Vec3 vel = t * (Speed - w * right) - c * (w * aft);
            Vec3 acc = c * (w * (Speed - w * right)) + t * (w * w * aft) - (t * right + c * aft) * TurnAccel;
            return new RefState(pos, vel, acc);
        }
    }
}
