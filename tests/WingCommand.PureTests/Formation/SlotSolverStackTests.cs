using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class SlotSolverStackTests
    {
        private const float Dt = 1f / 60f;

        private static FormationDefinition Def(FormationModifiers modifiers) => new FormationDefinition
        {
            Id = "test", Slots = new[] { new SlotDef(1f, 1f, 0f) }, Element = new int[1], Modifiers = modifiers,
            SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 600f,
        };

        private static readonly LeaderEstimate Leader = new LeaderEstimate
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, 200f), Track = Vec3.Forward, Flying = true,
        };

        private static float SlotY(SlotSolver solver, FormationDefinition def, float seconds, float floorY = float.NaN)
        {
            var output = new SlotTarget[FormationCatalog.MaxSlots];
            var caps = new MemberCapability[FormationCatalog.MaxSlots];
            for (int i = 0; i < caps.Length; i++) caps[i] = new MemberCapability { MaxSpeed = 255f, MinSpeed = 80f };
            int ticks = Math.Max(1, (int)Math.Round(seconds / Dt));
            for (int i = 0; i < ticks; i++) solver.Solve(def, 80f, Leader, caps, 1, floorY, 60f, Dt, output);
            return output[0].Ref.Pos.Y;
        }

        [Fact]
        public void TheStackEasesAtItsRate()
        {
            var solver = new SlotSolver { StackOffset = 300f };
            Assert.Equal(2000f + SlotSolver.StackRate, SlotY(solver, Def(FormationModifiers.None), 1f), 0);
            Assert.Equal(2300f, SlotY(solver, Def(FormationModifiers.None), 30f), 1);
        }

        [Fact]
        public void LevelReturnsToTheShape()
        {
            var solver = new SlotSolver { StackOffset = -150f };
            SlotY(solver, Def(FormationModifiers.None), 20f);
            solver.StackOffset = 0f;
            Assert.Equal(2000f, SlotY(solver, Def(FormationModifiers.None), 20f), 1);
        }

        [Fact]
        public void GoLowReachesItsDepthInOpenAir()
        {
            var solver = new SlotSolver { StackOffset = -150f };
            Assert.Equal(1850f, SlotY(solver, Def(FormationModifiers.TerrainFlatten), 30f, floorY: 1700f), 1);
        }

        [Fact]
        public void AStackedSlotDoesNotSwingWhenTheLeaderRolls()
        {
            // Review M5f m1: the stack sat inside the rolled frame, so a close slot swung sideways with the leader's bank.
            var def = new FormationDefinition
            {
                Id = "t", Slots = new[] { new SlotDef(0f, 0.5f, 0f) }, Element = new int[1], Modifiers = FormationModifiers.None,
                SpacingMin = 40f, SpacingDefault = 40f, SpacingMax = 600f,
            };
            var output = new SlotTarget[FormationCatalog.MaxSlots];
            var caps = new MemberCapability[FormationCatalog.MaxSlots];
            for (int i = 0; i < caps.Length; i++) caps[i] = new MemberCapability { MaxSpeed = 255f, MinSpeed = 80f };
            var solver = new SlotSolver { StackOffset = 300f };
            LeaderEstimate level = Leader;
            for (int i = 0; i < 60 * 30; i++) solver.Solve(def, 40f, level, caps, 1, float.NaN, 60f, Dt, output);
            float x0 = output[0].Ref.Pos.X;
            LeaderEstimate banked = Leader;
            banked.BankDeg = 60f;
            for (int i = 0; i < 60; i++) solver.Solve(def, 40f, banked, caps, 1, float.NaN, 60f, Dt, output);
            Assert.True(Math.Abs(output[0].Ref.Pos.X - x0) < 5f, $"swung {output[0].Ref.Pos.X - x0:0.0} m");
        }

        [Fact]
        public void GoLowNeverUndercutsTheFloor()
        {
            var solver = new SlotSolver { StackOffset = -150f };
            Assert.True(SlotY(solver, Def(FormationModifiers.TerrainFlatten), 20f, floorY: 1900f) >= 1900f + 60f - 0.01f);
        }
    }
}
