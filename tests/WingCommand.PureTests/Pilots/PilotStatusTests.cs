using Xunit;

namespace WingCommand.PureTests
{
    public class PilotStatusTests
    {
        [Fact]
        public void ALostPilotIsKiaWhateverElseIsTrue() =>
            Assert.Equal(PilotStatus.Kia, PilotStatuses.Of(true, PilotRecoveryStatus.Captured, true, true, true, true));

        [Fact]
        public void CaptureOutranksALocalSearchAndALocalSearchOutranksDowned()
        {
            Assert.Equal(PilotStatus.Captured, PilotStatuses.Of(false, PilotRecoveryStatus.Captured, false, false, true, false));
            Assert.Equal(PilotStatus.LocalSar, PilotStatuses.Of(false, PilotRecoveryStatus.Downed, false, false, true, true));
            Assert.Equal(PilotStatus.LocalSar, PilotStatuses.Of(false, PilotRecoveryStatus.Missing, false, false, true, false));
        }

        [Fact]
        public void ADownedPilotWithAHelicopterOnTheWayReadsRescue()
        {
            Assert.Equal(PilotStatus.Rescue, PilotStatuses.Of(false, PilotRecoveryStatus.Downed, false, false, false, true));
            Assert.Equal(PilotStatus.Downed, PilotStatuses.Of(false, PilotRecoveryStatus.Downed, false, false, false, false));
            Assert.Equal(PilotStatus.Missing, PilotStatuses.Of(false, PilotRecoveryStatus.Missing, false, false, false, false));
        }

        [Fact]
        public void ASeatedPilotIsFlyingAHeldOneInboundAndTheRestFree()
        {
            Assert.Equal(PilotStatus.Flying, PilotStatuses.Of(false, PilotRecoveryStatus.None, true, true, false, false));
            Assert.Equal(PilotStatus.Inbound, PilotStatuses.Of(false, PilotRecoveryStatus.None, false, true, false, false));
            Assert.Equal(PilotStatus.Free, PilotStatuses.Of(false, PilotRecoveryStatus.None, false, false, false, false));
        }

        [Fact]
        public void EveryPilotOutOfActionCountsAsLost()
        {
            foreach (PilotStatus s in new[] { PilotStatus.Downed, PilotStatus.Rescue, PilotStatus.Missing, PilotStatus.LocalSar, PilotStatus.Captured, PilotStatus.Kia })
                Assert.True(PilotStatuses.Lost(s), s.ToString());
            foreach (PilotStatus s in new[] { PilotStatus.Free, PilotStatus.Inbound, PilotStatus.Flying })
                Assert.False(PilotStatuses.Lost(s), s.ToString());
        }
    }
}
