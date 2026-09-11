using Xunit;

namespace WingCommand.PureTests
{
    public sealed class StandingOrderTests
    {
        [Fact]
        public void OldTaskCompletionCannotEraseANewOrderOrItsTargetPayload()
        {
            var standing = NewOrder();
            standing.Set((WingOrder.MoveToPoint, "waypoint A"));
            int oldLeg = standing.Revision;
            standing.Set((WingOrder.Attack, "target B"));
            Assert.False(standing.TryComplete(oldLeg, (WingOrder.Formation, ""), out bool changed));
            Assert.False(changed);
            Assert.Equal((WingOrder.Attack, "target B"), standing.Current);
        }

        [Fact]
        public void AChangedPayloadStartsANewRevisionButAnIdenticalOrderDoesNot()
        {
            var standing = NewOrder();
            standing.Set((WingOrder.Maneuver, "loop"));
            int started = standing.Revision;
            Assert.False(standing.Set((WingOrder.Maneuver, "loop")));
            Assert.Equal(started, standing.Revision);
            standing.Set((WingOrder.Maneuver, "roll"));
            Assert.False(standing.TryComplete(started, (WingOrder.Formation, ""), out _));
            Assert.Equal((WingOrder.Maneuver, "roll"), standing.Current);
            Assert.True(standing.TryComplete(standing.Revision, (WingOrder.Formation, ""), out _));
            Assert.Equal(WingOrder.Formation, standing.Current.order);
        }

        [Fact]
        public void CompletedLegAdvancesOnceAndCannotCompleteTheFollowingLeg()
        {
            var standing = NewOrder();
            standing.Set((WingOrder.MoveToPoint, "A"));
            int a = standing.Revision;
            Assert.True(standing.TryComplete(a, (WingOrder.MoveToPoint, "B"), out _));
            Assert.False(standing.TryComplete(a, (WingOrder.Formation, ""), out _));
            Assert.Equal((WingOrder.MoveToPoint, "B"), standing.Current);
        }

        private static StandingOrder<(WingOrder order, string payload)> NewOrder() =>
            new StandingOrder<(WingOrder, string)>((WingOrder.Formation, ""), (a, b) => a == b);
    }
}
