using System;

// Shared engine stubs used by multiple linked production tests; keep test-specific types local to avoid
// duplicate definitions.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public sealed class Transform
    {
        public Vector3 position;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
    }
}

namespace WingCommand
{
    internal sealed class WingPilot { }
    internal sealed partial class Pilot { }
}
