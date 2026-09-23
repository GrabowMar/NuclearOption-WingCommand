using Xunit;

namespace WingCommand.PureTests
{
    public class RecoveryRulesTests
    {
        [Theory]
        [InlineData(1f, 1f, 30f)]
        [InlineData(0f, 0f, 150f)]
        [InlineData(0.5f, 0.5f, 90f)]
        [InlineData(2f, -1f, 90f)]   // out-of-range readings clamp
        public void RefitTimeGrowsWithTheFuelAndAmmunitionToReplace(float fuel, float ammo, float seconds) =>
            Assert.Equal(seconds, RefitTimer.Seconds(fuel, ammo), 3);

        [Fact]
        public void BingoTripsOnceWhenTheFuelCannotReachTheFieldPlusTheReserve()
        {
            // 0.001 of the tanks per second, 50 km out at 200 m/s: 250 s of flight + a 10% reserve = 0.35.
            var bingo = new BingoMonitor();
            int trips = 0;
            float trippedAt = float.NaN;
            float fuel = 0.8f;
            for (int i = 0; i < 700; i++)
            {
                if (bingo.Update(fuel, 50000f, 200f, 1f))
                {
                    trips++;
                    trippedAt = fuel;
                }
                fuel -= 0.001f;
            }
            Assert.Equal(1, trips);
            Assert.InRange(trippedAt, 0.34f, 0.36f);
            Assert.True(bingo.Bingo);
        }

        [Fact]
        public void BingoNeverTripsBeforeABurnRateIsKnown()
        {
            var bingo = new BingoMonitor();
            Assert.False(bingo.Update(0.05f, 100000f, 200f, 1f));
            Assert.False(bingo.Bingo);
        }

        [Fact]
        public void RefuellingClearsBingoSoItCanTripAgain()
        {
            var bingo = new BingoMonitor();
            float fuel = 0.4f;
            for (int i = 0; i < 100; i++, fuel -= 0.001f) bingo.Update(fuel, 50000f, 200f, 1f);
            Assert.True(bingo.Bingo);
            bingo.Update(1f, 50000f, 200f, 1f);   // refitted
            Assert.False(bingo.Bingo);
        }
    }
}
