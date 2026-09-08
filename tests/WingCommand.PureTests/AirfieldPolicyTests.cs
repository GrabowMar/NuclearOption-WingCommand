using Xunit;

namespace WingCommand.PureTests
{
    public class TaxiRewritePolicyTests
    {
        [Fact]
        public void OnlyInboundTaxiIsRewritten()
        {
            // Owned post-landing service taxi must park before native apron ejection.
            Assert.True(TaxiRewritePolicy.ShouldPark(ours: true, enteringTaxi: true,
                                                     hasTakenOff: true));
        }

        [Fact]
        public void OutboundTaxiIsNeverRewritten()
        {
            // Pending delivery and refit departure retain native taxi while HasTakenOff is false.
            Assert.False(TaxiRewritePolicy.ShouldPark(ours: true, enteringTaxi: true,
                                                      hasTakenOff: false));
        }

        [Fact]
        public void OtherStatesAndOtherAircraftAreLeftAlone()
        {
            // Ignore transitions that do not enter taxi.
            Assert.False(TaxiRewritePolicy.ShouldPark(true, enteringTaxi: false, hasTakenOff: true));
            // Leave ordinary faction resupply taxi unchanged.
            Assert.False(TaxiRewritePolicy.ShouldPark(ours: false, enteringTaxi: true,
                                                      hasTakenOff: true));
        }

        [Fact]
        public void LeavingADepartureForAnythingButTakeoffGivesBackTheRunwaySlot()
        {
            // Cover interrupted taxi claims and stuck takeoff ejection, both of which bypass native
            // queue release.
            Assert.True(TaxiRewritePolicy.ShouldDrainQueue(ours: true, leavingDeparture: true,
                                                           enteringTakeoff: false));
        }

        [Fact]
        public void StartingATakeoffRunKeepsTheSlotItQueuedFor()
        {
            // Retain the claim when entering takeoff; native launch phases release it after safe use.
            Assert.False(TaxiRewritePolicy.ShouldDrainQueue(ours: true, leavingDeparture: true,
                                                            enteringTakeoff: true));
        }

        [Fact]
        public void NothingIsDrainedForAircraftThatWereNotDepartingOrAreNotOurs()
        {
            Assert.False(TaxiRewritePolicy.ShouldDrainQueue(true, leavingDeparture: false,
                                                            enteringTakeoff: false));
            Assert.False(TaxiRewritePolicy.ShouldDrainQueue(ours: false, leavingDeparture: true,
                                                            enteringTakeoff: false));
        }

        [Fact]
        public void RefitSuppressesTheStockPadEject()
        {
            // Refit keeps seated crew through native pad/runway ejection points; RTB permits
            // disembarkation.
            Assert.True(TaxiRewritePolicy.ShouldSuppressEjection(ours: true, refitPending: true,
                                                                 hasTakenOff: true));
            Assert.False(TaxiRewritePolicy.ShouldSuppressEjection(ours: true, refitPending: false,
                                                                  hasTakenOff: true));
            Assert.False(TaxiRewritePolicy.ShouldSuppressEjection(ours: false, refitPending: true,
                                                                  hasTakenOff: true));
        }

        [Fact]
        public void AnEjectAtBaseUnderRtbIsNotACombatLoss()
        {
            Assert.True(TaxiRewritePolicy.HoldsDeath(pendingSettlement: true, atFriendlyBase: false,
                                                     rtbOrRefit: false));
            Assert.True(TaxiRewritePolicy.HoldsDeath(pendingSettlement: false, atFriendlyBase: true,
                                                     rtbOrRefit: true));
            Assert.False(TaxiRewritePolicy.HoldsDeath(pendingSettlement: false, atFriendlyBase: false,
                                                      rtbOrRefit: true));
            Assert.False(TaxiRewritePolicy.HoldsDeath(pendingSettlement: false, atFriendlyBase: true,
                                                      rtbOrRefit: false));
        }
    }

    public class SupplyCompensationTests
    {
        [Fact]
        public void ADuplicateHangarDebitIsGivenBackExactlyOnce()
        {
            // Compensate the null-player hangar's extra stock debit after transaction reservation.
            Assert.Equal(1, SupplyCompensation.Delta(before: 5, after: 4));
        }

        [Fact]
        public void NoDebitMeansNoCompensation()
        {
            // No debit means no compensation for refused or abandoned launches.
            Assert.Equal(0, SupplyCompensation.Delta(before: 5, after: 5));
        }

        [Fact]
        public void StockRisingDuringTheCallIsNeverTreatedAsADebit()
        {
            // Do not remove unrelated supply increases from recovery or mission events.
            Assert.Equal(0, SupplyCompensation.Delta(before: 5, after: 7));
        }

        [Fact]
        public void ALargerDebitIsRestoredInFull()
        {
            Assert.Equal(3, SupplyCompensation.Delta(before: 10, after: 7));
        }
    }

    public class RecoverySettlementPolicyTests
    {
        [Fact]
        public void DespawnFollowsTheReserveSetting()
        {
            Assert.True(RecoverySettlementPolicy.ShouldDespawn(true));
            Assert.False(RecoverySettlementPolicy.ShouldDespawn(false));
        }

        [Fact]
        public void OnlyAPaidPurchaseIsRefunded()
        {
            Assert.True(RecoverySettlementPolicy.ShouldRefund(purchased: true, paid: 1500f));
            Assert.False(RecoverySettlementPolicy.ShouldRefund(purchased: true, paid: 0f));
            Assert.False(RecoverySettlementPolicy.ShouldRefund(purchased: false, paid: 1500f));
        }

        [Fact]
        public void AnEmptyWeaponsListIsReplacedByTheNativeFactoryFit()
        {
            Assert.True(RecoverySettlementPolicy.NativeLoadoutReplaces(0));
            Assert.False(RecoverySettlementPolicy.NativeLoadoutReplaces(1));
            Assert.False(RecoverySettlementPolicy.NativeLoadoutReplaces(6));
        }
    }
}
