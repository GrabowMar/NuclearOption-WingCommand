using Xunit;

namespace WingCommand.PureTests
{
    public class WeaponsFilterTests
    {
        [Fact]
        public void GunsAndMissilesNeedAnAircraftCarryingOne()
        {
            // weapons-radar.md A: an empty filtered station list makes the game's AI fly home out of ammo, so the order is
            // refused for each aircraft in scope without one.
            Assert.Equal("carries no gun", WeaponsFilter.Refusal(WeaponsPolicy.Guns, anyGun: false, anyMissile: true));
            Assert.Equal("carries no missiles", WeaponsFilter.Refusal(WeaponsPolicy.Missiles, anyGun: true, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Guns, anyGun: true, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Missiles, anyGun: false, anyMissile: true));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Auto, anyGun: false, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.NoAirToGround, anyGun: false, anyMissile: false));
        }

        [Theory]
        // The store class, then whether AUTO, MISSILES, GUNS and NO A-G allow it.
        [InlineData((int)StoreClass.AirMissile, true, true, false, true)]
        [InlineData((int)StoreClass.StrikeMissile, true, true, false, true)]
        [InlineData((int)StoreClass.Gun, true, false, true, true)]
        [InlineData((int)StoreClass.Bomb, true, false, false, true)]
        [InlineData((int)StoreClass.Other, true, false, false, true)]
        [InlineData((int)StoreClass.Ecm, true, true, true, true)]
        public void EachPolicyAllowsItsStations(int store, bool auto, bool missiles, bool guns, bool noAg)
        {
            var c = (StoreClass)store;
            Assert.Equal(auto, WeaponsFilter.AllowsStation(WeaponsPolicy.Auto, c, sarh: false, radarWanted: true));
            Assert.Equal(missiles, WeaponsFilter.AllowsStation(WeaponsPolicy.Missiles, c, false, true));
            Assert.Equal(guns, WeaponsFilter.AllowsStation(WeaponsPolicy.Guns, c, false, true));
            Assert.Equal(noAg, WeaponsFilter.AllowsStation(WeaponsPolicy.NoAirToGround, c, false, true));
        }

        [Fact]
        public void ARadarGuidedMissileNeedsTheRadarWantedOnJammersNever()
        {
            // The game lets a SARH missile guide with the radar off: the rule is ours (weapons-radar.md B).
            foreach (WeaponsPolicy p in new[] { WeaponsPolicy.Auto, WeaponsPolicy.Missiles, WeaponsPolicy.NoAirToGround })
            {
                Assert.False(WeaponsFilter.AllowsStation(p, StoreClass.AirMissile, sarh: true, radarWanted: false));
                Assert.True(WeaponsFilter.AllowsStation(p, StoreClass.AirMissile, sarh: true, radarWanted: true));
            }
            Assert.True(WeaponsFilter.AllowsStation(WeaponsPolicy.Guns, StoreClass.Ecm, sarh: false, radarWanted: false));
        }

        [Fact]
        public void NoAirToGroundKeepsAirTargetsAndMissilesCountAsAir()
        {
            Assert.True(WeaponsFilter.AllowsTarget(WeaponsPolicy.NoAirToGround, air: true));
            Assert.False(WeaponsFilter.AllowsTarget(WeaponsPolicy.NoAirToGround, air: false));
            foreach (WeaponsPolicy p in new[] { WeaponsPolicy.Auto, WeaponsPolicy.Missiles, WeaponsPolicy.Guns })
                Assert.True(WeaponsFilter.AllowsTarget(p, air: false));
        }

        [Fact]
        public void OnlyARestrictedMemberGetsAFilteredList()
        {
            Assert.False(WeaponsFilter.Restricts(WeaponsPolicy.Auto, radarWanted: true));
            Assert.True(WeaponsFilter.Restricts(WeaponsPolicy.Auto, radarWanted: false));
            Assert.True(WeaponsFilter.Restricts(WeaponsPolicy.Guns, radarWanted: true));
        }
    }
}
