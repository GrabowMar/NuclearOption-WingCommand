using System;
using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationTerrainBankTests
    {
        [Theory]
        [InlineData(0f, 100f)]
        [InlineData(8f, 300f)]
        [InlineData(-8f, 300f)]
        [InlineData(80f, 2000f)]
        [InlineData(-80f, 2000f)]
        public void SafeSmallTiltsAndHighAltitudeBankRemainUnchanged(float requested, float radarAltitude)
        {
            Assert.Equal(requested, FormationCollision.TerrainBank(requested, radarAltitude,
                45f, 500f, 15f, 300f), 4);
        }

        [Fact]
        public void LowLineAbreastKeepsOuterSlotsAboveTheTerrainFloor()
        {
            const float radarAltitude = 100f, floor = 45f, arm = 264f, stack = 3f;
            float oldBank = 80f * radarAltitude / 150f;
            Assert.True(LowestAltitude(oldBank, radarAltitude, arm, stack) < floor);
            float bank = FormationCollision.TerrainBank(oldBank, radarAltitude, floor, arm, stack, 0f);
            Assert.InRange(bank, 0f, oldBank - 1f);
            Assert.True(LowestAltitude(bank, radarAltitude, arm, stack) >= floor - 0.001f);
            Assert.Equal(-bank, FormationCollision.TerrainBank(-oldBank, radarAltitude,
                floor, arm, stack, 0f), 4);
        }

        [Fact]
        public void LargerWingsAndLowerTerrainMarginsReduceTheWholeFormationBank()
        {
            float smallWing = FormationCollision.TerrainBank(75f, 180f, 45f, 180f, 15f, 0f);
            float bigWing = FormationCollision.TerrainBank(75f, 180f, 45f, 720f, 15f, 0f);
            float risingTerrain = FormationCollision.TerrainBank(75f, 110f, 45f, 720f, 15f, 0f);
            Assert.True(smallWing > bigWing);
            Assert.True(bigWing > risingTerrain);
            Assert.True(risingTerrain > 0f);
        }

        [Fact]
        public void StepDownAndClimbingTrailConsumeClearanceBeforeRoll()
        {
            float level = FormationCollision.TerrainBank(70f, 200f, 45f, 350f, 0f, 600f);
            float stacked = FormationCollision.TerrainBank(70f, 200f, 45f, 350f, 25f, 600f);
            float climbing = FormationCollision.TerrainBank(70f, 200f, 45f, 350f, 25f, 600f, 0.1f);
            Assert.True(level > stacked);
            Assert.True(stacked > climbing);
            Assert.True(climbing > 0f);
        }

        [Theory]
        [InlineData(40f, 10f)]
        [InlineData(60f, 20f)]
        [InlineData(-5f, 0f)]
        public void AtTheTerrainFloorLateralLanesStayLevel(float altitude, float stack)
        {
            Assert.Equal(0f, FormationCollision.TerrainBank(80f, altitude, 45f, 500f, stack, 0f));
        }

        [Fact]
        public void CentrelineTrailDoesNotNeedAnArtificialLateralBankCap()
        {
            Assert.Equal(65f, FormationCollision.TerrainBank(65f, 150f, 45f, 0f, 15f, 600f));
        }

        [Theory]
        [MemberData(nameof(FormationLayoutTests.Shapes), MemberType = typeof(FormationLayoutTests))]
        public void EverySlotClearsTheFloorWithOneBankAcrossShapesTurnsAndLargeWings(int shape)
        {
            foreach (int count in new[] { 3, 16 })
            foreach (float turn in new[] { 0f, 0.5f, 1f })
            foreach (float pitch in new[] { 0f, 0.1f })
            foreach (float requested in new[] { -80f, 80f })
            {
                const float altitude = 350f, clearance = 45f;
                float lateralScale = 1f + (FormationLayout.TurnLateralScale - 1f) * turn;
                float backScale = 1f + (FormationLayout.TurnBackScale - 1f) * turn;
                float lateral = 0f, down = 0f, aft = 0f;
                for (int slot = 1; slot <= count; slot++)
                {
                    SlotLayout point = FormationLayout.Slot((FormationShape)shape, slot);
                    lateral = Math.Max(lateral, Math.Abs(point.Lateral * 120f * lateralScale));
                    down = Math.Max(down, -point.Height * 20f);
                    aft = Math.Max(aft, point.Back * 120f * backScale);
                }
                float bank = FormationCollision.TerrainBank(requested, altitude, clearance,
                    lateral, down, aft, pitch);
                double radians = bank * Math.PI / 180d;
                double horizontal = Math.Sqrt(1d - pitch * pitch);
                for (int slot = 1; slot <= count; slot++)
                {
                    SlotLayout point = FormationLayout.Slot((FormationShape)shape, slot);
                    double slotAltitude = altitude + horizontal *
                        (point.Lateral * 120f * lateralScale * Math.Sin(radians) +
                         point.Height * 20f * Math.Cos(radians)) -
                        point.Back * 120f * backScale * pitch;
                    Assert.True(slotAltitude >= clearance - 0.001d,
                        $"{(FormationShape)shape} slot {slot}/{count}, turn {turn}, pitch {pitch}, bank {bank}, altitude {slotAltitude}");
                }
            }
        }

        private static float LowestAltitude(float bank, float altitude, float arm, float stack)
        {
            double radians = bank * Math.PI / 180d;
            return altitude - (float)(arm * Math.Abs(Math.Sin(radians)) + stack * Math.Cos(radians));
        }
    }
}
