using System;

namespace WingCommand
{
    /// <summary>Engine-free 3D vector in Unity's axes (x right/east, y up, z forward/north; left-handed).
    /// The engine adapter converts to and from UnityEngine.Vector3 at the sensing/actuation boundary.</summary>
    internal readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => default;
        public static Vec3 Up => new Vec3(0f, 1f, 0f);
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);
        public static Vec3 Right => new Vec3(1f, 0f, 0f);

        public float SqrLength => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(SqrLength);
        public Vec3 Horizontal => new Vec3(X, 0f, Z);

        public Vec3 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec3(X / l, Y / l, Z / l) : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Same formula as UnityEngine.Vector3.Cross, so handedness matches the engine.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        /// <summary>Compass heading of the horizontal component, degrees clockwise from +z, in [0, 360).</summary>
        public static float HeadingDeg(Vec3 v)
        {
            float deg = (float)(Math.Atan2(v.X, v.Z) * 180.0 / Math.PI);
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Horizontal vector of the given length pointing along a compass heading.</summary>
        public static Vec3 FromHeading(float headingDeg, float length = 1f)
        {
            double r = headingDeg * Math.PI / 180.0;
            return new Vec3((float)Math.Sin(r) * length, 0f, (float)Math.Cos(r) * length);
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }
}
