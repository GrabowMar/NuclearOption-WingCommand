using Xunit;

namespace WingCommand.PureTests
{
    public sealed class StationFirePolicyTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void InvalidTargetNeverArmsOrFires(bool hasTurret, bool stationReady)
        {
            Assert.Equal(StationFireAction.None,
                StationFirePolicy.Decide(hasLiveTarget: false, hasTurret: hasTurret,
                    stationReady: stationReady));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TurretDelegatesReleaseToNativeLockingEvenWhileCoolingDown(bool stationReady)
        {
            Assert.Equal(StationFireAction.ArmNativeTurret,
                StationFirePolicy.Decide(hasLiveTarget: true, hasTurret: true,
                    stationReady: stationReady));
        }

        [Fact]
        public void ReadyFixedStationFiresDirectly()
        {
            Assert.Equal(StationFireAction.DirectFire,
                StationFirePolicy.Decide(hasLiveTarget: true, hasTurret: false, stationReady: true));
        }

        [Fact]
        public void UnreadyFixedStationDoesNotFire()
        {
            Assert.Equal(StationFireAction.None,
                StationFirePolicy.Decide(hasLiveTarget: true, hasTurret: false, stationReady: false));
        }
    }
}
