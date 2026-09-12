using Xunit;

namespace WingCommand.PureTests
{
    public sealed class WingFormationTests
    {
        [Fact]
        public void DefaultMaxWingSizeIsFour()
        {
            Assert.Equal(4, WingFormation.MaxWingSize);
        }

        [Fact]
        public void DefaultFormationShapeIsEchelonRight()
        {
            Assert.Equal(FormationShape.EchelonRight, WingFormation.Shape);
        }

        [Fact]
        public void DefaultSlotSpacingIsPositive()
        {
            Assert.True(WingFormation.SlotSpacing > 0f);
        }

        [Fact]
        public void ExceedLimitAllowanceIsFour()
        {
            Assert.Equal(4, WingTuning.ExceedLimitAllowance);
        }
    }
}
