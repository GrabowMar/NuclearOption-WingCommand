using Xunit;

namespace WingCommand.Tests
{
    public class ChatterLogicTests
    {
        [Theory]
        [InlineData("M. Adeyemi", "cobalt", "M. \"COBALT\" ADEYEMI")]
        [InlineData("K. Lindqvist", "MERIDIAN", "K. \"MERIDIAN\" LINDQVIST")]
        [InlineData("Vasquez", "hatchet", "\"HATCHET\" VASQUEZ")]
        public void IdentityPlacesCallsignBetweenGivenNameAndSurname(
            string name, string callsign, string expected)
        {
            Assert.Equal(expected, ChatterDialogue.Identity(name, callsign));
        }

        [Fact]
        public void AcknowledgementsAreStableForASeed()
        {
            string first = ChatterDialogue.Acknowledge(
                ChatterPersona.Aggressive, "Engage", 17);
            string second = ChatterDialogue.Acknowledge(
                ChatterPersona.Aggressive, "Engage", 17);

            Assert.Equal(first, second);
        }

        [Fact]
        public void PersonasGiveTheSameOrderDifferentFlavour()
        {
            string professional = ChatterDialogue.Acknowledge(
                ChatterPersona.Professional, "Engage", 0);
            string aggressive = ChatterDialogue.Acknowledge(
                ChatterPersona.Aggressive, "Engage", 0);
            string calm = ChatterDialogue.Acknowledge(
                ChatterPersona.Calm, "Engage", 0);

            Assert.NotEqual(professional, aggressive);
            Assert.NotEqual(aggressive, calm);
        }

        [Fact]
        public void ContextualEventIncludesTargetName()
        {
            string line = ChatterDialogue.Event(
                ChatterPersona.Calm, "Engaging", "BDF-12", 0);

            Assert.Contains("BDF-12", line);
        }

        [Fact]
        public void AmbientDialogueIncludesAllTeasersVerbatim()
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < ChatterDialogue.AmbientCount; i++)
            {
                ChatterExchange exchange = ChatterDialogue.Ambient(i);
                lines.Add(exchange.Opening);
                if (exchange.Reply != null) lines.Add(exchange.Reply);
            }

            Assert.Contains("The fires are ravenging the forests", lines);
            Assert.Contains("Something big is coming", lines);
            Assert.Contains("I can feel the buildings shaking", lines);
            Assert.Contains("They say it will be hot summer", lines);
        }

        [Fact]
        public void LonePilotNeverGetsAnExchangeThatNeedsAReply()
        {
            for (int i = 0; i < ChatterDialogue.AmbientCount; i++)
                Assert.Null(ChatterDialogue.Ambient(i, repliesAllowed: false).Reply);
        }

        [Fact]
        public void PilotSpecificExchangeMatchesOnlyItsTaggedSpeaker()
        {
            ChatterExchange exchange = FindExchange("Clean picture. Let's keep it that way.");

            Assert.True(exchange.SpeakerMatches("cobalt"));
            Assert.False(exchange.SpeakerMatches("HATCHET"));
            Assert.True(exchange.ReplyMatches("anyone"));
        }

        [Fact]
        public void PilotPairCanRequireASpecificResponder()
        {
            ChatterExchange exchange = FindExchange(
                "Hatchet, your definition of close support concerns me.");

            Assert.True(exchange.SpeakerMatches("COBALT"));
            Assert.True(exchange.ReplyMatches("hatchet"));
            Assert.False(exchange.ReplyMatches("MERIDIAN"));
        }

        private static ChatterExchange FindExchange(string opening)
        {
            for (int i = 0; i < ChatterDialogue.AmbientCount; i++)
            {
                ChatterExchange exchange = ChatterDialogue.AmbientAt(i);
                if (exchange.Opening == opening) return exchange;
            }

            throw new Xunit.Sdk.XunitException("Exchange not found: " + opening);
        }
    }
}
