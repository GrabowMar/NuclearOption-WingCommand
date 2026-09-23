using System;

namespace WingCommand
{
    /// <summary>Where an air-started wingman appears (spec §8): 2 km behind and 150 m below the leader, spread
    /// toward its slot's side (trail slots go right), each later member one step further out. Raised to at least
    /// 300 m above the terrain under the spawn point. It flies the leader's flight path at the leader's velocity,
    /// but never slower than <see cref="MinSpeedFactor"/> × its own loaded minimum speed.</summary>
    internal static class AirStart
    {
        public static float BehindM = 2000f, BelowM = 150f, LateralM = 200f, LateralStepM = 150f;
        public static float TerrainClearanceM = 300f, MinSpeedFactor = 1.5f;

        /// <summary>Spawn velocity: the leader's, raised along its flight path to at least
        /// <see cref="MinSpeedFactor"/>·<paramref name="loadedMinimum"/> (a leader climbing slowly would otherwise
        /// start the wingman just above its stall).</summary>
        public static Vec3 Velocity(Vec3 leaderVel, float loadedMinimum)
        {
            float floor = MinSpeedFactor * loadedMinimum;
            return leaderVel.Length >= floor ? leaderVel : Direction(leaderVel) * floor;
        }

        public static Vec3 Heading(Vec3 leaderVel)
        {
            Vec3 h = leaderVel.Horizontal;
            return h.SqrLength > 1f ? h.Normalized : Vec3.Forward;
        }

        /// <summary>Unit direction to point a spawn along: the leader's full flight path (climb or dive included),
        /// so the air-start begins at the leader's angle of attack, not a large negative one.</summary>
        public static Vec3 Direction(Vec3 leaderVel) => leaderVel.SqrLength > 1f ? leaderVel.Normalized : Heading(leaderVel);

        /// <summary>Spawn point of wing slot <paramref name="slot"/> of <paramref name="def"/>. The slot number is
        /// the lateral step, so slots called one at a time never share a point.</summary>
        public static Vec3 ForSlot(FormationDefinition def, int slot, Vec3 leaderPos, Vec3 leaderVel, float groundY) =>
            Position(leaderPos, leaderVel, SlotSolver.SlotFor(def, slot).Right, slot, groundY);

        public static Vec3 Position(Vec3 leaderPos, Vec3 leaderVel, float slotRight, int index, float groundY)
        {
            Vec3 fwd = Heading(leaderVel);
            Vec3 right = Vec3.Cross(Vec3.Up, fwd);
            float side = slotRight < 0f ? -1f : 1f;
            Vec3 p = leaderPos - fwd * BehindM + right * (side * (LateralM + LateralStepM * index));
            float y = Math.Max(leaderPos.Y - BelowM, groundY + TerrainClearanceM);
            return new Vec3(p.X, y, p.Z);
        }
    }
}
