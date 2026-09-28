using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Review R5: what a store is for LOADOUT's summary and ROLE. A gun (internal cannon, gun pod, turret) is rated against air and
    /// ground like a missile, but its hundreds of rounds must never count as AAM or AGM.</summary>
    public class StoreKindTests
    {
        [Fact]
        public void AGunIsNeitherAirToAirNorAirToGround()
        {
            Assert.Equal(StoreKind.Other, LoadoutSummary.KindOf(cargo: false, jammer: false, gun: true, bomb: false, antiAir: 0.8f, antiSurface: 0.3f, antiMissile: 0f));
            Assert.Equal(StoreKind.Other, LoadoutSummary.KindOf(false, false, true, false, 0.1f, 0.9f, 0f));
        }

        [Fact]
        public void TheOtherKindsKeepTheirOrder()
        {
            Assert.Equal(StoreKind.Cargo, LoadoutSummary.KindOf(true, true, true, true, 1f, 1f, 1f));
            Assert.Equal(StoreKind.Ecm, LoadoutSummary.KindOf(false, true, false, false, 0f, 0f, 0f));
            Assert.Equal(StoreKind.MissileDefence, LoadoutSummary.KindOf(false, false, false, false, 0.2f, 0.1f, 0.9f));
            Assert.Equal(StoreKind.Bomb, LoadoutSummary.KindOf(false, false, false, true, 0f, 0.9f, 0f));
            Assert.Equal(StoreKind.AirToAir, LoadoutSummary.KindOf(false, false, false, false, 0.9f, 0.4f, 0f));
            Assert.Equal(StoreKind.AirToGround, LoadoutSummary.KindOf(false, false, false, false, 0.2f, 0.4f, 0f));
            Assert.Equal(StoreKind.Other, LoadoutSummary.KindOf(false, false, false, false, 0f, 0f, 0f));
        }

        [Fact]
        public void AGunsRoundsStayOutOfTheRoleCounts()
        {
            var layout = new StationLayout(new[] { "Gun", "Wing" }, new string[2], new bool[2], new[] { 1, 2 }, new int[2][]);
            var facts = new[]
            {
                new StoreFacts { HasKey = true, Known = true, Kind = LoadoutSummary.KindOf(false, false, true, false, 0.8f, 0.3f, 0f), Mass = 100f, Ammo = 500 },
                new StoreFacts { HasKey = true, Known = true, Kind = StoreKind.AirToGround, Mass = 300f, Ammo = 2 },
            };
            FitSummary f = LoadoutSummary.Of(layout, facts);
            Assert.Equal(0, f.Aam);
            Assert.Equal(4, f.Agm);
        }
    }
}
