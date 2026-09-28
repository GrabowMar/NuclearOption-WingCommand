using Xunit;

namespace WingCommand.PureTests
{
    public sealed class StandingOrderTests
    {
        [Fact]
        public void OldTaskCompletionCannotEraseANewOrderOrItsTargetPayload()
        {
            var standing = NewOrder();
            standing.Set((TestOrder.MoveToPoint, "waypoint A"));
            int oldLeg = standing.Revision;
            standing.Set((TestOrder.Attack, "target B"));
            Assert.False(standing.TryComplete(oldLeg, (TestOrder.Formation, ""), out bool changed));
            Assert.False(changed);
            Assert.Equal((TestOrder.Attack, "target B"), standing.Current);
        }

        [Fact]
        public void AChangedPayloadStartsANewRevisionButAnIdenticalOrderDoesNot()
        {
            var standing = NewOrder();
            standing.Set((TestOrder.Maneuver, "loop"));
            int started = standing.Revision;
            Assert.False(standing.Set((TestOrder.Maneuver, "loop")));
            Assert.Equal(started, standing.Revision);
            standing.Set((TestOrder.Maneuver, "roll"));
            Assert.False(standing.TryComplete(started, (TestOrder.Formation, ""), out _));
            Assert.Equal((TestOrder.Maneuver, "roll"), standing.Current);
            Assert.True(standing.TryComplete(standing.Revision, (TestOrder.Formation, ""), out _));
            Assert.Equal(TestOrder.Formation, standing.Current.order);
        }

        [Fact]
        public void CompletedLegAdvancesOnceAndCannotCompleteTheFollowingLeg()
        {
            var standing = NewOrder();
            standing.Set((TestOrder.MoveToPoint, "A"));
            int a = standing.Revision;
            Assert.True(standing.TryComplete(a, (TestOrder.MoveToPoint, "B"), out _));
            Assert.False(standing.TryComplete(a, (TestOrder.Formation, ""), out _));
            Assert.Equal((TestOrder.MoveToPoint, "B"), standing.Current);
        }

        private static StandingOrder<(TestOrder order, string payload)> NewOrder() =>
            new StandingOrder<(TestOrder, string)>((TestOrder.Formation, ""), (a, b) => a == b);

        private enum TestOrder { Formation, MoveToPoint, Attack, Maneuver }
    }
}
