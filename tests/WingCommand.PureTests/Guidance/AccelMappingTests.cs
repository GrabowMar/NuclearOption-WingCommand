using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class AccelMappingTests
    {
        private static readonly Vec3 North200 = new Vec3(0f, 0f, 200f);

        private static AttitudeCommand Map(Vec3 accel, Vec3 velocity, float previousBank = 0f, float vCmdY = 0f) =>
            AccelMapping.Map(new GuidanceCommand { Accel = accel, VelCmd = new Vec3(0f, vCmdY, 0f) }, velocity, previousBank);

        [Fact]
        public void LevelUnacceleratedFlightNeedsOneGWingsLevel()
        {
            AttitudeCommand a = Map(Vec3.Zero, North200);
            Assert.Equal(0f, a.BankDeg, 3);
            Assert.Equal(1f, a.Nz, 4);
            Assert.Equal(0f, a.EnergyRate, 4);
        }

        [Fact]
        public void CoordinatedRightTurnAtSixtyDegreesNeedsTwoG()
        {
            float lateral = Scalar.G * (float)Math.Tan(60.0 * Math.PI / 180.0);
            AttitudeCommand a = Map(new Vec3(lateral, 0f, 0f), North200);
            Assert.Equal(60f, a.BankDeg, 2);
            Assert.Equal(2f, a.Nz, 3);
        }

        [Fact]
        public void LeftTurnGivesNegativeBank()
        {
            AttitudeCommand a = Map(new Vec3(-Scalar.G, 0f, 0f), North200);
            Assert.Equal(-45f, a.BankDeg, 2);
        }

        [Fact]
        public void PullUpAddsLoadFactorWithoutBank()
        {
            AttitudeCommand a = Map(new Vec3(0f, Scalar.G, 0f), North200);
            Assert.Equal(0f, a.BankDeg, 3);
            Assert.Equal(2f, a.Nz, 3);
        }

        [Fact]
        public void NearZeroLiftHoldsThePreviousBank()
        {
            AttitudeCommand a = Map(new Vec3(0f, -Scalar.G, 0f), North200, previousBank: 25f);
            Assert.Equal(25f, a.BankDeg);
            Assert.Equal(0f, a.Nz, 3);
        }

        [Fact]
        public void VerticalVelocityHoldsPreviousBank()
        {
            AttitudeCommand a = Map(new Vec3(5f, 0f, 0f), new Vec3(0f, 150f, 0f), previousBank: -12f);
            Assert.Equal(-12f, a.BankDeg);
            Assert.True(Scalar.IsFinite(a.Nz));
        }

        [Fact]
        public void EnergyRateCombinesTangentialAccelerationAndCommandedClimb()
        {
            AttitudeCommand a = Map(new Vec3(0f, 0f, 4.905f), North200, vCmdY: 8f);
            Assert.Equal(200f * 4.905f / Scalar.G + 8f, a.EnergyRate, 3);
        }
    }
}
