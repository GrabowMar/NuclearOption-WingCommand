using System;

namespace WingCommand
{
// Filled by the engine sensor from the game's aircraft, and by tests.
#pragma warning disable CS0649
    /// <summary>One tick of raw engine readings in Wing Command's world frame: global position (floating-origin
    /// safe) and Unity axes. AngularVelocity is the cockpit rigidbody's rate in its own local axes (rad/s, Unity
    /// signs: +x pitches the nose down, +y yaws right, +z rolls left). GroundSpeed is the game's speed relative
    /// to what is under the aircraft; it gates the fly-by-wire.</summary>
    internal struct RawAircraftSample
    {
        public Vec3 Pos, Vel, Fwd, Up, Right, AngularVelocity, Wind;
        public float AirDensity, RadarAlt, GroundSpeed, Throttle;
    }
#pragma warning restore CS0649

    /// <summary>Turns raw engine readings into <see cref="AircraftState"/>:
    /// <list type="bullet">
    /// <item>acceleration from filtered velocity differences (τ <see cref="AccelTau"/>);</item>
    /// <item>bank measured from the horizon;</item>
    /// <item>Pure signs for body rates;</item>
    /// <item>air data from the air-relative velocity.</item>
    /// </list>
    /// One instance per aircraft, because it keeps the filter. A read with dt = 0 peeks: it does not advance
    /// the filter.</summary>
    internal sealed class AircraftSensorCore
    {
        public static float AccelTau = 0.05f;
        public static float FbwMinSpeed = 25f, FbwMinRadarAlt = 1f;

        private Vec3 lastVel, acc;
        private bool primed;

        public AircraftState Read(in RawAircraftSample r, float dt)
        {
            if (!primed)
            {
                lastVel = r.Vel;
                primed = true;
            }
            if (dt > 0f)
            {
                Vec3 raw = (r.Vel - lastVel) / dt;
                acc += (raw - acc) * (1f - (float)Math.Exp(-dt / AccelTau));
                lastVel = r.Vel;
            }

            Vec3 air = r.Vel - r.Wind;
            float tas = air.Length, speed = r.Vel.Length;
            return new AircraftState
            {
                Pos = r.Pos, Vel = r.Vel, Acc = acc, Fwd = r.Fwd, Up = r.Up, Right = r.Right,
                BankDeg = BankOf(r.Fwd, r.Right),
                PitchDeg = Asin(r.Fwd.Y),
                GammaDeg = speed > 1f ? Asin(r.Vel.Y / speed) : 0f,
                SideslipDeg = tas > 1f ? Asin(Vec3.Dot(air, r.Right) / tas) : 0f,
                P = -r.AngularVelocity.Z * Scalar.Rad2Deg,
                Q = -r.AngularVelocity.X * Scalar.Rad2Deg,
                R = r.AngularVelocity.Y * Scalar.Rad2Deg,
                Tas = tas,
                Qbar = 0.5f * r.AirDensity * tas * tas,
                Nz = Vec3.Dot(acc, r.Up) / Scalar.G + r.Up.Y,
                RadarAlt = r.RadarAlt,
                Throttle = r.Throttle,
                FbwActive = r.GroundSpeed >= FbwMinSpeed && r.RadarAlt >= FbwMinRadarAlt,
                AirbrakeOpen = r.Throttle <= 0f && r.RadarAlt > 1f,
                Dt = dt,
            };
        }

        /// <summary>Bank, positive right wing down: the angle from the horizon's right to the wing's right about
        /// the nose. Zero within about 8° of vertical, where the horizon is undefined.</summary>
        public static float BankOf(Vec3 fwd, Vec3 right)
        {
            Vec3 horizon = Vec3.Cross(Vec3.Up, fwd);
            if (horizon.SqrLength < 0.02f) return 0f;
            horizon = horizon.Normalized;
            return (float)Math.Atan2(Vec3.Dot(Vec3.Cross(right, horizon), fwd), Vec3.Dot(right, horizon)) * Scalar.Rad2Deg;
        }

        private static float Asin(float x) => (float)Math.Asin(Scalar.Clamp(x, -1f, 1f)) * Scalar.Rad2Deg;
    }
}
