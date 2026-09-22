using Xunit;

namespace WingCommand.PureTests
{
    public class HoldGuidanceTests
    {
        private static readonly AirframeProfile Fighter = new AirframeProfile();

        private static AircraftState North(float altitude = 2000f, float speed = 200f, float vy = 0f) => new AircraftState
        {
            Pos = new Vec3(0f, altitude, 0f), Vel = new Vec3(0f, vy, speed), Tas = speed, Fwd = Vec3.Forward,
        };

        [Fact]
        public void HeadingToTheRightCommandsRightwardAccelerationWithinTheBankCap()
        {
            var cmd = HoldGuidance.Evaluate(new HoldSpec { Lateral = LateralHold.Heading, HeadingDeg = 90f }, North(), Fighter);
            Assert.True(cmd.Accel.X > 0f);
            AttitudeCommand a = AccelMapping.Map(cmd, North().Vel, 0f);
            Assert.InRange(a.BankDeg, 1f, 30.5f);
        }

        [Fact]
        public void AltitudeCaptureIsLimitedByTheBrakingLaw()
        {
            var cmd = HoldGuidance.Evaluate(new HoldSpec { Vertical = VerticalHold.Altitude, AltitudeM = 2010f }, North(), Fighter);
            // 0.2·10 = 2 m/s is below the √(2·g·0.3·10) = 7.7 m/s capture limit.
            Assert.Equal(2f, cmd.VelCmd.Y, 3);
        }

        [Fact]
        public void LevelHoldCommandsNoLateralOrVerticalAcceleration()
        {
            var cmd = HoldGuidance.Evaluate(new HoldSpec { Lateral = LateralHold.Level }, North(), Fighter);
            Assert.Equal(0f, cmd.Accel.X, 4);
            Assert.Equal(0f, cmd.Accel.Y, 4);
        }

        [Fact]
        public void SpeedHoldAcceleratesTowardTheTarget()
        {
            var cmd = HoldGuidance.Evaluate(new HoldSpec { Speed = true, SpeedMps = 250f }, North(), Fighter);
            Assert.True(cmd.Accel.Z > 0f);
            Assert.False(cmd.AfterburnerAllowed);
        }
    }
}
