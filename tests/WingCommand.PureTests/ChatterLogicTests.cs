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
        public void GroupAcknowledgementNamesTheOtherAircraftOnce()
        {
            string line = ChatterDialogue.GroupAcknowledge(
                ChatterPersona.Professional, "Engage", "Three and Four", 0);

            Assert.Contains("Three and Four", line);
            Assert.Contains("going in", line.ToLowerInvariant());
        }

        [Theory]
        [InlineData("OrbitHere", "station")]
        [InlineData("DeliverCargo", "cargo")]
        [InlineData("LandHere", "landing")]
        [InlineData("MoveToPoint", "waypoint")]
        public void GroupAcknowledgementsDescribeSupportOrders(string order, string expected)
        {
            string line = ChatterDialogue.GroupAcknowledge(
                ChatterPersona.Professional, order, "Three", 0);

            Assert.Contains("Three", line);
            Assert.Contains(expected, line.ToLowerInvariant());
        }

        [Theory]
        [InlineData("Splash")]
        [InlineData("Winchester")]
        [InlineData("Bingo")]
        public void CombatEventsCarryPilotFlavour(string eventName)
        {
            string professional = ChatterDialogue.Event(
                ChatterPersona.Professional, eventName, "BDF-12", 0);
            string dry = ChatterDialogue.Event(
                ChatterPersona.Dry, eventName, "BDF-12", 0);

            Assert.NotEqual(professional, dry);
        }

        [Theory]
        [InlineData("Damaged")]
        [InlineData("Critical")]
        [InlineData("PilotKilled")]
        [InlineData("Ejected")]
        [InlineData("AirframeLost")]
        public void NewCombatEventsHaveSpokenLines(string eventName)
        {
            string line = ChatterDialogue.Event(
                ChatterPersona.Calm, eventName, "COBALT", 0);

            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.NotEqual("Copy.", line);
        }
    }
}
