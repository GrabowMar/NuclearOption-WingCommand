using Xunit;

namespace WingCommand.PureTests
{
    public class RotaryAltitudePolicyTests
    {
        [Fact]
        public void WorldHeightSlotIsConvertedToCurrentAgl()
        {
            // At 120 m ASL and 60 m AGL, a 155 m ASL slot requires 95 m local AGL hold.
            Assert.Equal(95f, RotaryAltitudePolicy.SlotAgl(120f, 60f, 155f, 45f));
        }

        [Fact]
        public void LowerFormationSlotCannotCutTerrainClearance()
        {
            // Raise the otherwise 35 m AGL slot to the 45 m terrain floor.
            Assert.Equal(45f, RotaryAltitudePolicy.SlotAgl(100f, 50f, 85f, 45f));
        }
    }
}
