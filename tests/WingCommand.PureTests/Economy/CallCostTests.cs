using Xunit;

namespace WingCommand.PureTests
{
    public class CallCostTests
    {
        [Fact]
        public void ACallChargesTheListValueAndTakesStockOnlyWhereTheGameDoesNot()
        {
            CallQuote hangar = CallCost.Quote(price: 3_000_000f, allocation: 10_000_000f, stock: 4, sandbox: false, viaHangar: true);
            Assert.True(hangar.Allowed);
            Assert.Equal(3_000_000f, hangar.Charge);
            Assert.False(hangar.TakeStock, "a hangar spawn draws the faction's supply itself");
            CallQuote servicePoint = CallCost.Quote(3_000_000f, 10_000_000f, 4, false, viaHangar: false);
            Assert.True(servicePoint.TakeStock);
        }

        [Fact]
        public void NoStockOrTooLittleAllocationRefusesWithAReason()
        {
            CallQuote empty = CallCost.Quote(3_000_000f, 10_000_000f, 0, false, true);
            Assert.False(empty.Allowed);
            Assert.Contains("stock", empty.Reason);
            CallQuote poor = CallCost.Quote(3_000_000f, 1_000_000f, 4, false, true);
            Assert.False(poor.Allowed);
            Assert.Contains("3,0M", poor.Reason.Replace('.', ','));
            Assert.Equal(0f, poor.Charge);
        }

        [Fact]
        public void TheSandboxCallsForFree()
        {
            CallQuote free = CallCost.Quote(3_000_000f, 0f, 0, sandbox: true, viaHangar: false);
            Assert.True(free.Allowed);
            Assert.Equal(0f, free.Charge);
            Assert.False(free.TakeStock);
        }
    }
}
