using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class AceIngressTests
    {
        [Theory]
        [InlineData(1, "T/A-30")]
        [InlineData(2, "CT-7")]
        [InlineData(3, "FS-12")]
        [InlineData(4, "FS-20")]
        [InlineData(5, "KR-67")]
        public void TierHasExactAirframe(int tier, string name) => Assert.Equal(name, AceIngress.Airframe(tier));

        [Fact]
        public void PicksEnemySideOfPerimeterWithFormationAndPlayerClearance()
        {
            var bases = new[] { new AceIngress.Base(-30000, 0, true), new AceIngress.Base(30000, 0, false) };
            for (int seed = -64; seed <= 64; seed++)
            {
                Assert.True(AceIngress.TryPick(100000, 60000, 0, 0, bases, seed, out float x, out float z));
                Assert.True(x < -1000);
                Assert.True(Math.Abs(x) == 48000 || Math.Abs(z) == 28000);
                Assert.True(x * x + z * z >= 9000 * 9000);
                Assert.InRange(x, -48000, 48000);
                Assert.InRange(z, -28000, 28000);
            }
        }

        [Fact]
        public void CapturedOrMissingEnemyBasesCannotSpawnAFlight()
        {
            var bases = new[] { new AceIngress.Base(-30000, 0, false) };
            Assert.False(AceIngress.TryPick(100000, 60000, 0, 0, bases, 1, out _, out _));
            Assert.False(AceIngress.TryPick(100000, 60000, 0, 0, Array.Empty<AceIngress.Base>(), 1, out _, out _));
        }

        [Fact]
        public void RefusesInvalidMapsAndOverlappingControl()
        {
            var bases = new[] { new AceIngress.Base(0, 0, true), new AceIngress.Base(0, 0, false) };
            Assert.False(AceIngress.TryPick(100000, 60000, 0, 0, bases, 1, out _, out _));
            Assert.False(AceIngress.TryPick(float.NaN, 60000, 0, 0, bases, 1, out _, out _));
            Assert.False(AceIngress.TryPick(4000, 4000, 0, 0, bases, 1, out _, out _));
            Assert.Null(AceIngress.Airframe(0));
            Assert.Null(AceIngress.Airframe(6));
        }
    }
}
