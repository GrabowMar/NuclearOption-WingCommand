using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class SlotSolverTests
    {
        private const float Dt = 1f / 60f;

        private static FormationDefinition Def(FormationModifiers modifiers, params SlotDef[] slots) => new FormationDefinition
        {
            Id = "test", Slots = slots, Element = new int[slots.Length], Modifiers = modifiers,
            SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 600f,
        };

        private static MemberCapability[] Caps(float max = 255f, float min = 80f)
        {
            var caps = new MemberCapability[FormationCatalog.MaxSlots];
            for (int i = 0; i < caps.Length; i++) caps[i] = new MemberCapability { MaxSpeed = max, MinSpeed = min };
            return caps;
        }

        private static LeaderEstimate Leader(float speed = 200f, float rate = 0f, float bank = float.NaN) => new LeaderEstimate
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), Acc = new Vec3(speed * rate, 0f, 0f),
            Track = Vec3.Forward, TurnRate = rate, Flying = true,
            BankDeg = float.IsNaN(bank) ? (float)Math.Atan(speed * rate / Scalar.G) * Scalar.Rad2Deg : bank,
        };

        private static SlotTarget[] Run(SlotSolver solver, FormationDefinition def, float spacing, LeaderEstimate leader,
            float seconds, MemberCapability[] caps = null, float floorY = float.NaN)
        {
            var output = new SlotTarget[FormationCatalog.MaxSlots];
            caps = caps ?? Caps();
            int ticks = Math.Max(1, (int)Math.Round(seconds / Dt));
            for (int i = 0; i < ticks; i++) solver.Solve(def, spacing, leader, caps, def.Slots.Length, floorY, 60f, Dt, output);
            return output;
        }

        [Fact]
        public void StraightFlightPutsEachSlotAtItsOffset()
        {
            FormationDefinition def = Def(FormationModifiers.None, new SlotDef(-1f, 1f, 0f), new SlotDef(2f, 2f, -1f));
            SlotTarget[] s = Run(new SlotSolver(), def, 80f, Leader(), Dt);
            Assert.True((s[0].Ref.Pos - new Vec3(-80f, 2000f, -80f)).Length < 1e-2f, $"{s[0].Ref.Pos}");
            Assert.True((s[1].Ref.Pos - new Vec3(160f, 1990f, -160f)).Length < 1e-2f, $"{s[1].Ref.Pos}");
            Assert.True((s[0].Ref.Vel - new Vec3(0f, 0f, 200f)).Length < 1e-2f);
        }

        [Fact]
        public void SpacingIsClampedToTheFormationRange()
        {
            SlotTarget[] s = Run(new SlotSolver(), Def(FormationModifiers.None, new SlotDef(1f, 0f, 0f)), 1000f, Leader(), Dt);
            Assert.Equal(600f, s[0].Lateral, 2);
        }

        [Fact]
        public void OutsideSlotCompressesOnlyWhenItsArcSpeedWouldExceedItsMaximum()
        {
            FormationDefinition def = Def(FormationModifiers.TurnCompress, new SlotDef(-1f, 0f, 0f), new SlotDef(1f, 0f, 0f));
            LeaderEstimate turning = Leader(240f, 0.1f);
            SlotTarget[] s = Run(new SlotSolver(), def, 350f, turning, 5f);
            Assert.InRange(s[0].Lateral, -350f * FormationCatalog.CompressMin - 1f, -350f * FormationCatalog.CompressMin + 1f);   // needs 275 m/s, has 250
            Assert.Equal(350f, s[1].Lateral, 2);                                    // inside: 205 m/s is fine
            Assert.Equal(-350f, Run(new SlotSolver(), def, 350f, turning, 5f, Caps(max: 300f))[0].Lateral, 2);
            FormationDefinition plain = Def(FormationModifiers.None, new SlotDef(-1f, 0f, 0f));
            Assert.Equal(-350f, Run(new SlotSolver(), plain, 350f, turning, 5f)[0].Lateral, 2);
        }

        [Fact]
        public void CompressionEasesInWithoutAStep()
        {
            FormationDefinition def = Def(FormationModifiers.TurnCompress, new SlotDef(-1f, 0f, 0f));
            var solver = new SlotSolver();
            LeaderEstimate turning = Leader(240f, 0.1f);
            Assert.True(Math.Abs(Run(solver, def, 350f, turning, Dt)[0].Lateral + 350f) < 0.5f);
            float settled = Run(solver, def, 350f, turning, 3f)[0].Lateral;
            Assert.InRange(settled, -350f * FormationCatalog.CompressMin * 1.02f, -350f * FormationCatalog.CompressMin * 0.98f);
        }

        [Fact]
        public void CloseSlotFrameBankIsRateLimitedBySwingSpeed()
        {
            FormationDefinition def = Def(FormationModifiers.None, new SlotDef(1f, 0f, 0f));
            var solver = new SlotSolver();
            Run(solver, def, 40f, Leader(bank: 0f), Dt);
            SlotTarget[] s = Run(solver, def, 40f, Leader(bank: 60f), 0.1f);
            float limit = SlotSolver.RollSwingMax / 40f * Scalar.Rad2Deg * 0.1f;
            Assert.InRange(s[0].FrameBankDeg, limit - 0.1f, limit + 0.01f);
        }

        [Fact]
        public void CrossoverMirrorsAWideSlotAfterSixtyDegreesOfTurnTowardIt()
        {
            FormationDefinition def = Def(FormationModifiers.Crossover, new SlotDef(1f, 0f, 0f));
            var solver = new SlotSolver();
            var output = new SlotTarget[FormationCatalog.MaxSlots];
            MemberCapability[] caps = Caps();
            LeaderEstimate turning = Leader(200f, 0.1f);   // 5.73°/s to the right, toward the slot
            float previous = 350f, lastStep = 0f, maxStep = 0f, maxStepChange = 0f, lowest = float.MaxValue, startedAt = float.NaN;
            for (int i = 0; i < 20 * 60; i++)
            {
                solver.Solve(def, 350f, turning, caps, 1, float.NaN, 60f, Dt, output);
                float step = output[0].Lateral - previous;
                maxStep = Math.Max(maxStep, Math.Abs(step));
                maxStepChange = Math.Max(maxStepChange, Math.Abs(step - lastStep));   // velocity continuity
                lastStep = step;
                previous = output[0].Lateral;
                lowest = Math.Min(lowest, output[0].Ref.Pos.Y);
                if (float.IsNaN(startedAt) && output[0].Crossing) startedAt = i * Dt;
            }
            Assert.InRange(startedAt, 10.2f, 10.7f);   // 60° at 5.73°/s = 10.5 s
            Assert.Equal(-350f, output[0].Lateral, 1);
            Assert.False(output[0].Crossing);
            Assert.True(maxStep < 3f, $"lateral jumped {maxStep:0.00} m in one tick");
            Assert.True(maxStepChange < 0.05f, $"lateral velocity jumped {maxStepChange / Dt:0.0} m/s in one tick");
            Assert.InRange(lowest, 2000f - FormationCatalog.StackMetres - 0.5f, 2000f - FormationCatalog.StackMetres + 0.5f);
        }

        [Fact]
        public void CrossoverRepeatsOnlyAfterTheTurnReversesTowardTheSlotAgain()
        {
            FormationDefinition def = Def(FormationModifiers.Crossover, new SlotDef(1f, 0f, 0f));
            var solver = new SlotSolver();
            Assert.Equal(-350f, Run(solver, def, 350f, Leader(200f, 0.1f), 40f)[0].Lateral, 1);
            Assert.Equal(350f, Run(solver, def, 350f, Leader(200f, -0.1f), 20f)[0].Lateral, 1);
        }

        [Fact]
        public void TerrainFlattenKeepsSlotsAboveFloorPlusClearance()
        {
            FormationDefinition flat = Def(FormationModifiers.TerrainFlatten, new SlotDef(0f, 1f, -5f));
            Assert.Equal(2020f, Run(new SlotSolver(), flat, 80f, Leader(), Dt, floorY: 1960f)[0].Ref.Pos.Y, 2);
            Assert.Equal(1950f, Run(new SlotSolver(), flat, 80f, Leader(), Dt)[0].Ref.Pos.Y, 2);
        }

        [Fact]
        public void ExtraMembersExtendTheLastSlotAstern()
        {
            FormationDefinition def = Def(FormationModifiers.None, new SlotDef(1f, 1f, 0f));
            var output = new SlotTarget[FormationCatalog.MaxSlots];
            new SlotSolver().Solve(def, 80f, Leader(), Caps(), 3, float.NaN, 60f, Dt, output);
            Assert.True((output[1].Ref.Pos - new Vec3(80f, 1990f, -160f)).Length < 1e-2f, $"{output[1].Ref.Pos}");
            Assert.True((output[2].Ref.Pos - new Vec3(80f, 1980f, -240f)).Length < 1e-2f, $"{output[2].Ref.Pos}");
        }
    }
}
