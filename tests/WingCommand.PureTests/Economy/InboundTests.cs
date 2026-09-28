using Xunit;

namespace WingCommand.PureTests
{
    public class InboundTests
    {
        [Fact]
        public void ALaunchWhoseAircraftHasNotAppearedIsQueued() =>
            Assert.Equal(InboundPhase.Queued, InboundRules.Phase(appeared: false, adopted: false, onGround: false, GroundPhase.Parked));

        [Fact]
        public void AnAircraftThatExistsButIsNotYetAMemberOrStillParkedIsSpawning()
        {
            Assert.Equal(InboundPhase.Spawning, InboundRules.Phase(true, false, false, GroundPhase.Parked));
            Assert.Equal(InboundPhase.Spawning, InboundRules.Phase(true, true, true, GroundPhase.Parked));
        }

        [Theory]
        [InlineData((int)GroundPhase.TaxiOut, (int)InboundPhase.Taxiing)]
        [InlineData((int)GroundPhase.HoldShort, (int)InboundPhase.Taxiing)]
        [InlineData((int)GroundPhase.LineUp, (int)InboundPhase.Departing)]
        [InlineData((int)GroundPhase.Roll, (int)InboundPhase.Departing)]
        [InlineData((int)GroundPhase.ClimbOut, (int)InboundPhase.Departing)]
        [InlineData((int)GroundPhase.LiftOff, (int)InboundPhase.Departing)]
        public void AMemberOnTheGroundTaxiesThenDeparts(int ground, int expected) =>
            Assert.Equal((InboundPhase)expected, InboundRules.Phase(true, true, true, (GroundPhase)ground));

        [Fact]
        public void AMemberAirborneFromTheFieldIsJoining() =>
            Assert.Equal(InboundPhase.Joining, InboundRules.Phase(true, true, false, GroundPhase.Done));

        [Fact]
        public void AMemberThatReachedItsSlotEngagedTurnedHomeAbortedOrWasReleasedIsNoLongerInbound()
        {
            Assert.True(InboundRules.StillInbound(true, false, false, false, onGround: false, GroundPhase.Done, BehaviourId.Rejoin));
            Assert.False(InboundRules.StillInbound(true, false, false, false, false, GroundPhase.Done, BehaviourId.StationKeep));
            Assert.False(InboundRules.StillInbound(true, false, false, engaged: true, false, GroundPhase.Done, BehaviourId.Rejoin));
            Assert.False(InboundRules.StillInbound(true, false, recovering: true, false, false, GroundPhase.Done, BehaviourId.Rejoin));
            Assert.False(InboundRules.StillInbound(true, false, false, false, true, GroundPhase.Aborted, BehaviourId.Rejoin));
            Assert.False(InboundRules.StillInbound(true, released: true, false, false, true, GroundPhase.TaxiOut, BehaviourId.Rejoin));
            Assert.False(InboundRules.StillInbound(alive: false, false, false, false, true, GroundPhase.TaxiOut, BehaviourId.Rejoin));
            Assert.True(InboundRules.StillInbound(true, false, false, false, true, GroundPhase.TaxiOut, BehaviourId.Rejoin));
        }

        [Fact]
        public void OnlyAJoiningMemberWithAFiniteInterceptUnderNinetyNineMinutesHasAnEta()
        {
            Assert.Equal(80f, InboundRules.Eta(InboundPhase.Joining, 80f));
            Assert.True(float.IsNaN(InboundRules.Eta(InboundPhase.Taxiing, 80f)));
            Assert.True(float.IsNaN(InboundRules.Eta(InboundPhase.Joining, float.PositiveInfinity)));
            Assert.True(float.IsNaN(InboundRules.Eta(InboundPhase.Joining, 100f * 60f)));
        }

        [Fact]
        public void TheRowReadsTypePilotPhaseAndEta()
        {
            Assert.Equal("VT-7 · HATCH · JOINING · ETA 01:20", InboundWords.Row("VT-7", "HATCH", InboundPhase.Joining, 80f));
            Assert.Equal("FS-20 · QUEUED", InboundWords.Row("FS-20", null, InboundPhase.Queued, float.NaN));
            Assert.Equal("+2 MORE", InboundWords.More(2));
        }

        [Fact]
        public void PhaseWordsAreSpelledOutNeverShortened()
        {
            foreach (InboundPhase p in System.Enum.GetValues(typeof(InboundPhase)))
                Assert.True(InboundWords.Phase(p).Length >= 6, p.ToString());
            Assert.Equal("DEPARTING", InboundWords.Phase(InboundPhase.Departing));
        }

        [Fact]
        public void TheLongestRowFitsTheContentWidth() =>
            Assert.True(InboundWords.Row("ABCDEFGH", "ABCDEFGHIJKLMN", InboundPhase.Departing, 5999f).Length <= 60);
    }
}
