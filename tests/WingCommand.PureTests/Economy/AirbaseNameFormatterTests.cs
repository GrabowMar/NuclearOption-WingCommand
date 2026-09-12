using Xunit;

namespace WingCommand.PureTests
{
    public class AirbaseNameFormatterTests
    {
        [Theory]
        [InlineData("airbase_island5", "Island 5 Airfield")]
        [InlineData("airbase_island14", "Island 14 Airfield")]
        [InlineData("airbase_island1", "Island 1 Airfield")]
        [InlineData("airbase_NE", "Northeast Airbase")]
        [InlineData("airbase_SE", "Southeast Airbase")]
        [InlineData("airbase_NW", "Northwest Airbase")]
        [InlineData("airbase_SW", "Southwest Airbase")]
        [InlineData("airbase_Main", "Main Airbase")]
        [InlineData("AssaultCarrier1", "Assault Carrier 01")]
        [InlineData("Carrier2", "Carrier 02")]
        [InlineData("airbase_island5(Clone)", "Island 5 Airfield")]
        [InlineData("<UNIT_AIRBASE>++AssaultCarrier1", "Assault Carrier 01")]
        [InlineData("Opal Airport", "Opal Airport")]
        [InlineData("", "FIELD")]
        [InlineData(null, "FIELD")]
        public void FormatsRawNamesToMilitaryDesignations(string raw, string expected)
        {
            Assert.Equal(expected, AirbaseNameFormatter.Format(raw));
        }
    }
}
