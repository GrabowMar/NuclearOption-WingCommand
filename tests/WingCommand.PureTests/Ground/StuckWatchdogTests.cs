using Xunit;

namespace WingCommand.PureTests
{
    public class StuckWatchdogTests
    {
        private const float Dt = 0.5f;

        [Fact]
        public void NoProgressReroutesThenRelocatesOnceAndNeverAgain()
        {
            var w = new StuckWatchdog();
            WatchdogAction first = WatchdogAction.None, second = WatchdogAction.None;
            float rerouteAt = -1f, relocateAt = -1f;
            for (float t = 0f; t < 200f; t += Dt)
            {
                WatchdogAction a = w.Update(Vec3.Zero, false, Dt);
                if (a == WatchdogAction.Reroute && rerouteAt < 0f) { rerouteAt = t; first = a; }
                if (a == WatchdogAction.Relocate)
                {
                    Assert.True(relocateAt < 0f, "relocated twice");
                    relocateAt = t;
                    second = a;
                }
            }
            Assert.Equal(WatchdogAction.Reroute, first);
            Assert.Equal(WatchdogAction.Relocate, second);
            Assert.InRange(rerouteAt, StuckWatchdog.RerouteSeconds - 1f, StuckWatchdog.RerouteSeconds + 1f);
            Assert.InRange(relocateAt, StuckWatchdog.RelocateSeconds - 1f, StuckWatchdog.RelocateSeconds + 1f);
        }

        [Fact]
        public void WaitingOrMovingIsNeverStuck()
        {
            var w = new StuckWatchdog();
            for (float t = 0f; t < 120f; t += Dt) Assert.Equal(WatchdogAction.None, w.Update(Vec3.Zero, true, Dt));
            var moving = new StuckWatchdog();
            for (float t = 0f; t < 120f; t += Dt) Assert.Equal(WatchdogAction.None, moving.Update(new Vec3(0f, 0f, t * 2f), false, Dt));
        }
    }
}
