using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class PlanLanesTests
    {
        private static readonly List<uint> None = new List<uint>();

        [Fact]
        public void ALaneWhoseElementIsExactlyItsAircraftGoesByElement() =>
            Assert.True(PlanLanes.ByElement(1, 1, true, new List<uint> { 5, 6 }, new List<uint> { 6, 5 }));

        [Fact]
        public void AReusedLetterWithOtherAircraftGoesById()
        {
            // Review 2 [1]: B merged into A, a new element B formed from other aircraft; lane B's step went to them.
            Assert.False(PlanLanes.ByElement(1, 1, true, new List<uint> { 5, 6 }, new List<uint> { 7, 8 }));
        }

        [Fact]
        public void LaneAWhoseElementHoldsAnotherLanesJetsGoesById()
        {
            // Review 2 [4]: B's recovering jets sit in element A; lane A's step must not reach them.
            Assert.False(PlanLanes.ByElement(0, 0, true, new List<uint> { 1, 2 }, new List<uint> { 1, 2, 5, 6 }));
        }

        [Fact]
        public void ALaneMergedIntoAOrWithNoElementGoesById()
        {
            Assert.False(PlanLanes.ByElement(1, 0, true, new List<uint> { 5 }, new List<uint> { 1, 5 }));
            Assert.False(PlanLanes.ByElement(2, 2, false, new List<uint> { 5 }, None));
        }

        [Fact]
        public void ALaneWithNothingCapturedYetIsItsElement() => Assert.True(PlanLanes.ByElement(1, 1, true, None, new List<uint> { 5 }));

        [Theory]
        [InlineData(1, 0, (int)PlanKind.FormUp, false)]   // [2] FORM UP merged B into A: B keeps its own aircraft
        [InlineData(1, -1, (int)PlanKind.Attack, false)]  // [3] an ATTACK names no element: the aircraft stay
        [InlineData(1, 1, (int)PlanKind.Rtb, false)]      // they may leave the element while they recover
        [InlineData(1, 1, (int)PlanKind.Move, true)]
        [InlineData(0, 0, (int)PlanKind.Orbit, true)]
        public void WhenALaneTakesItsElementsMembersAgain(int lane, int result, int kind, bool recapture) =>
            Assert.Equal(recapture, PlanLanes.Recapture(lane, result, result >= 0, (PlanKind)kind));

        [Fact]
        public void AnAircraftOfAnotherRunningLaneIsNotCaptured()
        {
            var lanes = new[] { new List<uint>(), new List<uint> { 5, 6 }, new List<uint>(), new List<uint>() };
            var active = new[] { true, true, false, false };
            Assert.True(PlanLanes.OwnedElsewhere(0, 5, lanes, active));
            Assert.False(PlanLanes.OwnedElsewhere(0, 1, lanes, active));
            active[1] = false;
            Assert.False(PlanLanes.OwnedElsewhere(0, 5, lanes, active));
        }

        [Fact]
        public void APlayerOrderReachesALaneByItsAircraftNotByTheElementTheyShare()
        {
            // Review 2 [4]: an order to one of B's jets inside element A no longer holds lane A as well.
            Assert.False(PlanLanes.Reaches(new List<uint> { 1, 2 }, 0, 5, 0));
            Assert.True(PlanLanes.Reaches(new List<uint> { 1, 2 }, 0, 1, 0));
            Assert.True(PlanLanes.Reaches(None, 2, 9, 2));
        }
    }
}
