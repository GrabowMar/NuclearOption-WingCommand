using Xunit;

namespace WingCommand.PureTests
{
    public class TaxiRewritePolicyTests
    {
        [Fact]
        public void OnlyInboundTaxiIsRewritten()
        {
            // Landed and taxiing to a service point: this is the run that ejects the pilot
            // on the apron, and the only one this mod takes over.
            Assert.True(TaxiRewritePolicy.ShouldPark(ours: true, enteringTaxi: true,
                                                     hasTakenOff: true));
        }

        [Fact]
        public void OutboundTaxiIsNeverRewritten()
        {
            // A delivery on the threshold and a refit leaving its parking spot both have
            // HasTakenOff false, and both need the stock taxi they are being handed.
            Assert.False(TaxiRewritePolicy.ShouldPark(ours: true, enteringTaxi: true,
                                                      hasTakenOff: false));
        }

        [Fact]
        public void OtherStatesAndOtherAircraftAreLeftAlone()
        {
            // Not a taxi transition at all.
            Assert.False(TaxiRewritePolicy.ShouldPark(true, enteringTaxi: false, hasTakenOff: true));
            // A faction AI taxiing to resupply is the mission working as intended.
            Assert.False(TaxiRewritePolicy.ShouldPark(ours: false, enteringTaxi: true,
                                                      hasTakenOff: true));
        }

        [Fact]
        public void LeavingADepartureForAnythingButTakeoffGivesBackTheRunwaySlot()
        {
            // Covers both leaks: an interrupted taxi that had already queued at the
            // hold-short line, and a takeoff run whose twelve-second stuck timer ejected
            // the pilot straight to parked without dequeuing.
            Assert.True(TaxiRewritePolicy.ShouldDrainQueue(ours: true, leavingDeparture: true,
                                                           enteringTakeoff: false));
        }

        [Fact]
        public void StartingATakeoffRunKeepsTheSlotItQueuedFor()
        {
            // The takeoff state is what the slot was reserved for, and gives it back itself.
            // Draining here would release the strip to somebody else while this aircraft is
            // still accelerating down it.
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
            // Helicopters eject on the pad and a parked jet ejects after ten seconds.
            // Refit has to keep the pilot in the seat; RTB wants that eject as disembark.
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
            // Hangar.TrySpawnAircraft charges one airframe when the player argument is null,
            // on top of the source the purchase transaction already reserved.
            Assert.Equal(1, SupplyCompensation.Delta(before: 5, after: 4));
        }

        [Fact]
        public void NoDebitMeansNoCompensation()
        {
            // A refused spawn, or a carrier pad that abandoned the launch while its doors
            // opened, never charged anything.
            Assert.Equal(0, SupplyCompensation.Delta(before: 5, after: 5));
        }

        [Fact]
        public void StockRisingDuringTheCallIsNeverTreatedAsADebit()
        {
            // Something other than this delivery moved the count - a recovery settling in
            // the same frame, a mission trigger. Taking that away would invent a charge
            // nobody made.
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
