using System.Collections.Generic;
using Xunit;

namespace WingCommand
{
    internal static class FastMath
    {
        public static float SquareDistance(GlobalPosition a, GlobalPosition b) => 0f;
    }
}

namespace WingCommand.PureTests
{
    public class SplashSelectionTests
    {
        [Fact]
        public void SplashKeepsEveryLiveSelectionAcrossHudRefreshAndRetarget()
        {
            var first = new Unit();
            var second = new Unit();
            var hudTargets = new List<Unit> { first, null, new Unit { disabled = true }, second, first };
            var order = WingDirective.Splash(hudTargets, first);
            hudTargets.Clear();

            Assert.Equal(new[] { first, second }, order.Targets);
            var retargeted = order.Retarget(second);
            Assert.Same(second, retargeted.Target);
            Assert.Same(order.Targets, retargeted.Targets);
            Assert.Equal(WingOrder.FireForEffect, retargeted.Order);
            Assert.False(order.SameIntentAs(WingDirective.Splash(new[] { first }, first)));
        }
    }
}
