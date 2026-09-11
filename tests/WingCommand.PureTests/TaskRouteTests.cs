using Xunit;

namespace WingCommand.PureTests
{
    public sealed class TaskRouteTests
    {
        [Fact]
        public void PatrolCyclesThenFinishesRemainingLegsWhenDisabled()
        {
            var route = new TaskRoute<string>();
            route.Add("A");
            Assert.False(route.SetRepeat(true));
            route.Add("B");
            Assert.True(route.SetRepeat(true));
            Assert.True(route.Advance(1, 1, out string next));
            Assert.Equal("B", next);
            Assert.True(route.Advance(2, 2, out next));
            Assert.Equal("A", next);
            Assert.Equal(new[] { "A", "B" }, route);
            route.SetRepeat(false);
            Assert.True(route.Advance(3, 3, out next));
            Assert.False(route.Advance(4, 4, out _));
            Assert.Empty(route);
        }

        [Fact]
        public void StaleCompletionCannotRemoveOrRotateANewerRoute()
        {
            var route = new TaskRoute<string>();
            route.Add("new A");
            route.Add("new B");
            route.SetRepeat(true);
            Assert.False(route.Advance(1, 2, out _));
            Assert.Equal(new[] { "new A", "new B" }, route);
        }

        [Fact]
        public void RefitPreservesCurrentLegAndLoopAndCanOnlyRestoreOnce()
        {
            var route = new TaskRoute<string>();
            route.Add("A");
            route.Add("B");
            route.SetRepeat(true);
            route.Advance(1, 1, out _);
            route.Suspend("B");
            Assert.Empty(route);
            Assert.False(route.Repeat);
            Assert.Equal("B", route.Restore(_ => true, "formation"));
            Assert.True(route.Repeat);
            Assert.Equal(new[] { "B", "A" }, route);
            Assert.Equal("formation", route.Restore(_ => true, "formation"));
            Assert.False(route.Repeat);
        }

        [Fact]
        public void ReplacementAndAbandonmentCancelSuspendedTask()
        {
            var route = new TaskRoute<string>();
            route.Suspend("attack");
            route.Clear();
            Assert.Equal("formation", route.Restore(_ => true, "formation"));
            route.Suspend("hold");
            route.CancelSuspension();
            Assert.Equal("formation", route.Restore(_ => true, "formation"));
        }

        [Fact]
        public void DestroyedTargetsAreSkippedAfterRefit()
        {
            var route = new TaskRoute<string>();
            route.Add("destroyed");
            route.Add("surviving");
            route.Suspend("destroyed");
            Assert.Equal("surviving", route.Restore(target => target != "destroyed", "formation"));
            Assert.Single(route);
            route.Suspend("surviving");
            Assert.Equal("formation", route.Restore(_ => false, "formation"));
        }

        [Fact]
        public void PatrolUsesRoeOnlyWhileItsTaskOwnsFlight()
        {
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                OrderRoePolicy.AuthorityFor(WingBehaviours.Task, WingOrder.MoveToPoint, patrol: true));
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                OrderRoePolicy.AuthorityFor(WingBehaviours.Task, WingOrder.MoveToPoint));
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                OrderRoePolicy.AuthorityFor(WingBehaviours.MissileBreak, WingOrder.MoveToPoint, patrol: true));
            Assert.Equal(StationFireMode.None,
                OrderRoePolicy.StationFire(OrderEngagementAuthority.StandingRoe, WingRoe.Hold, false, true));
        }
    }
}
