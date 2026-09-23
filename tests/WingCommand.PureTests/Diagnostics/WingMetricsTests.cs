using Xunit;

namespace WingCommand.PureTests
{
    public class WingMetricsTests
    {
        [Fact]
        public void SlotErrorRmsAndMaxAreTimeWeightedOverTheWindow()
        {
            var m = new WingMetrics();
            m.Reset(0f);
            m.Sample(0, 10f, false, 150f, 1f, 1f);
            m.Sample(1, 20f, false, 150f, 1f, 1f);
            MetricsSnapshot s = m.Snapshot(1f);
            Assert.Equal(15.811f, s.SlotRmsM, 2);
            Assert.Equal(20f, s.SlotMaxM);
            Assert.Equal(2, s.Members);
        }

        [Fact]
        public void CaptureTimeRunsFromTheResetToEachMembersFirstStationSample()
        {
            var m = new WingMetrics();
            m.Reset(100f);
            m.Sample(0, 50f, false, 150f, 110f, 1f);
            m.Sample(0, 5f, true, 150f, 130f, 1f);
            m.Sample(1, 60f, false, 150f, 130f, 1f);
            MetricsSnapshot s = m.Snapshot(140f);
            Assert.Equal(1, s.CapturedMembers);
            Assert.Equal(30f, s.MeanCaptureSeconds, 3);
            Assert.Equal(1f / 3f, s.StationFraction, 3);
            Assert.Equal(40f, s.WindowSeconds, 3);
        }

        [Fact]
        public void EventsAreCountedByKind()
        {
            var m = new WingMetrics();
            m.Reset(0f);
            m.Event(WingEventKind.GcasActivated);
            m.Event(WingEventKind.CollisionEmergency);
            m.Event(WingEventKind.CollisionEmergency);
            m.Event(WingEventKind.FallingBehind);
            m.Event(WingEventKind.BehaviourChanged);
            MetricsSnapshot s = m.Snapshot(1f);
            Assert.Equal(1, s.Gcas);
            Assert.Equal(2, s.Collision);
            Assert.Equal(1, s.FallingBehind);
            Assert.Equal(1, s.Transitions);
        }

        [Fact]
        public void MinimaTrackTheClosestSeparationAndTheSlowestMember()
        {
            var m = new WingMetrics();
            m.Reset(0f);
            m.Separation(120f);
            m.Separation(35f);
            m.Sample(0, 1f, true, 60f, 1f, 1f);
            m.Sample(1, 1f, true, 52f, 1f, 1f);
            MetricsSnapshot s = m.Snapshot(1f);
            Assert.Equal(35f, s.MinSeparationM);
            Assert.Equal(52f, s.MinSpeedMps);
        }

        [Fact]
        public void AnEmptyWindowReportsSentinelsNotNaN()
        {
            var m = new WingMetrics();
            m.Reset(5f);
            MetricsSnapshot s = m.Snapshot(5f);
            Assert.Equal(0, s.Members);
            Assert.Equal(0f, s.SlotRmsM);
            Assert.Equal(-1f, s.MeanCaptureSeconds);
            Assert.Equal(-1f, s.MinSeparationM);
            Assert.Equal(-1f, s.MinSpeedMps);
        }

        [Fact]
        public void ResetStartsAFreshWindow()
        {
            var m = new WingMetrics();
            m.Reset(0f);
            m.Sample(0, 40f, false, 100f, 1f, 1f);
            m.Event(WingEventKind.GcasActivated);
            m.Reset(10f);
            m.Sample(0, 4f, true, 100f, 11f, 1f);
            MetricsSnapshot s = m.Snapshot(11f);
            Assert.Equal(4f, s.SlotMaxM);
            Assert.Equal(0, s.Gcas);
            Assert.Equal(1f, s.MeanCaptureSeconds, 3);
        }
    }
}
