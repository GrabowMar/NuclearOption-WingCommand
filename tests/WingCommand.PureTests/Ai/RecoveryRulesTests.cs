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
        public void AfterburnerBurnIsNotLearnedAsTheRateHome()
        {
            // In game (wc-m5c-defend) a defensive break in afterburner raised the learned rate and called Joker and Bingo
            // a second apart 15 km from the field. Dry 0.001/s, then 10 s of afterburner at 0.005/s: the rate home stays
            // 0.001 (need 0.25, Joker below 0.40) and 0.449 is no Joker.
            var b = new BingoMonitor();
            b.Update(0.500f, 30000f, 200f, 1f);
            b.Update(0.499f, 30000f, 200f, 1f);
            float fuel = 0.499f;
            for (int i = 0; i < 10; i++)
            {
                fuel -= 0.005f;
                b.Update(fuel, 30000f, 200f, 1f, afterburner: true);
            }
            Assert.Equal(0.001f, b.BurnRate, 5);
            Assert.False(b.Joker);
        }

        [Fact]
        public void ARateLearnedOnlyInAfterburnerIsNoRate()
        {
            var b = new BingoMonitor();
            b.Update(0.90f, 30000f, 200f, 1f, afterburner: true);
            b.Update(0.88f, 30000f, 200f, 1f, afterburner: true);
            Assert.True(float.IsPositiveInfinity(b.SecondsToBingo));
            b.Update(0.879f, 30000f, 200f, 1f);
            Assert.Equal(0.001f, b.BurnRate, 5);
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

        [Fact]
        public void JokerTripsOnceBeforeBingoWithTheTimeLeft()
        {
            // burn 0.001/s; 30 km at 200 m/s = 150 s -> need 0.15 + 0.1 reserve = 0.25; Joker below 0.40
            var b = new BingoMonitor();
            b.Update(0.500f, 30000f, 200f, 1f);
            b.Update(0.499f, 30000f, 200f, 1f);
            Assert.Equal(249f, b.SecondsToBingo, 0);
            int jokers = 0;
            float jokerAt = 0f;
            for (float fuel = 0.498f; fuel > 0.2505f; fuel -= 0.001f)
            {
                Assert.False(b.Update(fuel, 30000f, 200f, 1f));
                if (b.JokerNow) { jokers++; jokerAt = fuel; }
            }
            Assert.Equal(1, jokers);
            Assert.InRange(jokerAt, 0.395f, 0.400f);
            Assert.True(b.Update(0.2495f, 30000f, 200f, 1f));
            Assert.False(b.JokerNow);
        }

        [Fact]
        public void FuelFallingPastJokerAndBingoAtOnceCallsBingoOnly()
        {
            var b = new BingoMonitor();
            b.Update(0.500f, 30000f, 200f, 1f);
            b.Update(0.499f, 30000f, 200f, 1f);
            Assert.True(b.Update(0.2f, 30000f, 200f, 1f));
            Assert.True(b.Joker);
            Assert.False(b.JokerNow);
        }

        [Fact]
        public void ARefuelClearsJoker()
        {
            var b = new BingoMonitor();
            b.Update(0.40f, 30000f, 200f, 1f);
            b.Update(0.39f, 30000f, 200f, 1f);   // rate 0.01 -> need 1.6: bingo at once
            Assert.True(b.Joker);
            b.Update(1f, 3000f, 200f, 1f);       // refuelled near the field
            Assert.False(b.Joker);
            Assert.False(b.Bingo);
        }
    }
}
