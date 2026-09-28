using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class LiveryChoiceTests
    {
        [Fact]
        public void ATokenRoundTripsEveryKind()
        {
            Assert.Equal("B3", LiveryChoice.Token(LiveryKind.Builtin, 3, null));
            Assert.Equal("Adesert camo", LiveryChoice.Token(LiveryKind.AppData, 0, "desert camo"));
            Assert.Equal("W123456", LiveryChoice.Token(LiveryKind.Workshop, 0, "123456"));
            Assert.True(LiveryChoice.TryParse("B3", out LiveryKind k1, out int i1, out _));
            Assert.Equal(LiveryKind.Builtin, k1);
            Assert.Equal(3, i1);
            Assert.True(LiveryChoice.TryParse("Adesert camo", out LiveryKind k2, out _, out string n2));
            Assert.Equal(LiveryKind.AppData, k2);
            Assert.Equal("desert camo", n2);
            Assert.True(LiveryChoice.TryParse("W123456", out LiveryKind k3, out _, out string n3));
            Assert.Equal(LiveryKind.Workshop, k3);
            Assert.Equal("123456", n3);
        }

        [Fact]
        public void AGarbledTokenIsIgnored()
        {
            Assert.False(LiveryChoice.TryParse("Bx", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("B-1", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("Wabc", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("A", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse(null, out _, out _, out _));
        }

        [Fact]
        public void AnAppDataNameThatClimbsFoldersIsRejected()
        {
            Assert.False(LiveryChoice.TryParse("A../evil", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("Aa/b", out _, out _, out _));
            Assert.False(LiveryChoice.TryParse("Aa\\b", out _, out _, out _));
        }

        [Fact]
        public void ASavedLiveryIsFoundByItsKeyNotItsPlace()
        {
            var offered = new List<string> { null, "B0", "B2", "Afoo" };
            Assert.Equal(3, LiveryChoice.IndexOf(offered, "Afoo"));
            Assert.Equal(2, LiveryChoice.IndexOf(offered, "B2"));
        }

        [Fact]
        public void ALiveryNoLongerOfferedReadsStandard()
        {
            var offered = new List<string> { null, "B0" };
            Assert.Equal(0, LiveryChoice.IndexOf(offered, "B5"));
            Assert.Equal(0, LiveryChoice.IndexOf(offered, null));
        }

        [Fact]
        public void TheStepperWrapsFromStandardToTheLast()
        {
            Assert.Equal(3, LiveryChoice.Step(0, 4, -1));
            Assert.Equal(0, LiveryChoice.Step(3, 4, 1));
            Assert.Equal(0, LiveryChoice.Step(0, 1, 1));
        }

        [Fact]
        public void TheLiveryMapSurvivesDelimitersInKeys()
        {
            var map = new Dictionary<string, string> { { "FS-20;x|y", "Aa,b;c" }, { "VT-7", "B1" } };
            Dictionary<string, string> back = LiveryChoice.Decode(LiveryChoice.Encode(map));
            Assert.Equal(2, back.Count);
            Assert.Equal("Aa,b;c", back["FS-20;x|y"]);
            Assert.Equal("B1", back["VT-7"]);
            Assert.Empty(LiveryChoice.Decode(""));
            Assert.Empty(LiveryChoice.Decode("garbage-without-a-field"));
        }
    }
}
