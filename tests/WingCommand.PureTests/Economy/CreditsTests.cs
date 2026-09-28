using Xunit;

namespace WingCommand.PureTests
{
    public class CreditsTests
    {
        [Theory]
        [InlineData(87f, "87 CR")]
        [InlineData(9620f, "9,620 CR")]
        [InlineData(12.5f, "12.5 CR")]
        [InlineData(0f, "0 CR")]
        [InlineData(1250000f, "1,250,000 CR")]
        public void AnAmountReadsInGroupedCredits(float millions, string expected) => Assert.Equal(expected, Credits.Text(millions));

        [Fact]
        public void ANothingPriceReadsFree()
        {
            Assert.Equal("FREE", Credits.Price(0f));
            Assert.Equal("87 CR", Credits.Price(87f));
        }

        [Theory]
        [InlineData(9620f, "9,620")]
        [InlineData(999999f, "999,999")]
        [InlineData(1250000f, "1.3M")]
        [InlineData(100000000f, "100M")]
        [InlineData(0f, "0")]
        public void TheFundsTileNeverTakesMoreThanSevenCharacters(float millions, string expected)
        {
            Assert.Equal(expected, Credits.Short(millions));
            Assert.True(Credits.Short(millions).Length <= 7);
        }

        [Fact]
        public void CallCostSpeaksCreditsLikeTheFundsTile()
        {
            // Values and the allocation are the game's millions (UnitConverter.ValueReading); the panel reads CR.
            CallQuote poor = CallCost.Quote(87f, 20f, 4, false, true);
            Assert.Equal("needs 87 CR, the allocation holds 20 CR", poor.Reason);
            Assert.Equal("87 CR", CallCost.Money(87f));
        }
    }
}
