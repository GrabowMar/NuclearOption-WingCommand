using Xunit;

namespace WingCommand.PureTests
{
    public class RoomNotchesTests
    {
        private static bool[] OnlyPlan()
        {
            var e = new bool[RoomNotches.Count];
            e[RoomNotches.Plan] = true;
            return e;
        }

        [Fact]
        public void FourNotchesWithKeys()
        {
            // The user's answer 2026-09-24: PLAN · BEHAVIOUR · SQUADRON · WORKSHOP, not nine.
            Assert.Equal(4, RoomNotches.Count);
            Assert.Equal(new[] { "PLAN", "BEHAVIOUR", "SQUADRON", "WORKSHOP" }, RoomNotches.Labels);
            Assert.Equal("CTRL 1", RoomNotches.Key(0));
            Assert.Equal("CTRL 4", RoomNotches.Key(3));
            Assert.Equal("", RoomNotches.Key(4));
            Assert.Equal(4, RoomNotches.Pending.Length);
        }

        [Fact]
        public void TabSkipsDisabledNotchesAndWraps()
        {
            var e = new bool[RoomNotches.Count];
            e[0] = e[2] = true;
            Assert.Equal(2, RoomNotches.Next(0, +1, e));
            Assert.Equal(0, RoomNotches.Next(2, +1, e));
            Assert.Equal(2, RoomNotches.Next(0, -1, e));
            Assert.Equal(0, RoomNotches.Next(0, +1, OnlyPlan()));   // the only one stays
        }

        [Fact]
        public void ADigitForADisabledOrMissingNotchDoesNothing()
        {
            Assert.Equal(0, RoomNotches.ForDigit(1, OnlyPlan()));
            Assert.Equal(-1, RoomNotches.ForDigit(2, OnlyPlan()));
            Assert.Equal(-1, RoomNotches.ForDigit(5, OnlyPlan()));
            Assert.Equal(-1, RoomNotches.ForDigit(0, OnlyPlan()));
        }

        [Fact]
        public void NotchesShrinkToFitTheRoom()
        {
            Assert.Equal(220f, RoomNotches.Width(2000f, 4, 220f, 4f));
            float w = RoomNotches.Width(700f, 4, 220f, 4f);
            Assert.True(w * 4 + 4f * 3 <= 700f + 0.01f);
            Assert.Equal(0f, RoomNotches.Width(-5f, 4, 220f, 4f));
        }
    }
}
