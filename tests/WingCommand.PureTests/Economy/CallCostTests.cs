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
        public void RecruitingAFactionAircraftCostsAShareOfItsValueOnce()
        {
            CallQuote first = CallCost.Recruit(value: 4_000_000f, rate: 0.25f, allocation: 2_000_000f, sandbox: false, paid: false);
            Assert.True(first.Allowed);
            Assert.Equal(1_000_000f, first.Charge);
            Assert.False(first.TakeStock, "the aircraft is already flying: no stock changes hands");
            Assert.Equal(0f, CallCost.Recruit(4_000_000f, 0.25f, 0f, false, paid: true).Charge);
            Assert.True(CallCost.Recruit(4_000_000f, 0.25f, 0f, false, paid: true).Allowed, "paid once, it can be recruited again for free");
            Assert.Equal(4_000_000f, CallCost.Recruit(4_000_000f, 7f, 10_000_000f, false, false).Charge);
        }

        [Fact]
        public void RecruitingRefusesWhenTheAllocationIsShortAndIsFreeInTheSandbox()
        {
            CallQuote poor = CallCost.Recruit(4_000_000f, 0.25f, 500_000f, false, false);
            Assert.False(poor.Allowed);
            Assert.Contains("1,0M", poor.Reason.Replace('.', ','));
            CallQuote free = CallCost.Recruit(4_000_000f, 0.25f, 0f, sandbox: true, paid: false);
            Assert.True(free.Allowed);
            Assert.Equal(0f, free.Charge);
        }

        [Theory]
        // Review M3c I5. spawned, destroyed, via hangar, stock taken by hand → allocation back, stock by hand, return it
        [InlineData(false, false, true, false, true, 1, false)]   // a hangar never produced it: the game drew the airframe
        [InlineData(false, false, false, true, true, 1, false)]   // a service point never produced it: we drew it
        [InlineData(false, false, false, false, true, 0, false)]  // sandbox service point: nothing was drawn
        [InlineData(true, false, true, false, true, 0, true)]     // spawned, not adopted: back to the reserve (the game restocks)
        [InlineData(true, false, false, true, true, 0, true)]
        [InlineData(true, true, true, false, true, 0, false)]     // destroyed before it joined: the airframe is gone
        public void AFailedLaunchRefundsTheAllocationAndSettlesTheAirframeOnce(bool spawned, bool destroyed, bool viaHangar,
            bool tookStock, bool allocation, int stock, bool returnIt)
        {
            RefundPlan plan = CallCost.Refund(spawned, destroyed, viaHangar, tookStock);
            Assert.Equal(allocation, plan.Allocation);
            Assert.Equal(stock, plan.StockByHand);
            Assert.Equal(returnIt, plan.ReturnAircraft);
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
