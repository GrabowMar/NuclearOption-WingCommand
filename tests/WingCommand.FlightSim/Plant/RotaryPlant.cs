using System;

namespace WingCommand.FlightSim
{
    /// <summary>Helicopter parameters: a UH-90-like set from the native defaults and the first in-game runs.</summary>
    internal sealed class RotaryParams
    {
        public float MassKg = 5000f;
        /// <summary>Full-collective thrust at rest in multiples of the weight (hover at 1/ThrustToWeight collective).</summary>
        public float ThrustToWeight = 1.8f;
        /// <summary>Speed (m/s) at which the thrust per unit of collective has halved: the game's blade-element rotor
        /// needs more collective the faster it flies (the UH-90 cannot hold its height at 107 m/s with full
        /// collective), which caps the level speed near 78 m/s here.</summary>
        public float ThrustHalfSpeed = 120f;
        /// <summary>Rotor speed: collective above <see cref="SustainCollective"/> asks more power than the engines give,
        /// so the rotor droops at DroopRate per unit of excess per second; it recovers with RpmRecoverTau. Thrust
        /// scales with rpm² (the game's RotorShaft: engine torque is power / ω, capped).</summary>
        public float SustainCollective = 0.85f, DroopRate = 0.4f, RpmRecoverTau = 2f;
        /// <summary>Quadratic drag, 1/m: about 70 m/s at 20° of tilt.</summary>
        public float DragK = 7.3e-4f;
        /// <summary>Helo FBW maxAngularVel (x pitch, y yaw, z roll), rad/s, and its pitch-rate g limit.</summary>
        public float MaxPitchRate = 1f, MaxYawRate = 2f, MaxRollRate = 2f, GLimit = 3f;
        public float RateLagS = 0.15f, CollectiveLagS = 0.3f;

        public static RotaryParams Utility => new RotaryParams();
    }

    /// <summary>Helicopter plant for the FlightSim (spec M2 §8.1): a point mass carried by rotor thrust along the disc
    /// normal. The disc attitude follows the helo fly-by-wire's rate command (target = stick·maxAngularVel, the pitch
    /// target capped by gLimit·g/max(speed, 10), reached through a first-order lag); the collective lags; drag is
    /// quadratic. Thrust = collective·ThrustToWeight·g·rpm² / (1 + (V/ThrustHalfSpeed)²), and the rotor droops under
    /// a collective the engines cannot sustain (<see cref="RotaryParams"/>). Euler angles are integrated from the body
    /// rates (fine at ≤ 30° of tilt). No ground effect, no vortex ring. Pure signs: pitch + nose up, roll + right,
    /// yaw + nose right.</summary>
    internal sealed class RotaryPlant : ISimPlant
    {
        private const float Deg = (float)(Math.PI / 180.0);
        private readonly RotaryParams p;
        private float pitch, roll, heading, pitchRate, rollRate, yawRate;

        public RotaryPlant(RotaryParams parameters, Vec3 position, Vec3 velocity, float headingDeg)
        {
            p = parameters;
            Position = position;
            Velocity = velocity;
            heading = headingDeg * Deg;
            Collective = HoverCollective;
        }

        public Vec3 Position { get; private set; }
        public Vec3 Velocity { get; private set; }
        /// <summary>Ground height (NaN: none): the aircraft sits on it, its sink stopped and its slide damped.</summary>
        public float GroundY = float.NaN;
        public float GroundFriction = 3f;
        /// <summary>The hardest sink speed at contact with the ground so far (review M4c I1: the last-contact value was
        /// overwritten every tick while down).</summary>
        public float ContactSpeed { get; private set; }
        public Vec3 Acceleration { get; private set; }
        public float Collective { get; private set; }
        /// <summary>Rotor speed over nominal.</summary>
        public float Rpm { get; private set; } = 1f;
        public float HoverCollective => 1f / p.ThrustToWeight;
        public float PitchDeg => pitch / Deg;
        public float RollDeg => roll / Deg;
        public float HeadingDeg => heading / Deg;

        public Vec3 Forward => Basis().fwd;
        public Vec3 Up => Basis().up;
        public Vec3 Right => Basis().right;

        public void SetAttitude(float pitchDeg, float rollDeg)
        {
            pitch = pitchDeg * Deg;
            roll = rollDeg * Deg;
        }

        public void SetCollective(float collective) => Collective = collective;

        public void Step(in ControlOutput input, float dt)
        {
            float speed = Velocity.Length;
            float pitchLimit = p.GLimit * Scalar.G / Math.Max(speed, 10f);
            float pitchTarget = Scalar.Clamp(Scalar.Clamp(input.Pitch, -1f, 1f) * p.MaxPitchRate, -pitchLimit, pitchLimit);
            float rollTarget = Scalar.Clamp(input.Roll, -1f, 1f) * p.MaxRollRate;
            float yawTarget = Scalar.Clamp(input.Yaw, -1f, 1f) * p.MaxYawRate;
            float k = 1f - (float)Math.Exp(-dt / p.RateLagS);
            pitchRate += (pitchTarget - pitchRate) * k;
            rollRate += (rollTarget - rollRate) * k;
            yawRate += (yawTarget - yawRate) * k;
            pitch += pitchRate * dt;
            roll += rollRate * dt;
            heading += yawRate * dt;
            Collective += (Scalar.Clamp01(input.Throttle) - Collective) * (1f - (float)Math.Exp(-dt / p.CollectiveLagS));
            float excess = Collective - p.SustainCollective;
            Rpm = excess > 0f ? Math.Max(0.3f, Rpm - p.DroopRate * excess * dt)
                : Rpm + (1f - Rpm) * (1f - (float)Math.Exp(-dt / p.RpmRecoverTau));

            float perCollective = p.ThrustToWeight * Scalar.G * Rpm * Rpm / (1f + speed * speed / (p.ThrustHalfSpeed * p.ThrustHalfSpeed));
            Vec3 thrust = Basis().up * (Collective * perCollective);
            Vec3 drag = Velocity * (-p.DragK * speed);
            Acceleration = thrust + drag - Vec3.Up * Scalar.G;
            Velocity += Acceleration * dt;
            Position += Velocity * dt;
            if (!float.IsNaN(GroundY) && Position.Y <= GroundY)
            {
                if (Velocity.Y < 0f) ContactSpeed = Math.Max(ContactSpeed, -Velocity.Y);
                Position = new Vec3(Position.X, GroundY, Position.Z);
                float slide = Math.Min(1f, GroundFriction * dt);
                Velocity = new Vec3(Velocity.X * (1f - slide), Math.Max(0f, Velocity.Y), Velocity.Z * (1f - slide));
            }
        }

        /// <summary>What the engine sensor would read.</summary>
        public AircraftState Read(float dt)
        {
            (Vec3 fwd, Vec3 up, Vec3 right) = Basis();
            float speed = Velocity.Length;
            return new AircraftState
            {
                Pos = Position, Vel = Velocity, Acc = Acceleration, Fwd = fwd, Up = up, Right = right,
                BankDeg = RollDeg, PitchDeg = PitchDeg,
                GammaDeg = speed > 1f ? (float)Math.Asin(Scalar.Clamp(Velocity.Y / speed, -1f, 1f)) / Deg : 0f,
                P = rollRate / Deg, Q = pitchRate / Deg, R = yawRate / Deg,
                Tas = speed, Qbar = Isa.DynamicPressure(Position.Y, speed), Nz = Collective * p.ThrustToWeight,
                RadarAlt = Position.Y, Throttle = Collective, FbwActive = true, Dt = dt, RotorRpm = Rpm,
            };
        }

        /// <summary>Body axes from heading, then pitch about the level right axis, then roll about the nose.</summary>
        private (Vec3 fwd, Vec3 up, Vec3 right) Basis()
        {
            var fwd0 = new Vec3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading));
            var right0 = new Vec3((float)Math.Cos(heading), 0f, -(float)Math.Sin(heading));
            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch), cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
            Vec3 fwd = fwd0 * cp + Vec3.Up * sp;
            Vec3 up1 = Vec3.Up * cp - fwd0 * sp;
            return (fwd, up1 * cr + right0 * sr, right0 * cr - up1 * sr);
        }
    }
}
