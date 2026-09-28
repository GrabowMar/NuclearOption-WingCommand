using Xunit;

namespace WingCommand.PureTests
{
    public class StoreRulesTests
    {
        private static MountFacts Ok() => new MountFacts { Known = true, OnStation = true, Pylons = 2, Ammo = 1 };

        private static MissionFacts Open() => new MissionFacts
        {
            EventContent = false, TacticalOpen = true, StrategicOpen = true, TacticalMinRank = 2, StrategicMinRank = 4, Rank = 5,
        };

        [Fact]
        public void AnOrdinaryStoreIsOk() => Assert.Equal(StoreVerdict.Ok, StoreRules.Check(Ok(), Open()));

        [Fact]
        public void ASwitchedOffStoreIsNeverOffered()
        {
            MountFacts m = Ok();
            m.Disabled = true;
            StoreVerdict v = StoreRules.Check(m, Open());
            Assert.Equal(StoreVerdict.Disabled, v);
            Assert.False(StoreRules.Offered(v));
        }

        [Fact]
        public void EventContentIsOfferedOnlyWhenTheMissionAllowsIt()
        {
            MountFacts m = Ok();
            m.EventContent = true;
            Assert.Equal(StoreVerdict.EventOnly, StoreRules.Check(m, Open()));
            MissionFacts events = Open();
            events.EventContent = true;
            Assert.Equal(StoreVerdict.Ok, StoreRules.Check(m, events));
        }

        [Fact]
        public void ARestrictedStoreIsListedButCannotBePicked()
        {
            MountFacts m = Ok();
            m.Restricted = true;
            StoreVerdict v = StoreRules.Check(m, Open());
            Assert.Equal(StoreVerdict.Restricted, v);
            Assert.True(StoreRules.Offered(v));
            Assert.False(StoreRules.Pickable(v));
        }

        [Fact]
        public void ANuclearStoreBeforeEscalationCanBeFittedButLaunchesEmpty()
        {
            MountFacts m = Ok();
            m.Nuclear = true;
            MissionFacts early = Open();
            early.TacticalOpen = false;
            StoreVerdict v = StoreRules.Check(m, early);
            Assert.Equal(StoreVerdict.NuclearNotYet, v);
            Assert.True(StoreRules.Pickable(v));
            Assert.False(StoreRules.Flies(v));
        }

        [Fact]
        public void ANuclearStoreBelowTheMissionRankNamesTheRank()
        {
            MountFacts m = Ok();
            m.Nuclear = true;
            MissionFacts low = Open();
            low.Rank = 1;
            Assert.Equal(StoreVerdict.NuclearRank, StoreRules.Check(m, low));
        }

        [Fact]
        public void AStrategicStoreNeedsTheStrategicThresholdAndItsRank()
        {
            MountFacts m = Ok();
            m.Nuclear = m.Strategic = true;
            MissionFacts f = Open();
            f.StrategicOpen = false;
            Assert.Equal(StoreVerdict.NuclearNotYet, StoreRules.Check(m, f));
            f.StrategicOpen = true;
            f.Rank = 3;
            Assert.Equal(StoreVerdict.NuclearRank, StoreRules.Check(m, f));
        }

        [Fact]
        public void TheFirstFailingRuleNamesTheReason()
        {
            MountFacts m = Ok();
            m.Known = false;
            m.Restricted = true;
            Assert.Equal(StoreVerdict.Missing, StoreRules.Check(m, Open()));
            m.Known = true;
            m.OnStation = false;
            Assert.Equal(StoreVerdict.NotOnStation, StoreRules.Check(m, Open()));
        }

        [Fact]
        public void AShipFieldRefusesShipRearmStoresOnly()
        {
            MountFacts m = Ok();
            m.ShipRearm = true;
            int warheads = 0;
            Assert.Equal(StoreVerdict.NotFromShip, StoreRules.AtField(m, Open(), true, ref warheads));
            Assert.Equal(StoreVerdict.Ok, StoreRules.AtField(m, Open(), false, ref warheads));
            Assert.Equal(StoreVerdict.Ok, StoreRules.AtField(Ok(), Open(), true, ref warheads));
        }

        [Fact]
        public void WarheadsAreCountedAcrossTheWholeFitInStationOrder()
        {
            MountFacts m = Ok();
            m.Nuclear = true;
            int warheads = 3;
            Assert.Equal(StoreVerdict.Ok, StoreRules.AtField(m, Open(), false, ref warheads));
            Assert.Equal(1, warheads);
            Assert.Equal(StoreVerdict.Warheads, StoreRules.AtField(m, Open(), false, ref warheads));
            Assert.Equal(1, warheads);
        }

        [Fact]
        public void AnEditTimeVerdictNeverFailsOnWarheadsOrShips()
        {
            MountFacts m = Ok();
            m.Nuclear = m.ShipRearm = true;
            Assert.Equal(StoreVerdict.Ok, StoreRules.Check(m, Open()));
        }
    }
}
