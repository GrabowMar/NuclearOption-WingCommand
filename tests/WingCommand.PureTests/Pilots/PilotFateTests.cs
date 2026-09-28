using Xunit;

namespace WingCommand.PureTests
{
    public class PilotFateTests
    {
        [Theory]
        // home, aircraft disabled, pilot missing, dead, ejected → fate
        [InlineData(true, true, false, false, false, PilotFate.Home)]      // back in the reserve (the game disables it)
        [InlineData(false, false, false, false, false, PilotFate.Released)] // dismissed or taken over, intact and seated
        [InlineData(false, true, false, false, false, PilotFate.Down)]     // review M3c C1: shot down, pilot still seated
        [InlineData(false, false, false, true, false, PilotFate.Down)]
        [InlineData(false, false, false, false, true, PilotFate.Down)]
        [InlineData(false, false, true, false, false, PilotFate.Down)]
        public void APilotIsFreeAgainOnlyWhenHomeOrReleasedWithAnIntactAircraft(bool home, bool disabled, bool missing, bool dead,
            bool ejected, object fate) =>
            Assert.Equal((PilotFate)fate, PilotFates.Of(home, disabled, missing, dead, ejected));
    }
}
