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

    }
}
