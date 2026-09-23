using System;

namespace WingCommand.FlightSim
{
    /// <summary>An aircraft that starts on the ground (spec M3 §9): a kinematic bicycle (nose wheel = yaw × steering lock,
    /// <see cref="MaxAccel"/> per unit of throttle, 4 m/s² per unit of brake, a little rolling drag) until it reaches
    /// its lift-off speed, then the fixed-wing plant at that speed and heading.</summary>
    internal sealed class DepartingPlant : ISimPlant
    {
        public static float MaxAccel = 6f;

        private readonly PlantParams wing;
        private readonly float liftSpeed, wheelbase, lockDeg;
        private FixedWingPlant air;
        private Vec3 pos, fwd;
        private float speed;

        public DepartingPlant(PlantParams wing, Pose at, float liftSpeed, float wheelbase, float lockDeg)
        {
            this.wing = wing;
            this.liftSpeed = liftSpeed;
            this.wheelbase = wheelbase;
            this.lockDeg = lockDeg;
            pos = at.Pos;
            fwd = at.Fwd.Horizontal.Normalized;
        }

        public bool Airborne => air != null;
        public Vec3 Position => air != null ? air.Position : pos;
        public Vec3 Velocity => air != null ? air.Velocity : fwd * speed;

        public AircraftState Read(float dt) => air != null
            ? air.Read(dt)
            : new AircraftState
            {
                Pos = pos, Vel = fwd * speed, Fwd = fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, fwd), Tas = speed,
                RadarAlt = 0f, Dt = dt,
            };

        public void Step(in ControlOutput o, float dt)
        {
            if (air != null)
            {
                air.Step(o, dt);
                return;
            }
            speed = Math.Max(0f, speed + (MaxAccel * Scalar.Clamp01(o.Throttle) - 4f * Scalar.Clamp01(o.Brake) - 0.05f) * dt);
            float delta = Scalar.Clamp(o.Yaw, -1f, 1f) * lockDeg * Scalar.Deg2Rad;
            float turn = speed * (float)Math.Tan(delta) / wheelbase * dt;
            Vec3 right = Vec3.Cross(Vec3.Up, fwd);
            fwd = (fwd * (float)Math.Cos(turn) + right * (float)Math.Sin(turn)).Normalized;
            pos += fwd * speed * dt;
            if (speed >= liftSpeed)
            {
                air = new FixedWingPlant(wing, pos + Vec3.Up * 1f, speed, Vec3.HeadingDeg(fwd));
                air.SetThrottleState(1f);
            }
        }

        /// <summary>What the engine's safe relocation does: the aircraft stands at the pose, stopped.</summary>
        public void Teleport(Pose to)
        {
            pos = to.Pos;
            fwd = to.Fwd.Horizontal.Normalized;
            speed = 0f;
        }
    }
}
