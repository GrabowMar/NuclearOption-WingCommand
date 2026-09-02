using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// Weapons authority follows the behaviour, not the standing order. This is the pure
    /// half of the oldest conflict in the system: a wingman recalled from past its leash
    /// flies formation while its order still reads Engage, and reading the order alone gave
    /// it weapons-free authority from inside the slot with ROE bypassed.
    /// </summary>
    public class OrderAuthorityTests
    {
        [Theory]
        [InlineData(WingOrder.Engage)]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        public void ARecalledWingmanIsGovernedByStandingRoeWhateverItsOrderSays(WingOrder order)
        {
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                         OrderRoePolicy.AuthorityFor(WingBehaviours.Rejoin, order));
        }

        [Theory]
        [InlineData(WingOrder.Engage)]
        [InlineData(WingOrder.Attack)]
        public void HoldingOverheadIsAlsoJustStationKeeping(WingOrder order)
        {
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                         OrderRoePolicy.AuthorityFor(WingBehaviours.DeckHold, order));
        }

        [Theory]
        [InlineData(WingBehaviours.MissileBreak)]
        [InlineData(WingBehaviours.Held)]
        public void AWingmanNotFlyingItsOrderDefendsItselfAndNothingMore(string behaviour)
        {
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                         OrderRoePolicy.AuthorityFor(behaviour, WingOrder.Engage));
        }

        [Fact]
        public void FlyingTheOrderStillAnswersToTheOrder()
        {
            Assert.Equal(OrderRoePolicy.Authority(WingOrder.Engage),
                         OrderRoePolicy.AuthorityFor(WingBehaviours.Task, WingOrder.Engage));
            Assert.Equal(OrderEngagementAuthority.ExplicitTarget,
                         OrderRoePolicy.AuthorityFor(WingBehaviours.Task, WingOrder.Attack));
        }

        [Fact]
        public void AnUnknownBehaviourFallsBackToTheOrderRatherThanToNothing()
        {
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                         OrderRoePolicy.AuthorityFor("someone.elses.behaviour",
                                                     WingOrder.Formation));
        }
    }
}
