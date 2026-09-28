using Xunit;

namespace WingCommand.PureTests
{
    public class EngineSticksTests
    {
        [Fact]
        public void PitchUpBecomesANegativeGamePitch()
        {
            StickInputs s = EngineSticks.FromPure(new ControlOutput { Pitch = 0.4f, Roll = 0.3f, Yaw = -0.2f, Throttle = 0.7f });
            Assert.Equal(-0.4f, s.Pitch);
            Assert.Equal(0.3f, s.Roll);
            Assert.Equal(-0.2f, s.Yaw);
            Assert.Equal(0.7f, s.Throttle);
            Assert.Equal(0f, s.Brake);
        }

        [Fact]
        public void AirbrakeMeansZeroThrottle() =>
            Assert.Equal(0f, EngineSticks.FromPure(new ControlOutput { Throttle = 0.3f, Airbrake = true }).Throttle);

        [Fact]
        public void OutputsAreClampedToTheGameRange()
        {
            StickInputs s = EngineSticks.FromPure(new ControlOutput { Pitch = -2f, Roll = 3f, Throttle = 1.5f });
            Assert.Equal(1f, s.Pitch);
            Assert.Equal(1f, s.Roll);
            Assert.Equal(1f, s.Throttle);
        }

        [Fact]
        public void ReadingTheGameSticksInvertsTheMapping()
        {
            ControlOutput o = EngineSticks.ToPure(new StickInputs { Pitch = -0.5f, Roll = 0.25f, Yaw = 0.1f, Throttle = 0.6f });
            Assert.Equal(0.5f, o.Pitch);
            Assert.Equal(0.25f, o.Roll);
            Assert.Equal(0.1f, o.Yaw);
            Assert.Equal(0.6f, o.Throttle);
            Assert.False(o.Airbrake);
            Assert.True(EngineSticks.ToPure(new StickInputs { Throttle = 0f }).Airbrake);
        }
    }
}
