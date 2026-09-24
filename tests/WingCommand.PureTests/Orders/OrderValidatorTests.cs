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
            Assert.Equal("name too long", OrderValidator.Check(new WingOrder { Kind = OrderKind.RenameElement, Text = new string('x', 65) }));
            Assert.Equal("call 1 to 7 aircraft", OrderValidator.Check(new WingOrder { Kind = OrderKind.Call, Number = 0f }));
            Assert.Equal("stack out of range", OrderValidator.Check(new WingOrder { Kind = OrderKind.Stack, Number = float.NaN }));
            Assert.Equal("no such spacing", OrderValidator.Check(new WingOrder { Kind = OrderKind.SetSpacing, Number = 4f }));
            Assert.Null(OrderValidator.Check(WingOrder.Tasked(WingTask.Orbit(Waypoint.At(0f, 0f)))));
            Assert.Null(OrderValidator.Check(new WingOrder { Kind = OrderKind.Attack, Units = new uint[] { 7 } }));
        }

        [Fact]
        public void AnEjectionIsOneConfirmedWingmanOrderedByYou()
        {
            Assert.Equal("eject one wingman at a time", OrderValidator.Check(new WingOrder { Kind = OrderKind.Eject, Flag = true }));
            Assert.Equal("eject one wingman at a time", OrderValidator.Check(new WingOrder
                { Kind = OrderKind.Eject, Flag = true, Scope = WingScope.OfElement(1) }));
            Assert.Equal("eject one wingman at a time", OrderValidator.Check(new WingOrder
                { Kind = OrderKind.Eject, Flag = true, Scope = WingScope.OfMembers(11, 12) }));
            Assert.Equal("not confirmed", OrderValidator.Check(new WingOrder { Kind = OrderKind.Eject, Scope = WingScope.OfMembers(11) }));
            Assert.Equal("only you can order an ejection", OrderValidator.Check(new WingOrder
                { Kind = OrderKind.Eject, Flag = true, Scope = WingScope.OfMembers(11), Source = OrderSource.Rule }));
            Assert.Null(OrderValidator.Check(new WingOrder { Kind = OrderKind.Eject, Flag = true, Scope = WingScope.OfMembers(11) }));
        }

        [Fact]
        public void AnOverrideNamesAKnownSettingAndOneOfItsValues()
        {
            Assert.Equal("no such doctrine setting", OrderValidator.Check(new WingOrder { Kind = OrderKind.SetOverride, Number = 99f, Text = "Auto" }));
            Assert.Equal("no such doctrine setting", OrderValidator.Check(new WingOrder { Kind = OrderKind.SetOverride, Number = float.NaN, Text = "Auto" }));
            Assert.Equal("no such doctrine setting", OrderValidator.Check(new WingOrder { Kind = OrderKind.SetOverride, Number = 6.5f, Text = "Auto" }));
            Assert.Equal("no such value", OrderValidator.Check(new WingOrder
                { Kind = OrderKind.SetOverride, Number = (float)DoctrineAxis.Weapons, Text = "Lasers" }));
            Assert.Null(OrderValidator.Check(new WingOrder { Kind = OrderKind.SetOverride, Number = (float)DoctrineAxis.Radar, Text = "Silent" }));
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

        [Fact]
        public void TheLongestCustomDoctrineFitsAnOrder()
        {
            // Review P2 I6: a custom doctrine travels as its config text.
            var d = new WingDoctrine(MissileGuard.Lead, MissileResponse.Press, FormationInterval.Standard, false, TargetPolicy.Ground, EngagementReach.Long,
                WeaponsPolicy.NoAirToGround, RadarPolicy.Silent);
            Assert.Null(OrderValidator.Check(new WingOrder { Kind = OrderKind.SetDoctrine, Text = d.ToString() }));
        }
    }
}
