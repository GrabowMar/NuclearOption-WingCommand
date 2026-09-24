using Xunit;

namespace WingCommand.PureTests
{
    public class WingOrderTests
    {
        [Fact]
        public void AnOrderIsThePlayersUnlessAPlanOrARuleSentIt()
        {
            Assert.Equal(OrderSource.Player, WingOrder.Of(OrderKind.Rtb).Source);
            Assert.Equal(TransitionReason.Commanded, WingOrder.Of(OrderKind.Rtb).Reason);
            Assert.Equal(TransitionReason.Plan, new WingOrder { Source = OrderSource.Plan }.Reason);
            Assert.Equal(TransitionReason.Rule, new WingOrder { Source = OrderSource.Rule }.Reason);
        }
    }
}
