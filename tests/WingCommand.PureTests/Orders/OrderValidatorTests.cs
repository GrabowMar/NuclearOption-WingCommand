using Xunit;

namespace WingCommand.PureTests
{
    public class OrderValidatorTests
    {
        [Fact]
        public void AWingOrderWithNoArgumentsIsValid() =>
            Assert.Null(OrderValidator.Check(WingOrder.Of(OrderKind.FormUp)));

        [Fact]
        public void ScopesAreBounded()
        {
            Assert.Equal("no such element", OrderValidator.Check(WingOrder.Of(OrderKind.Rtb, WingScope.OfElement(4))));
            Assert.Equal("nobody selected", OrderValidator.Check(WingOrder.Of(OrderKind.Rtb, WingScope.OfMembers())));
            Assert.Equal("too many selected", OrderValidator.Check(WingOrder.Of(OrderKind.Rtb,
                WingScope.OfMembers(1, 2, 3, 4, 5, 6, 7, 8, 9))));
            Assert.Null(OrderValidator.Check(WingOrder.Of(OrderKind.Rtb, WingScope.OfMembers(11, 12))));
        }

        [Fact]
        public void KindsCheckTheirArguments()
        {
            Assert.Equal("no task", OrderValidator.Check(WingOrder.Of(OrderKind.Task)));
            Assert.Equal("no target", OrderValidator.Check(WingOrder.Of(OrderKind.Attack)));
            Assert.Equal("no target", OrderValidator.Check(WingOrder.Of(OrderKind.EscortTarget)));
            Assert.Equal("nothing selected to recruit", OrderValidator.Check(WingOrder.Of(OrderKind.Recruit)));
            Assert.Equal("no name", OrderValidator.Check(WingOrder.Of(OrderKind.SetShape)));
            Assert.Equal("name too long", OrderValidator.Check(new WingOrder { Kind = OrderKind.RenameElement, Text = new string('x', 33) }));
            Assert.Equal("call 1 to 7 aircraft", OrderValidator.Check(new WingOrder { Kind = OrderKind.Call, Number = 0f }));
            Assert.Equal("stack out of range", OrderValidator.Check(new WingOrder { Kind = OrderKind.Stack, Number = float.NaN }));
            Assert.Equal("no such spacing", OrderValidator.Check(new WingOrder { Kind = OrderKind.SetSpacing, Number = 4f }));
            Assert.Null(OrderValidator.Check(WingOrder.Tasked(WingTask.Orbit(Waypoint.At(0f, 0f)))));
            Assert.Null(OrderValidator.Check(new WingOrder { Kind = OrderKind.Attack, Units = new uint[] { 7 } }));
        }

        [Fact]
        public void AnAcceptedResultCarriesItsAck()
        {
            OrderResult r = OrderResult.Acked("2 attacking", 1);
            Assert.True(r.Accepted);
            Assert.Equal("2 attacking", r.Ack);
            Assert.Equal(1, r.Element);
            Assert.Equal(-1, OrderResult.Ok.Element);
        }
    }
}
