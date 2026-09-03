using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The tuning behind a column of ships.
    ///
    /// Only the constants, not the geometry: FormationSolver lives in Flight/ and takes
    /// UnityEngine types, so it is outside what this project compiles by design. That the
    /// flattened slot really lands at sea level is checked in-game, not here.
    /// </summary>
    public class SurfaceFormationTests
    {
        [Fact]
        public void HullsSitFurtherApartThanAircraft()
        {
            // Wider, not narrower, and the direction is the whole point: hulls cannot climb
            // over each other to resolve a converging pass, and a column that closes up is a
            // column that rams. The rotary scale goes the other way for the opposite reason.
            Assert.True(WingTuning.SurfaceSpacingScale > 1f);
            Assert.True(WingTuning.SurfaceSpacingScale > WingTuning.RotarySpacingScale);
        }

        [Fact]
        public void AFullWingOfHullsSpansAWorkableNavalInterval()
        {
            // Trail places the last slot roughly MaxWingSize spacings astern. Anything under
            // a few hundred metres is a collision risk; anything over a couple of kilometres
            // puts the tail of the column outside the leader's own air defence.
            float astern = WingFormation.SlotSpacing *
                           WingTuning.SurfaceSpacingScale *
                           WingFormation.MaxWingSize;

            Assert.InRange(astern, 400f, 2000f);
        }
    }
}
