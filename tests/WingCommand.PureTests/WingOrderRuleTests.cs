using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// "Which orders send a wingman away from the wing" had two definitions that disagreed:
    /// the leash reflex leashed Engage and Attack, while WingOrderCatalog.IsTargetOrder —
    /// whose doc still described the leash it no longer drove — answered Attack alone.
    /// There is one now, and this is it.
    /// </summary>
    public class WingOrderRuleTests
    {
        [Theory]
        [InlineData(WingOrder.Engage)]
        [InlineData(WingOrder.Attack)]
        public void AnOrderThatLeavesTheWingNeedsATether(WingOrder order)
        {
            Assert.True(WingOrderRules.SendsWingmanHunting(order));
        }

        [Theory]
        [InlineData(WingOrder.FireForEffect)]
        [InlineData(WingOrder.JamTarget)]
        public void AnOrderFlownFromTheSlotDoesNot(WingOrder order)
        {
            // Both carry a designated target, which is why they were mistaken for the same
            // thing. Neither leaves the slot, so neither can overshoot a leash.
            Assert.True(WingOrderRules.CarriesTarget(order));
            Assert.False(WingOrderRules.SendsWingmanHunting(order));
        }

        [Theory]
        [InlineData(WingOrder.Formation)]
        [InlineData(WingOrder.ReturnToBase)]
        [InlineData(WingOrder.OrbitHere)]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.MoveToPoint)]
        [InlineData(WingOrder.DeliverCargo)]
        [InlineData(WingOrder.FallBack)]
        [InlineData(WingOrder.Maneuver)]
        public void EverythingElseIsNeitherHuntingNorTargeted(WingOrder order)
        {
            Assert.False(WingOrderRules.SendsWingmanHunting(order));
            Assert.False(WingOrderRules.CarriesTarget(order));
        }
    }
}
