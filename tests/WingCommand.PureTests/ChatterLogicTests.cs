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
    }
}
