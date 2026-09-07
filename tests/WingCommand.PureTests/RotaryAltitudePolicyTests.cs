using Xunit;

namespace WingCommand.PureTests
{
    public class RotaryAltitudePolicyTests
    {
        [Fact]
        public void WorldHeightSlotIsConvertedToCurrentAgl()
        {
            // Own position: 120 m above sea level and 60 m AGL. A terrain-raised
            // formation slot at 155 m therefore requires a 95 m local AGL hold.
            Assert.Equal(95f, RotaryAltitudePolicy.SlotAgl(120f, 60f, 155f, 45f));
        }

        [Fact]
        public void LowerFormationSlotCannotCutTerrainClearance()
        {
            // This slot would otherwise be only 35 m above the local ground.
            Assert.Equal(45f, RotaryAltitudePolicy.SlotAgl(100f, 50f, 85f, 45f));
        }
    }
}
