using Xunit;

namespace WingCommand.PureTests
{
    public class FlightStackTests
    {
        [Fact]
        public void FlightStackPicksThePipelineByClass()
        {
            Assert.IsType<FixedWingPipeline>(FlightStack.NewPipeline(AirframeClass.FixedWing));
        }

        [Fact]
        public void FormationPilotBuildsItsPipelineFromItsClass()
        {
            Assert.IsType<FixedWingPipeline>(new FormationPilot(0, AirframeClass.FixedWing).Pipeline);
        }

        [Fact]
        public void RotaryProfileHasNoMinimumSpeed()
        {
            var p = new AirframeProfile { Class = AirframeClass.Rotary, StallSpeed = 30f };
            Assert.Equal(0f, p.MinimumSpeed(1f));
            Assert.Equal(0f, p.MinimumSpeed(2f));
        }
    }
}
