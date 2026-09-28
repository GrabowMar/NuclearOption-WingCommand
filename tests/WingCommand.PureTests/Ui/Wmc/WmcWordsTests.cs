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
        public void ShapeNamesFitEightCharacters()
        {
            // Review R1 I3: "FORM FINGER FOUR (STRONG RIGHT)" was cut in the telemetry strip and the shape buttons.
            Assert.Equal("FNGR R", WmcWords.Shape("finger-four-right", "Finger Four (strong right)"));
            Assert.Equal("ECH R", WmcWords.Shape("echelon-right", "Echelon Right"));
            Assert.Equal("SPREAD", WmcWords.Shape("combat-spread", "Combat Spread"));
            Assert.Equal("ARROWHEA", WmcWords.Shape("user-arrow", "Arrowhead (wide)"));
            Assert.Equal(WmcText.Unknown, WmcWords.Shape(null, null));
            string[] shipped =
            {
                "echelon-right", "echelon-left", "line-abreast", "trail", "vic", "finger-four-right", "finger-four-left", "diamond",
                "box", "ladder", "combat-spread", "fluid-four", "wall", "offset-box", "card", "staggered-trail", "echelon-staggered",
                "heavy-stream", "high-cover", "low-cover", "sweep-ahead", "close-escort",
            };
            foreach (string id in shipped) Assert.InRange(WmcWords.Shape(id, "A Very Long Unmapped Name").Length, 1, 8);
            Assert.NotEqual(WmcWords.Shape("echelon-staggered", "x"), WmcWords.Shape("staggered-trail", "x"));
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
