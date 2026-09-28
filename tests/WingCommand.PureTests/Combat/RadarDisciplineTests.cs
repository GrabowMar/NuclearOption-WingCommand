using Xunit;

namespace WingCommand.PureTests
{
    public class RadarDisciplineTests
    {
        [Theory]
        [InlineData((int)RadarPolicy.On, false, true)]
        [InlineData((int)RadarPolicy.On, true, true)]
        [InlineData((int)RadarPolicy.Silent, false, false)]
        [InlineData((int)RadarPolicy.Silent, true, true)]
        [InlineData((int)RadarPolicy.Off, false, false)]
        [InlineData((int)RadarPolicy.Off, true, false)]
        public void TheRadarIsWantedOnPerPolicyAndEngagement(int policy, bool engaged, bool wanted) =>
            Assert.Equal(wanted, RadarDiscipline.Wanted((RadarPolicy)policy, engaged));

        [Fact]
        public void AnAircraftWithoutARadarIsNeverToggled()
        {
            var g = new RadarGate();
            Assert.False(RadarDiscipline.Step(ref g, hasRadar: false, isOn: true, wanted: false, dt: 5f));
        }

        [Fact]
        public void TheFirstMismatchTogglesAtOnceAndAMatchNever()
        {
            var g = new RadarGate();
            Assert.False(RadarDiscipline.Step(ref g, true, isOn: true, wanted: true, dt: 0.02f));
            Assert.True(RadarDiscipline.Step(ref g, true, isOn: true, wanted: false, dt: 0.02f));
        }

        [Fact]
        public void EngagementFlappingTogglesAtMostOnceEveryTwoSeconds()
        {
            var g = new RadarGate();
            bool on = false;
            int toggles = 0;
            for (int i = 0; i < 300; i++)   // 6 s at 50 Hz, engaged and not in turn every tick
            {
                bool wanted = RadarDiscipline.Wanted(RadarPolicy.Silent, i % 2 == 0);
                if (RadarDiscipline.Step(ref g, true, on, wanted, 0.02f))
                {
                    on = !on;
                    toggles++;
                }
            }
            Assert.InRange(toggles, 1, 3);
        }

        [Fact]
        public void ARadarSwitchedBackOnFromOutsideIsSwitchedOffAgainAfterTheGap()
        {
            // A rearm or a new radar pod installs a radar that is already on (weapons-radar.md B).
            var g = new RadarGate();
            Assert.True(RadarDiscipline.Step(ref g, true, isOn: true, wanted: false, dt: 0.02f));
            Assert.False(RadarDiscipline.Step(ref g, true, isOn: true, wanted: false, dt: 1f), "within the gap");
            Assert.True(RadarDiscipline.Step(ref g, true, isOn: true, wanted: false, dt: 1.1f));
        }
    }
}
