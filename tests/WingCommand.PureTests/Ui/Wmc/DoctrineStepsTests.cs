using Xunit;

namespace WingCommand.PureTests
{
    public class DoctrineStepsTests
    {
        [Fact]
        public void CycleAdvancesOneAxisAndWraps()
        {
            WingDoctrine d = WingDoctrine.Reserve;   // Wing, Press, Close, spread, Hold, Slot
            WingDoctrine n = DoctrineSteps.Cycle(d, DoctrineAxis.Guard, 1);
            Assert.Equal(MissileGuard.Lead, n.Guard);
            Assert.Equal(d.Interval, n.Interval);
            Assert.Equal(MissileGuard.Off, DoctrineSteps.Cycle(n, DoctrineAxis.Guard, 1).Guard);
            Assert.Equal(TargetPolicy.Cover, DoctrineSteps.Cycle(d, DoctrineAxis.Targets, -1).Targets);
            Assert.False(DoctrineSteps.Cycle(d, DoctrineAxis.Spread, 1).SpreadWhenThreatened);
            Assert.Equal(EngagementReach.Long, DoctrineSteps.Cycle(d, DoctrineAxis.Reach, 1).Reach);
            Assert.Equal(FormationInterval.Standard, DoctrineSteps.Cycle(d, DoctrineAxis.Interval, 1).Interval);
        }

        [Fact]
        public void ResponseStaysBreakWhileTheGuardIsOff()
        {
            var d = new WingDoctrine(MissileGuard.Off, MissileResponse.Break, FormationInterval.Close, true, TargetPolicy.Hold, EngagementReach.Slot);
            Assert.Equal(MissileResponse.Break, DoctrineSteps.Cycle(d, DoctrineAxis.Response, 1).Response);
            Assert.Equal("—", DoctrineSteps.Word(d, DoctrineAxis.Response));
        }

        [Fact]
        public void WordsAreUpperCaseValues()
        {
            WingDoctrine d = WingDoctrine.Sweep;
            Assert.Equal("WING", DoctrineSteps.Word(d, DoctrineAxis.Guard));
            Assert.Equal("BREAK", DoctrineSteps.Word(d, DoctrineAxis.Response));
            Assert.Equal("OPEN", DoctrineSteps.Word(d, DoctrineAxis.Interval));
            Assert.Equal("ON", DoctrineSteps.Word(d, DoctrineAxis.Spread));
            Assert.Equal("BOTH", DoctrineSteps.Word(d, DoctrineAxis.Targets));
            Assert.Equal("LONG", DoctrineSteps.Word(d, DoctrineAxis.Reach));
            Assert.Equal("MISSILE GUARD", DoctrineSteps.Label(DoctrineAxis.Guard));
        }
    }
}
