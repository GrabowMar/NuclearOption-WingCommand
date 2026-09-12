using Xunit;

namespace WingCommand.PureTests
{
    public class ChatterLifetimeTests
    {
        [Fact]
        public void ReplacedOrdersInvalidateQueuedAndVisibleSpeechEvenWhenReturningToTheSameOrder()
        {
            var order = new StandingOrder<int>(1, (a, b) => a == b);
            int revision = order.Revision;
            var line = new ChatterLifetime(0f, false, () => order.Revision == revision);
            Assert.True(line.CanStart(1f));
            order.Set(2);
            Assert.False(line.CanStart(2f));
            Assert.False(line.IsRelevant);
            order.Set(1);
            Assert.False(line.IsRelevant);
        }

        [Fact]
        public void BackloggedRoutineSpeechExpiresWithoutCuttingOffARelevantVisibleLine()
        {
            var routine = new ChatterLifetime(10f, false, () => true);
            var urgent = new ChatterLifetime(10f, true, null);
            Assert.True(routine.CanStart(15.9f));
            Assert.False(routine.CanStart(16f));
            Assert.True(routine.IsRelevant);
            Assert.True(urgent.CanStart(16f));
            Assert.False(urgent.CanStart(25f));
        }
    }
}
