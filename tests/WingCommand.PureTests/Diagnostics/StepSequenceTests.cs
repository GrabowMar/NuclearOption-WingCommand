using Xunit;

namespace WingCommand.PureTests
{
    public class StepSequenceTests
    {
        [Fact]
        public void RollStepsRightThenLeftAtHalfStick()
        {
            Assert.Equal(0f, StepSequence.At(1f).Roll);
            Assert.Equal(0.5f, StepSequence.At(2.2f).Roll);
            Assert.Equal(0f, StepSequence.At(3f).Roll);
            Assert.Equal(-0.5f, StepSequence.At(5.2f).Roll);
        }

        [Fact]
        public void PitchStepsThenMilitaryPowerThenIdle()
        {
            Assert.Equal(0.3f, StepSequence.At(8.5f).Pitch);
            Assert.Equal(-0.3f, StepSequence.At(11.5f).Pitch);
            Assert.Equal(0.89f, StepSequence.At(15f).Throttle);
            Assert.Equal(0f, StepSequence.At(22f).Throttle);
            Assert.Equal(0.7f, StepSequence.At(27f).Throttle);
        }

        [Fact]
        public void SequenceEndsAfterThirtySeconds()
        {
            Assert.False(StepSequence.At(29.9f).Done);
            Assert.True(StepSequence.At(30f).Done);
        }
    }
}
