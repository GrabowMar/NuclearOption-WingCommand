using Xunit;

namespace WingCommand.PureTests
{
    public class VoicePackIndexTests
    {
        [Fact]
        public void FileNamesMapToEventsByToken()
        {
            var idx = new VoicePackIndex();
            idx.Add(0, "fireFox3_02");
            idx.Add(1, "killAircraft");
            Assert.Equal(new[] { 0 }, idx.Clips("fireFox3"));
            Assert.Equal(new[] { 1 }, idx.Clips("killAircraft"));
            Assert.Empty(idx.Clips("fuelLow"));
        }

        [Fact]
        public void DigitTokensAreIgnoredAndCaseDoesNotMatter()
        {
            var idx = new VoicePackIndex();
            idx.Add(0, "KillAircraft-03");
            idx.Add(1, "12345");
            idx.Add(2, "FUELLOW 7");
            Assert.Equal(new[] { 0 }, idx.Clips("killaircraft"));
            Assert.Equal(new[] { 2 }, idx.Clips("fuelLow"));
            Assert.Equal(2, idx.Count);
        }

        [Fact]
        public void YappinatorAliasesCount()
        {
            var idx = new VoicePackIndex();
            idx.Add(0, "fox3 01");
            idx.Add(1, "missile_a");
            idx.Add(2, "kill");
            Assert.Equal(new[] { 0 }, idx.Clips("fireFox3"));
            Assert.Equal(new[] { 1 }, idx.Clips("fireMissile"));
            Assert.Equal(new[] { 2 }, idx.Clips("killGeneric"));
        }

        [Fact]
        public void AFileServesEveryEventItNames()
        {
            var idx = new VoicePackIndex();
            idx.Add(4, "spawn_fuelLow");
            Assert.Equal(new[] { 4 }, idx.Clips("Spawn"));
            Assert.Equal(new[] { 4 }, idx.Clips("fuelLow"));
        }

        [Fact]
        public void CallsMapToTheirEventsInOrder()
        {
            Assert.Equal(new[] { "fireFox3", "fireMissile" }, VoicePackIndex.EventsFor("FOX3"));
            Assert.Equal(new[] { "killAircraft", "killGeneric" }, VoicePackIndex.EventsFor("SPLASH"));
            Assert.Equal(new[] { "fuelLow" }, VoicePackIndex.EventsFor("JOKER"));
            Assert.Equal(new[] { "RwrOnFox3", "RwrOn" }, VoicePackIndex.EventsFor("PANIC"));
        }

        [Fact]
        public void ACallWithNoMappedEventHasNone() => Assert.Empty(VoicePackIndex.EventsFor("ENGAGING"));

        [Theory]
        [InlineData(2, 2, 0)]
        [InlineData(3, 2, 1)]
        [InlineData(4, 2, 0)]
        [InlineData(3, 0, -1)]
        public void PacksAreDealtRoundRobinByWingmanNumber(int number, int packs, int expected) =>
            Assert.Equal(expected, VoicePackIndex.PackFor(number, packs));
    }
}
