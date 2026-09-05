using Xunit;

namespace WingCommand.Tests
{
    public class FormationSafetyTests
    {
        [Theory]
        [InlineData(149f, 87.5f, false, true)]
        [InlineData(150f, 87.4f, false, true)]
        [InlineData(150f, 87.5f, false, false)]
        [InlineData(80f, 30f, true, false)]
        public void CloseLeaderCannotBypassFixedWingReleaseGates(
            float altitude, float speed, bool rotary, bool expected)
        {
            Assert.Equal(expected, ClimbOutPolicy.ShouldClimbOut(
                altitude, 100f, WingOrder.Formation, true, false, true,
                speed, 70f, rotary));
        }

        [Theory]
        [InlineData(450f, 75f, true)]
        [InlineData(600f, 75f, true)]
        [InlineData(79f, 75f, false)]
        [InlineData(80f, 75f, true)]
        [InlineData(450f, 87.5f, false)]
        public void SpeedRecoveryLevelsOnlyAfterTerrainClearance(float altitude, float speed, bool expected)
        {
            Assert.Equal(expected, ClimbOutPolicy.ShouldAccelerateLevel(altitude, speed, 70f));
        }

        [Theory]
        [InlineData(20f, 1000f)]
        [InlineData(50f, 1000f)]
        [InlineData(100f, 950f)]
        public void DescendingAimRetainsTerrainClearance(float radarAlt, float expected)
        {
            Assert.Equal(expected, FormationSafety.AimAltitude(1000f, 500f, radarAlt));
        }

        [Fact]
        public void ClimbingAimIsPreserved()
        {
            Assert.Equal(1200f, FormationSafety.AimAltitude(1000f, 1200f, 20f));
        }

        [Theory]
        [InlineData(300f, -20f, false)]
        [InlineData(1000f, -3f, false)]
        [InlineData(100f, 0f, false)]
        [InlineData(300f, 0f, true)]
        public void BankTrimYieldsToRecovery(float altitude, float verticalSpeed, bool allowed)
        {
            Assert.Equal(allowed, FormationSafety.AllowsBankMatch(altitude, verticalSpeed));
        }
    }
}
