using Xunit;

namespace WingCommand.PureTests
{
    public class DepartureChatterTests
    {
        [Fact]
        public void EachActualPhaseReportsOnlyOnceDespiteRepeatedObservationOrStateRollback()
        {
            var radio = new DepartureChatter();
            radio.Observe(10, DeparturePhase.Taxiing, 0f);
            AssertCall(radio, 0f, 10, DeparturePhase.Taxiing);
            radio.Observe(10, DeparturePhase.Taxiing, 10f);
            Assert.False(radio.TryDequeue(10f, true, out _, out _));
            radio.Observe(10, DeparturePhase.Departing, 12f);
            AssertCall(radio, 12f, 10, DeparturePhase.Departing);
            radio.Observe(10, DeparturePhase.Taxiing, 20f);
            Assert.False(radio.TryDequeue(20f, true, out _, out _));
            radio.Observe(10, DeparturePhase.Airborne, 25f);
            AssertCall(radio, 25f, 10, DeparturePhase.Airborne);
            radio.Observe(10, DeparturePhase.Airborne, 30f);
            Assert.False(radio.TryDequeue(30f, true, out _, out _));
        }

        [Fact]
        public void LiftoffReplacesUnsaidGroundChatterWhileWarningsOwnTheChannel()
        {
            var radio = new DepartureChatter();
            radio.Observe(2, DeparturePhase.Taxiing, 0f);
            Assert.False(radio.TryDequeue(0f, false, out _, out _));
            radio.Observe(2, DeparturePhase.Departing, 1f);
            radio.Observe(2, DeparturePhase.Airborne, 2f);
            Assert.True(radio.HasPending(2, DeparturePhase.Airborne));
            Assert.False(radio.TryDequeue(3f, false, out _, out _));
            AssertCall(radio, 4f, 2, DeparturePhase.Airborne);
            Assert.False(radio.HasPending(2, DeparturePhase.Airborne));
            Assert.False(radio.TryDequeue(10f, true, out _, out _));
        }

        [Fact]
        public void ThreeAircraftShareOneSpacedChannelInsteadOfCallingTogether()
        {
            var radio = new DepartureChatter();
            radio.Observe(3, DeparturePhase.Taxiing, 0f);
            radio.Observe(1, DeparturePhase.Taxiing, 0f);
            radio.Observe(2, DeparturePhase.Taxiing, 0f);
            AssertCall(radio, 0f, 1, DeparturePhase.Taxiing);
            Assert.False(radio.TryDequeue(0f, true, out _, out _));
            Assert.False(radio.TryDequeue(4.99f, true, out _, out _));
            AssertCall(radio, 5f, 2, DeparturePhase.Taxiing);
            Assert.False(radio.TryDequeue(9.99f, true, out _, out _));
            AssertCall(radio, 10f, 3, DeparturePhase.Taxiing);
        }

        [Fact]
        public void RadioSilenceDoesNotReplayOldPhasesWhenEnabledAgain()
        {
            var radio = new DepartureChatter();
            radio.Observe(7, DeparturePhase.Taxiing, 0f);
            radio.Silence();
            radio.Observe(7, DeparturePhase.Taxiing, 1f);
            Assert.False(radio.TryDequeue(1f, true, out _, out _));
            radio.Observe(7, DeparturePhase.Airborne, 2f);
            AssertCall(radio, 2f, 7, DeparturePhase.Airborne);
        }

        [Fact]
        public void ObsoleteReportsExpireWithoutRepeating()
        {
            var radio = new DepartureChatter();
            radio.Observe(4, DeparturePhase.Taxiing, 0f);
            Assert.False(radio.TryDequeue(DepartureChatter.ReportLifetimeSeconds + 1f,
                true, out _, out _));
            radio.Observe(4, DeparturePhase.Taxiing, 25f);
            Assert.False(radio.TryDequeue(25f, true, out _, out _));
        }

        [Fact]
        public void RemovedAircraftCannotReportAndReplacementGetsItsOwnLifecycle()
        {
            var radio = new DepartureChatter();
            radio.Observe(4, DeparturePhase.Taxiing, 0f);
            radio.Forget(4);
            Assert.False(radio.TryDequeue(0f, true, out _, out _));
            radio.Observe(4, DeparturePhase.Taxiing, 1f);
            AssertCall(radio, 1f, 4, DeparturePhase.Taxiing);
        }

        [Fact]
        public void MissionResetClearsBothPhasesAndRadioSpacing()
        {
            var radio = new DepartureChatter();
            radio.Observe(4, DeparturePhase.Airborne, 100f);
            AssertCall(radio, 100f, 4, DeparturePhase.Airborne);
            radio.Reset();
            radio.Observe(4, DeparturePhase.Taxiing, 0f);
            AssertCall(radio, 0f, 4, DeparturePhase.Taxiing);
        }

        [Theory]
        [InlineData("Taxiing")]
        [InlineData("Departing")]
        [InlineData("Airborne")]
        [InlineData("AirborneRejoining")]
        public void DepartureEventsHaveConciseLinesAcrossPilotPersonas(string eventName)
        {
            foreach (ChatterPersona persona in System.Enum.GetValues(typeof(ChatterPersona)))
            {
                string line = ChatterDialogue.Event(persona, eventName, null, 0);
                Assert.NotEqual("Copy.", line);
                Assert.InRange(line.Length, 8, 75);
                Assert.DoesNotContain("clearance", line.ToLowerInvariant());
            }
        }

        private static void AssertCall(DepartureChatter radio, float now, int id, DeparturePhase phase)
        {
            Assert.True(radio.TryDequeue(now, true, out int actualId, out DeparturePhase actualPhase));
            Assert.Equal(id, actualId);
            Assert.Equal(phase, actualPhase);
        }
    }
}
