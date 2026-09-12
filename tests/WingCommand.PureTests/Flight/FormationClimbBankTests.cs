using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationClimbBankTests
    {
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
