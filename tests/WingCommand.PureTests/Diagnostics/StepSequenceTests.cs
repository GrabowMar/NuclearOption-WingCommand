using Xunit;

namespace WingCommand.PureTests
{
    public class StepSequenceTests
    {
        [Fact]
        public void BrainFliesBetweenShortOpenLoopPulses()
        {
            // In game the 30 s open-loop test left the aircraft banked 60–86° with the stick centred; it dived from
            // 1610 m to 100 m and crashed. Now only short pulses are open loop; the brain recovers between them.
            Assert.Equal(StepPhase.Brain, StepSequence.At(1f).Phase);
            StepCommand right = StepSequence.At(StepSequence.RollRightAt + 0.2f);
            Assert.Equal(StepPhase.Open, right.Phase);
            Assert.Equal(StepSequence.RollStick, right.Roll);
            Assert.Equal(0f, right.Pitch);
            Assert.Equal(StepPhase.Brain, StepSequence.At(StepSequence.RollRightAt + 1f).Phase);
            Assert.Equal(-StepSequence.RollStick, StepSequence.At(StepSequence.RollLeftAt + 0.2f).Roll);
            Assert.Equal(StepSequence.PitchStick, StepSequence.At(StepSequence.PitchUpAt + 0.5f).Pitch);
            Assert.Equal(-StepSequence.PitchStick, StepSequence.At(StepSequence.PitchDownAt + 0.5f).Pitch);
            Assert.Equal(StepPhase.Brain, StepSequence.At(StepSequence.PitchDownAt + 2f).Phase);
        }

        [Fact]
        public void ThrottlePhasesOverrideOnlyTheThrottle()
        {
            StepCommand military = StepSequence.At(StepSequence.ThrustFrom + 1f);
            Assert.Equal(StepPhase.Throttle, military.Phase);
            Assert.Equal(StepSequence.MilitaryThrottle, military.Throttle);
            StepCommand idle = StepSequence.At(StepSequence.BrakeFrom + 1f);
            Assert.Equal(StepPhase.Throttle, idle.Phase);
            Assert.Equal(0f, idle.Throttle);
            Assert.Equal(StepPhase.Brain, StepSequence.At(StepSequence.BrakeTo + 0.5f).Phase);
        }

        [Fact]
        public void SequenceEndsAfterItsDuration()
        {
            Assert.False(StepSequence.At(StepSequence.Duration - 0.1f).Done);
            Assert.True(StepSequence.At(StepSequence.Duration).Done);
        }

        private static AircraftState At(float radarAlt, float vy) =>
            new AircraftState { RadarAlt = radarAlt, Vel = new Vec3(0f, vy, 150f) };

        [Fact]
        public void AbortsWhenLowOrSinkingFast()
        {
            Assert.True(StepSequence.Unsafe(At(700f, 0f), 1500f));
            Assert.True(StepSequence.Unsafe(At(1600f, -45f), 1500f));
            Assert.False(StepSequence.Unsafe(At(1600f, -10f), 1500f));
        }
    }
}
