using System;

namespace WingCommand
{
    /// <summary>A rabbit circling a (possibly moving) centre clockwise at the orbit speed, on a 30°-bank
    /// radius. Tracking it flies the hold. It starts 30° ahead of the member's bearing from the centre, so the
    /// join is a smooth turn. Holds sit 500 m above the leader plus 100 m per slot number.</summary>
    internal struct HoldOrbit
    {
        public static float BankDeg = 30f, LeadDeg = 30f, BaseHeight = 500f, SlotHeight = 100f;

        public Vec3 Center;
        public float Radius, Speed;
        /// <summary>Bearing of the rabbit from the centre, radians clockwise from +z.</summary>
        public float Angle;

        public static float RadiusFor(float speed) => speed * speed / (Scalar.G * (float)Math.Tan(BankDeg * Scalar.Deg2Rad));

        public void Begin(Vec3 center, float speed, Vec3 memberPos)
        {
            Center = center;
            Speed = speed;
            Radius = RadiusFor(speed);
            Vec3 d = memberPos - center;
            Angle = (float)Math.Atan2(d.X, d.Z) + LeadDeg * Scalar.Deg2Rad;
        }

        public RefState Step(Vec3 center, Vec3 centerVel, float dt)
        {
            Center = center;
            if (Radius > 1f) Angle += Speed / Radius * dt;
            float s = (float)Math.Sin(Angle), c = (float)Math.Cos(Angle);
            var radial = new Vec3(s, 0f, c);
            var tangent = new Vec3(c, 0f, -s);
            return new RefState(center + radial * Radius, tangent * Speed + centerVel.Horizontal,
                radial * (-Speed * Speed / Math.Max(1f, Radius)));
        }
    }
}
