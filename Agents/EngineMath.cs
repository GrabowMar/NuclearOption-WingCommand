using UnityEngine;

namespace WingCommand
{
    /// <summary>Conversions between game types and Pure types. Pure works in global positions (floating-origin
    /// safe); only physics queries and rendering use local positions.</summary>
    internal static class EngineMath
    {
        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Vec3 ToVec3(this GlobalPosition g) => new Vec3(g.x, g.y, g.z);
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static GlobalPosition ToGlobal(this Vec3 v) => new GlobalPosition(v.X, v.Y, v.Z);
        public static Vector3 ToLocal(this Vec3 v) => new GlobalPosition(v.X, v.Y, v.Z).ToLocalPosition();
    }
}
