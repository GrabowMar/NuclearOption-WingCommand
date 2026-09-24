using Xunit;

namespace WingCommand.PureTests
{
    public class RoomNotchesTests
    {
        private static bool[] OnlyTactical()
        {
            var e = new bool[RoomNotches.Count];
            e[RoomNotches.Tactical] = true;
            return e;
        }

        [Fact]
        public void NineNotchesWithKeys()
        {
            Assert.Equal(9, RoomNotches.Labels.Length);
            Assert.Equal("TACTICAL", RoomNotches.Labels[0]);
            Assert.Equal("DEBRIEF", RoomNotches.Labels[8]);
            Assert.Equal("CTRL 1", RoomNotches.Key(0));
            Assert.Equal("CTRL 9", RoomNotches.Key(8));
            Assert.Equal(9, RoomNotches.Pending.Length);
        }

        [Fact]
        public void TabSkipsDisabledNotchesAndWraps()
        {
            var e = new bool[RoomNotches.Count];
            e[0] = e[3] = e[7] = true;
            Assert.Equal(3, RoomNotches.Next(0, +1, e));
            Assert.Equal(7, RoomNotches.Next(3, +1, e));
            Assert.Equal(0, RoomNotches.Next(7, +1, e));
            Assert.Equal(7, RoomNotches.Next(0, -1, e));
            Assert.Equal(0, RoomNotches.Next(0, +1, OnlyTactical()));   // the only one stays
        }

        [Fact]
        public void ADigitForADisabledNotchDoesNothing()
        {
            Assert.Equal(0, RoomNotches.ForDigit(1, OnlyTactical()));
            Assert.Equal(-1, RoomNotches.ForDigit(2, OnlyTactical()));
            Assert.Equal(-1, RoomNotches.ForDigit(0, OnlyTactical()));
            Assert.Equal(-1, RoomNotches.ForDigit(10, OnlyTactical()));
        }

        [Fact]
        public void NotchesShrinkToFitTheRoom()
        {
            Assert.Equal(164f, RoomNotches.Width(2000f, 9, 164f, 4f));
            float w = RoomNotches.Width(1100f, 9, 164f, 4f);
            Assert.True(w * 9 + 4f * 8 <= 1100f + 0.01f);
            Assert.Equal(0f, RoomNotches.Width(-5f, 9, 164f, 4f));
        }
    }
}
