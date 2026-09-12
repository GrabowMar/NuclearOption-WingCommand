using Xunit;

namespace WingCommand.PureTests
{
    public class OrderPolicyTests
    {
        [Fact]
        public void SplashRetainsOtherTargetsAfterDefenceAndRepeatedOrdersStartFreshSalvos()
        {
            Assert.False(WingOrderRules.RetireAfterDefence(WingOrder.FireForEffect, false));
            Assert.True(WingOrderRules.RetireAfterDefence(WingOrder.Attack, false));
            Assert.True(WingOrderRules.RetireAfterDefence(WingOrder.Maneuver, true));
            var target = new Unit();
            var first = WingDirective.AtTarget(WingOrder.FireForEffect, target);
            var repeated = WingDirective.AtTarget(WingOrder.FireForEffect, target);
            Assert.False(first.SameIntentAs(in repeated));
            Assert.Single(first.Targets);
        }

        [Fact]
        public void Splash_BypassesLeashAndYieldsWeaponsAuthorityToSafetyBehaviours()
        {
            Assert.False(WingOrderRules.SendsWingmanHunting(WingOrder.FireForEffect));
            Assert.True(WingOrderRules.SendsWingmanHunting(WingOrder.Attack));
            Assert.True(WingOrderRules.SendsWingmanHunting(WingOrder.Engage));
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
        [InlineData(WingOrder.JamTarget)]
        public void CompletedDesignationsRetireIndependentlyOfWhichReflexOwnsFlight(WingOrder order)
        {
            Assert.True(WingOrderRules.TargetTaskComplete(order, targetAlive: false, deliveryPending: false));
            Assert.False(WingOrderRules.TargetTaskComplete(order, targetAlive: true, deliveryPending: false));
            Assert.False(WingOrderRules.TargetTaskComplete(order, targetAlive: false, deliveryPending: true));
        }

        [Fact]
        public void FireForEffect_DoesNotRetireTargetTaskImmediatelyWhenTargetDies()
        {
            // SplashState manages expenditure and the entire designated target set.
            Assert.True(WingOrderRules.CarriesTarget(WingOrder.FireForEffect));
            Assert.False(WingOrderRules.TargetTaskComplete(WingOrder.FireForEffect, targetAlive: false, deliveryPending: false));
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

        [Fact]
        public void SeekAndDestroy_IsAMapPointTaskUntilItHandsOffToEngage()
        {
            Assert.True(WingOrderCatalog.NeedsPoint(WingOrder.SeekAndDestroy));
            Assert.True(WingOrderCatalog.TakesPoint(WingOrder.SeekAndDestroy));
            Assert.False(WingOrderRules.CarriesTarget(WingOrder.SeekAndDestroy));
            Assert.False(WingOrderRules.SendsWingmanHunting(WingOrder.SeekAndDestroy));
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                OrderRoePolicy.Authority(WingOrder.SeekAndDestroy));
            Assert.Equal(WingOrder.Engage,
                WingOrderRules.PointTaskCompletion(WingOrder.SeekAndDestroy));
            Assert.Equal(WingOrder.Formation,
                WingOrderRules.PointTaskCompletion(WingOrder.MoveToPoint));
            Assert.Equal("Seek and Destroy", WingOrderCatalog.Label(WingOrder.SeekAndDestroy));

            var surface = new WingMember { IsSurface = true };
            Assert.False(WingOrderCatalog.CanApply(surface, WingOrder.SeekAndDestroy));
        }

        [Fact]
        public void AttackOrder_IsPresentedAsAttackTargetWithoutChangingItsApiIdentity()
        {
            Assert.Equal("Attack Target", WingOrderCatalog.Label(WingOrder.Attack));
            Assert.Equal("ATK TGT", WingOrderCatalog.ShortLabel(WingOrder.Attack));
        }

        [Fact]
        public void StandDown_IsALoiterNotAMapPointTask()
        {
            Assert.Equal("Stand Down", WingOrderCatalog.Label(WingOrder.StandDown));
            Assert.Equal("WAIT", WingOrderCatalog.ShortLabel(WingOrder.StandDown));
            Assert.False(WingOrderCatalog.NeedsPoint(WingOrder.StandDown));
            Assert.False(WingOrderCatalog.TakesPoint(WingOrder.StandDown));
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                OrderRoePolicy.Authority(WingOrder.StandDown));
        }
    }
}
