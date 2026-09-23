using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Ground height (terrain and statics) under global points, by a downward ray. Sea level (0) where
    /// nothing is hit or the ground is below the sea.</summary>
    internal static class TerrainProbe
    {
        public static readonly float[] LookAheadSeconds = { 0f, 2f, 5f, 10f };
        private const float CastHeight = 5000f;

        public static float GroundY(Vec3 at)
        {
            Vector3 local = at.ToLocal();
            var origin = new Vector3(local.x, local.y + CastHeight, local.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f * CastHeight + Math.Abs(at.Y), PhysicsLayers.StaticsMask))
                return Math.Max(0f, hit.point.GlobalY());
            return 0f;
        }

        /// <summary>Highest ground under the aircraft and 2, 5 and 10 s ahead on its horizontal path.</summary>
        public static float LookAhead(Vec3 pos, Vec3 vel)
        {
            Vec3 h = vel.Horizontal;
            float top = 0f;
            for (int i = 0; i < LookAheadSeconds.Length; i++) top = Math.Max(top, GroundY(pos + h * LookAheadSeconds[i]));
            return top;
        }

        /// <summary>Highest ground under the aircraft and 2 s ahead (the GCAS near floor).</summary>
        public static float Near(Vec3 pos, Vec3 vel) => Math.Max(GroundY(pos), GroundY(pos + vel.Horizontal * 2f));
    }
}
