using Xunit;

namespace WingCommand.PureTests
{
    public class OrderPolicyTests
    {
        [Fact]
        public void Splash_RunUsesLeashAndYieldsWeaponsAuthorityToSafetyBehaviours()
        {
            Assert.True(WingOrderRules.SendsWingmanHunting(WingOrder.FireForEffect));
            Assert.False(WingOrderRules.SendsWingmanHunting(WingOrder.JamTarget));
            Assert.Equal(OrderEngagementAuthority.ExplicitTarget,
                OrderRoePolicy.Authority(WingOrder.FireForEffect));
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                OrderRoePolicy.AuthorityFor(WingBehaviours.Rejoin, WingOrder.FireForEffect));
            foreach (WingOrder order in System.Enum.GetValues(typeof(WingOrder)))
                Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                    OrderRoePolicy.AuthorityFor(WingBehaviours.TerrainAbort, order));
        }

        [Fact]
        public void Maneuver_CannotBeAcknowledgedWhileDefendingOrAwaitingDelivery()
        {
            var member = new WingMember();
            Assert.True(WingOrderCatalog.CanApply(member, WingOrder.Maneuver));
            member.IsPanicking = true;
            Assert.False(WingOrderCatalog.CanApply(member, WingOrder.Maneuver));
            Assert.True(WingOrderCatalog.CanApply(member, WingOrder.Formation));
            member.IsPanicking = false;
            member.DeliveryPending = true;
            Assert.False(WingOrderCatalog.CanApply(member, WingOrder.Maneuver));
            Assert.True(WingOrderCatalog.CanApply(member, WingOrder.Formation));
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        [InlineData(WingOrder.JamTarget)]
        public void CompletedDesignationsRetireIndependentlyOfWhichReflexOwnsFlight(WingOrder order)
        {
            Assert.True(WingOrderRules.TargetTaskComplete(order, targetAlive: false, deliveryPending: false));
            Assert.False(WingOrderRules.TargetTaskComplete(order, targetAlive: true, deliveryPending: false));
            Assert.False(WingOrderRules.TargetTaskComplete(order, targetAlive: false, deliveryPending: true));
        }

        [Fact]
        public void ARouteOrOpenEndedOrderDoesNotNeedADesignatedTargetToSurvive()
        {
            foreach (WingOrder order in System.Enum.GetValues(typeof(WingOrder)))
            {
                if (WingOrderRules.CarriesTarget(order)) continue;
                Assert.False(WingOrderRules.TargetTaskComplete(order, targetAlive: false, deliveryPending: false));
            }
        }
    }
}
