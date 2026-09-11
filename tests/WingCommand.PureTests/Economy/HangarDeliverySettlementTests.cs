using Xunit;

namespace WingCommand.PureTests
{
    public class HangarDeliverySettlementTests
    {
        [Fact]
        public void AcceptedSequenceRetainsOwnershipUntilCompletionOrDestruction()
        {
            Assert.True(HangarFieldPolicy.CanRefundDelivery(false, false, false, false));
            Assert.False(HangarFieldPolicy.CanRefundDelivery(true, false, false, false));
            Assert.True(HangarFieldPolicy.CanRefundDelivery(true, true, false, false));
            Assert.True(HangarFieldPolicy.CanRefundDelivery(true, false, true, false));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ObservedAircraftPreventsRefundEvenAfterNativeFailure(bool completed, bool destroyed)
        {
            Assert.False(HangarFieldPolicy.CanRefundDelivery(true, completed, destroyed, true));
        }
    }
}
