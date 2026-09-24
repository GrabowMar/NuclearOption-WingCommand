using Xunit;

namespace WingCommand.PureTests
{
    public class WmcWordsTests
    {
        [Fact]
        public void PostureCountsByDuty()
        {
            var rows = new[]
            {
                new SnapshotMember { Duty = (byte)MemberDuty.Engaged }, new SnapshotMember { Duty = (byte)MemberDuty.Engaged },
                new SnapshotMember { Duty = (byte)MemberDuty.Formation }, new SnapshotMember { Duty = (byte)MemberDuty.Defending },
            };
            Assert.Equal("2 ATK · 1 FORM · 1 DEF", WmcWords.Posture(rows, 4));
            Assert.Equal("NONE", WmcWords.Posture(rows, 0));
        }

        [Fact]
        public void ScopeTextNeverGrowsPastThreeNumbers()
        {
            Assert.Equal("WING · 3", WmcWords.ScopeText("WING", 3));
            Assert.Equal("#2 #3", WmcWords.ScopeText("#2 #3", 2));
            Assert.Equal("4 AIRCRAFT", WmcWords.ScopeText("#2 #3 #4 #5", 4));
            Assert.Equal("ELEMENT B · 2", WmcWords.ScopeText("ELEMENT B", 2));
        }

        [Fact]
        public void CueWordsNameTheNextInputAndFitTheBanner()
        {
            Assert.Equal("RIGHT-CLICK AN ENEMY · SHIFT ADDS", WmcWords.Cue(MapMode.Attack));
            Assert.Equal("ARMED ORBIT · RIGHT-CLICK A POINT", WmcWords.Banner(MapMode.Orbit));
            foreach (MapMode m in System.Enum.GetValues(typeof(MapMode)))
                if (m != MapMode.Off) Assert.True(WmcWords.Banner(m).Length <= 48, m.ToString());
            Assert.Null(WmcWords.Banner(MapMode.Off));
        }

        [Fact]
        public void TelemetryFieldsAreInvariantAndDashWhenUnknown()
        {
            Assert.Equal("2,425 m", WmcWords.Altitude(2425.4f));
            Assert.Equal("612 km/h", WmcWords.Speed(170f));
            Assert.Equal(WmcText.Unknown, WmcWords.Altitude(float.NaN));
        }
    }
}
