using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class LoadoutSummaryTests
    {
        private static StoreFacts Store(StoreKind kind, float mass, int ammo) =>
            new StoreFacts { HasKey = true, Known = true, Kind = kind, Mass = mass, Ammo = ammo };

        [Fact]
        public void StationsCountRowsNotSets()
        {
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), new StoreFacts[6]);
            Assert.Equal(5, s.Stations);
            Assert.Equal(0, s.Fitted);
            Assert.Equal(0f, s.Mass);
        }

        [Fact]
        public void MassCountsEveryPylonOfAStation()
        {
            var facts = new StoreFacts[6];
            facts[0] = facts[1] = Store(StoreKind.AirToAir, 50f, 1);
            facts[2] = Store(StoreKind.AirToGround, 100f, 2);
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), facts);
            Assert.Equal(300f, s.Mass, 3);
            Assert.Equal(2, s.Fitted);
            Assert.Equal(2, s.Aam);
            Assert.Equal(4, s.Agm);
        }

        [Fact]
        public void ASetWithoutPylonsWeighsNothing()
        {
            var l = new StationLayout(new[] { "A" }, new[] { "" }, new[] { false }, new[] { 0 }, new[] { new int[0] });
            FitSummary s = LoadoutSummary.Of(l, new[] { Store(StoreKind.Bomb, 500f, 1) });
            Assert.Equal(0f, s.Mass);
            Assert.Equal(0, s.Bombs);
        }

        [Fact]
        public void AnUnknownStoreCountsAsEmpty()
        {
            var facts = new StoreFacts[6];
            facts[5] = new StoreFacts { HasKey = true, Known = false };
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), facts);
            Assert.Equal(1, s.Unknown);
            Assert.Equal(0, s.Fitted);
        }

        [Fact]
        public void AStoreTheLaunchClearsDoesNotFlyAndReadsBlocked()
        {
            var facts = new StoreFacts[6];
            facts[3] = Store(StoreKind.Other, 300f, 1);
            facts[4] = Store(StoreKind.Bomb, 200f, 1);
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), facts);
            Assert.Equal(1, s.Fitted);
            Assert.Equal(1, s.Blocked);
            Assert.Equal(200f, s.Mass, 3);
        }

        [Fact]
        public void AStoreTheMissionRefusesIsCountedApartAndWeighsNothing()
        {
            var facts = new StoreFacts[6];
            facts[2] = Store(StoreKind.Bomb, 100f, 1);
            facts[2].Refused = true;
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), facts);
            Assert.Equal(1, s.Refused);
            Assert.Equal(0, s.Fitted);
            Assert.Equal(0, s.Unknown);
            Assert.Equal(0f, s.Mass);
        }

        [Fact]
        public void CargoAndMissileDefenceAreCounted()
        {
            var facts = new StoreFacts[6];
            facts[2] = Store(StoreKind.MissileDefence, 10f, 10);
            facts[4] = Store(StoreKind.Cargo, 400f, 2);
            FitSummary s = LoadoutSummary.Of(StationLayoutTests.Vt7(), facts);
            Assert.Equal(20, s.MslDef);
            Assert.Equal(2, s.Cargo);
        }
    }
}
