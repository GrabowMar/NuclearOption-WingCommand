using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationClimbBankTests
    {
        [Theory]
        // At 900 km/h, four degrees of flight-path pitch already means 17 m/s climb.
        // Absolute climb rate and a small height error do not imply a runaway zoom.
        [InlineData(4f, 3f, 17.4f, -20f, 70f)]
        [InlineData(12f, 11f, 52f, -10f, 70f)]
        [InlineData(25f, 25f, 105.7f, -10f, 70f)]
        [InlineData(4f, 3f, 17.4f, -20f, 12f)]
        public void CommandedClimbingTurnsRetainTheirSafeBankCeiling(
            float pitch, float demand, float climb, float heightError, float ceiling)
        {
            Assert.Equal(ceiling, FormationControlRules.PitchDownBankAuthority(
                pitch, demand, climb, heightError, ceiling, 8f));
        }

        [Theory]
        // Recorded FS-20 post-departure intercepts: fast climbs still need to turn.
        [InlineData(35.1f, 18f, 86.9f, 981f, 55f, 35f)]
        [InlineData(13.8f, 1f, 54.9f, -133f, 55f, 35f)]
        [InlineData(9.9f, -1.5f, 54.1f, -168f, 55f, 35f)]
        // Follow a commanded climb; retain severe pitch recovery and lower safety ceilings.
        [InlineData(35f, 35f, 90f, 1000f, 55f, 55f)]
        [InlineData(45f, 0f, 90f, -100f, 55f, 8f)]
        [InlineData(35.1f, 18f, 86.9f, 981f, 12f, 12f)]
        [InlineData(45f, 0f, 90f, -100f, 5f, 5f)]
        public void ClimbRecoveryRetainsTurningUnlessPitchErrorIsExtreme(
            float pitch, float demand, float climb, float heightError, float ceiling, float expected)
        {
            Assert.Equal(expected, FormationControlRules.PitchDownBankAuthority(
                pitch, demand, climb, heightError, ceiling, 8f), 3);
        }
    }
}
