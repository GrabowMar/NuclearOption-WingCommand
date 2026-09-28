using Xunit;

namespace WingCommand.PureTests
{
    public class FaultGuardTests
    {
        [Fact]
        public void ThirdFaultWithinTenSecondsTrips()
        {
            var guard = new FaultGuard();
            Assert.False(guard.Record(1f));
            Assert.False(guard.Record(4f));
            Assert.True(guard.Record(10.5f));
        }

        [Fact]
        public void FaultsSpreadOverMoreThanTenSecondsDoNotTrip()
        {
            var guard = new FaultGuard();
            guard.Record(0f);
            guard.Record(6f);
            Assert.False(guard.Record(10.5f));
            Assert.True(guard.Record(12f));
        }
    }
}
