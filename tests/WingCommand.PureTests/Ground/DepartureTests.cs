using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class DepartureTests
    {
        private static RunwaySample Runway(float width) => new RunwaySample
        {
            Start = new Vec3(0f, 0f, 0f), End = new Vec3(0f, 0f, 2000f), Width = width, Length = 2000f, Takeoff = true,
        };

        [Theory]
        [InlineData(45f, 11f, 2)]
        [InlineData(90f, 11f, 4)]
        [InlineData(200f, 11f, 4)]
        [InlineData(20f, 11f, 1)]
        public void AbreastCountFollowsTheRunwayWidth(float width, float span, int expected) =>
            Assert.Equal(expected, LineupPlanner.Abreast(width, span));

        [Fact]
        public void LineupSlotsAreCentredAndRowsStepBack()
        {
            RunwaySample r = Runway(60f);
            Vec3 a = LineupPlanner.Slot(r, false, 0, 0, 2, 2), b = LineupPlanner.Slot(r, false, 0, 1, 2, 2);
            Vec3 c = LineupPlanner.Slot(r, false, 1, 0, 2, 2);
            Assert.Equal(0f, a.X + b.X, 3);
            Assert.Equal(30f, Math.Abs(a.X - b.X), 3);
            Assert.Equal(a.Z, b.Z, 3);
            Assert.Equal(LineupPlanner.RowGap, a.Z - c.Z, 3);
            Assert.True(c.Z >= LineupPlanner.ThresholdMargin - 1e-3f, "the last row is still on the runway");
            Vec3 reversed = LineupPlanner.Slot(r, true, 0, 0, 1, 1);
            Assert.True(reversed.Z > 1900f && Math.Abs(reversed.X) < 1e-3f, $"reversed slot {reversed}");
        }

        private static DepartureSequencer FourShip()
        {
            var d = new DepartureSequencer();
            for (int o = 1; o <= 4; o++) d.Expect(o, 2);
            return d;
        }

        /// <summary>Each member in turn is granted the lineup, clears the threshold and is lined up.</summary>
        private static void LineUp(DepartureSequencer d, float time, params int[] owners)
        {
            foreach (int o in owners)
            {
                Assert.True(d.MayLineUp(o, time), $"{o} may line up");
                d.ClearedThreshold(o);
                d.LinedUp(o);
            }
        }

        [Fact]
        public void AMixedGroupLinesUpAsFewAbreastAsItsWidestTypeAllows()
        {
            // Review M3a #10: the type expected last set the count.
            var d = new DepartureSequencer();
            d.Expect(1, 4);
            d.Expect(2, 2);
            d.Expect(3, 4);
            Assert.Equal(2, d.Abreast);
        }

        [Fact]
        public void AMemberArrivingAfterTheLockWaitsForTheNextGroupAndLeavesTheSlotsAlone()
        {
            // Review M3a #10: a late narrow type shrank the rows (and a late arrival moved the slots) mid-lineup.
            var d = new DepartureSequencer();
            d.Expect(1, 2);
            d.Expect(2, 2);
            d.Enqueue(1, 0f);
            d.Enqueue(2, 0f);
            Assert.True(d.MayLineUp(1, 0f));
            d.Expect(3, 1);
            d.Enqueue(3, 1f);
            Assert.Equal(2, d.Abreast);
            Assert.Equal(1, d.Rows);
            d.ClearedThreshold(1);
            d.LinedUp(1);
            LineUp(d, 2f, 2);
            Assert.False(d.MayLineUp(3, 2f));
            Assert.True(d.MayRoll(1, 3f));
            d.Airborne(1);
            d.Airborne(2);
            Assert.False(d.RunwayLocked);
            Assert.True(d.MayLineUp(3, 4f), "the late member is the next group");
            Assert.Equal(1, d.Abreast);
            d.SlotOf(3, out int row, out int column);
            Assert.Equal(0, row);
            Assert.Equal(0, column);
        }

        [Fact]
        public void TheGroupLinesUpTogetherOnceEveryoneHoldsShort()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 3; o++) d.Enqueue(o, 0f);
            Assert.False(d.MayLineUp(1, 10f));
            d.Enqueue(4, 12f);
            Assert.True(d.MayLineUp(1, 12f));
            Assert.True(d.RunwayLocked);
            LineUp(d, 12f, 1, 2, 3);
            d.SlotOf(3, out int row, out int column);
            Assert.Equal(1, row);
            Assert.Equal(0, column);
        }

        [Fact]
        public void MembersLineUpOneByOneAsThePreviousClearsTheThreshold()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            Assert.True(d.MayLineUp(1, 1f));
            Assert.False(d.MayLineUp(2, 1f));
            d.ClearedThreshold(1);
            Assert.True(d.MayLineUp(2, 2f));
            d.Remove(3);
            d.ClearedThreshold(2);
            Assert.True(d.MayLineUp(4, 3f), "a removed member never holds up the next");
        }

        [Fact]
        public void WhoeverReachesTheHoldShortNextLinesUpIntoTheNextSlot()
        {
            // Queued early (400 m out) the queue order need not be the order at the hold-short: slots follow the lineup.
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            Assert.True(d.MayLineUp(2, 1f), "2 got to the hold-short first");
            Assert.False(d.MayLineUp(1, 1f), "one at a time");
            d.SlotOf(2, out int row, out int column);
            Assert.Equal((0, 0), (row, column));
            d.ClearedThreshold(2);
            Assert.True(d.MayLineUp(1, 2f));
            d.SlotOf(1, out row, out column);
            Assert.Equal((0, 1), (row, column));
            Assert.Equal(2, d.Rows);
        }

        [Fact]
        public void ARowMissingARemovedMemberStillRolls()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 3; o++) d.Enqueue(o, 0f);
            d.Remove(4);
            Assert.True(d.MayLineUp(1, 0f));
            d.ClearedThreshold(1);
            Assert.True(d.MayLineUp(2, 0f));
            d.ClearedThreshold(2);
            Assert.True(d.MayLineUp(3, 0f));
            d.LinedUp(1);
            d.LinedUp(2);
            d.LinedUp(3);
            Assert.True(d.MayRoll(1, 1f));
            Assert.True(d.MayRoll(3, 1f + DepartureSequencer.RowInterval + 0.1f), "row 2 has no fourth member to wait for");
        }

        [Fact]
        public void AGroupThatNeverGathersLinesUpAfterTheTimeout()
        {
            DepartureSequencer d = FourShip();
            d.Enqueue(1, 0f);
            Assert.False(d.MayLineUp(1, DepartureSequencer.GatherTimeout - 1f));
            Assert.True(d.MayLineUp(1, DepartureSequencer.GatherTimeout + 1f));
        }

        [Fact]
        public void RowsRollInOrderAnIntervalApart()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            LineUp(d, 0f, 1, 2, 3, 4);
            Assert.True(d.MayRoll(1, 20f));
            Assert.True(d.MayRoll(2, 20f));
            Assert.False(d.MayRoll(3, 20f + DepartureSequencer.RowInterval - 1f));
            Assert.True(d.MayRoll(3, 20f + DepartureSequencer.RowInterval + 0.1f));
        }

        [Fact]
        public void NothingLinesUpOrStartsItsRollWhileAForeignAircraftIsOnTheRunway()
        {
            // Review M3a #7: native traffic on the runway was not checked.
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            d.RunwayBusy = true;
            Assert.False(d.MayLineUp(1, 5f));
            d.RunwayBusy = false;
            LineUp(d, 6f, 1, 2);
            d.RunwayBusy = true;
            Assert.False(d.MayRoll(1, 7f));
            d.RunwayBusy = false;
            Assert.True(d.MayRoll(1, 8f));
            d.RunwayBusy = true;
            Assert.True(d.MayRoll(2, 8f), "a row that has started rolls on together");
        }

        [Fact]
        public void NothingLinesUpWhileANativeLandingIsPending()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            d.NativeLandingPending = true;
            Assert.False(d.MayLineUp(1, 5f));
            d.NativeLandingPending = false;
            Assert.True(d.MayLineUp(1, 6f));
        }

        [Fact]
        public void TheLockIsReleasedWhenTheLastMemberIsAirborneAndAMissingMemberNeverBlocks()
        {
            DepartureSequencer d = FourShip();
            for (int o = 1; o <= 4; o++) d.Enqueue(o, 0f);
            LineUp(d, 0f, 1, 2);
            d.Remove(4);   // lost on the ground
            LineUp(d, 0f, 3);
            Assert.True(d.MayRoll(1, 1f));
            Assert.True(d.MayRoll(3, 1f + DepartureSequencer.RowInterval + 0.1f), "row 2 rolls without its missing member");
            d.Airborne(1);
            d.Airborne(2);
            Assert.True(d.RunwayLocked);
            d.Airborne(3);
            Assert.False(d.RunwayLocked);
        }
    }
}
