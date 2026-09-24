using Xunit;

namespace WingCommand.PureTests
{
    public class WeaponsFilterTests
    {
        [Fact]
        public void GunsAndMissilesNeedSomeoneInScopeCarryingOne()
        {
            // weapons-radar.md A: an empty filtered station list makes the game's AI fly home out of ammo.
            Assert.Equal("nobody in scope carries a gun", WeaponsFilter.Refusal(WeaponsPolicy.Guns, anyGun: false, anyMissile: true));
            Assert.Equal("nobody in scope carries missiles", WeaponsFilter.Refusal(WeaponsPolicy.Missiles, anyGun: true, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Guns, anyGun: true, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Missiles, anyGun: false, anyMissile: true));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.Auto, anyGun: false, anyMissile: false));
            Assert.Null(WeaponsFilter.Refusal(WeaponsPolicy.NoAirToGround, anyGun: false, anyMissile: false));
        }
    }
}
