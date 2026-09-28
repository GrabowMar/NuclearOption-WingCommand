using Xunit;

namespace WingCommand.PureTests
{
    public class FormationDiagnosticsTests
    {
        [Fact]
        public void ModeAndSafetyTransitionsRemainVisibleDuringBurstCooldown()
        {
            var recovery = new FormationRecovery();
            Assert.True(recovery.ReportStateChanged("Intercept", false));
            Assert.True(recovery.BurstReport(true, 0.02f));
            for (int i = 0; i < 500; i++) recovery.BurstReport(false, 0.02f);
            Assert.False(recovery.BurstReport(true, 0.02f));
            Assert.False(recovery.ReportStateChanged("Intercept", false));
            Assert.True(recovery.ReportStateChanged("Intercept", true));
            Assert.False(recovery.ReportStateChanged("Intercept", true));
            Assert.True(recovery.ReportStateChanged("Intercept", false));
            Assert.True(recovery.ReportStateChanged("CollisionAvoidance", false));
            Assert.True(recovery.ReportStateChanged("StaggerHold", false));
            Assert.False(recovery.ReportStateChanged("StaggerHold", false));
        }
    }
}
