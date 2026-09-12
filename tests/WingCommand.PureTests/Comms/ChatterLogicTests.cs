using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class ChatterLogicTests
    {
        [Theory]
        [InlineData("John Smith", "Maverick", "John \"MAVERICK\" SMITH")]
        [InlineData("Cher", "Ace", "\"ACE\" CHER")]
        [InlineData(null, null, "\"NO CALLSIGN\" UNKNOWN")]
        public void Identity_FormatsGivenNameCallsignSurname(string name, string callsign, string expected)
        {
            Assert.Equal(expected, ChatterDialogue.Identity(name, callsign));
        }

        [Fact]
        public void Ambient_WrapsIntoRange()
        {
            ChatterExchange first = ChatterDialogue.Ambient(seed: 0);
            ChatterExchange wrapped = ChatterDialogue.Ambient(seed: ChatterDialogue.AmbientCount);

            Assert.Equal(first.Opening, wrapped.Opening);
        }

        [Fact]
        public void EveryOrderHasARadioAcknowledgementLine()
        {
            foreach (WingOrder order in System.Enum.GetValues(typeof(WingOrder)))
            {
                string line = ChatterDialogue.Acknowledge(
                    ChatterPersona.Professional, order.ToString(), seed: 0);
                Assert.False(string.IsNullOrWhiteSpace(line));
            }
        }

        [Fact]
        public void RefitHasItsOwnRadioAcknowledgementLine()
        {
            string single = ChatterDialogue.Acknowledge(
                ChatterPersona.Professional, "REFIT", seed: 0);
            string group = ChatterDialogue.GroupAcknowledge(
                ChatterPersona.Professional, "REFIT", "Two", seed: 0);

            Assert.False(string.IsNullOrWhiteSpace(single));
            Assert.False(string.IsNullOrWhiteSpace(group));
            Assert.NotEqual(ChatterDialogue.Acknowledge(ChatterPersona.Professional, "UNKNOWN", seed: 0), single);
            Assert.NotEqual(ChatterDialogue.GroupAcknowledge(ChatterPersona.Professional, "UNKNOWN", "Two", seed: 0), group);
        }

        [Fact]
        public void AttackAndFireForEffectHaveSpecificAcknowledgements()
        {
            foreach (ChatterPersona persona in (ChatterPersona[])System.Enum.GetValues(typeof(ChatterPersona)))
            {
                string attack = ChatterDialogue.Acknowledge(persona, "ATTACK", seed: 1);
                string fireForEffect = ChatterDialogue.Acknowledge(persona, "FIREFOREFFECT", seed: 1);

                Assert.False(string.IsNullOrWhiteSpace(attack));
                Assert.False(string.IsNullOrWhiteSpace(fireForEffect));

                string fallback = ChatterDialogue.Acknowledge(persona, "UNKNOWN", seed: 1);
                Assert.NotEqual(fallback, attack);
                Assert.NotEqual(fallback, fireForEffect);
            }
        }

        [Fact]
        public void EveryFlightEventProducesValidLinesAcrossAllPersonas()
        {
            string[] eventNames =
            {
                "ENGAGING", "DEFENDING", "BREAKCALL", "SPLASH", "WINCHESTER", "BINGO",
                "REJOINING", "TAXIING", "DEPARTING", "AIRBORNE", "AIRBORNEREJOINING",
                "DETACHED", "FALLINGBACK", "HOLDING", "COVERING", "ORBITING", "DELIVERING",
                "DELIVERED", "NODROPOFF", "FIREFOREFFECT", "EXPENDED", "OUTOFAMMO", "DOWN",
                "UNABLE", "SLOWLEADER", "PANIC", "DEFENSIVECLEAR", "JAMMING", "JAMMINGOFF",
                "MANEUVERING", "MANEUVERDONE", "DAMAGED", "CRITICAL", "RECOVERED", "UNABLEORDER",
                "FOX1", "FOX2", "FOX3", "MAGNUM", "RIFLE",
            };

            foreach (ChatterPersona persona in (ChatterPersona[])System.Enum.GetValues(typeof(ChatterPersona)))
            {
                for (int i = 0; i < eventNames.Length; i++)
                {
                    string lineWithoutDetail = ChatterDialogue.Event(persona, eventNames[i], null, seed: 42);
                    Assert.False(string.IsNullOrWhiteSpace(lineWithoutDetail));

                    string lineWithDetail = ChatterDialogue.Event(persona, eventNames[i], "Target-Alpha", seed: 42);
                    Assert.False(string.IsNullOrWhiteSpace(lineWithDetail));
                }
            }
        }

        [Fact]
        public void SoloAmbientModeReturnsExchangeWithNullReply()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                ChatterExchange exchange = ChatterDialogue.Ambient(seed, repliesAllowed: false);
                Assert.False(string.IsNullOrWhiteSpace(exchange.Opening));
                Assert.Null(exchange.Reply);
            }
        }

        [Fact]
        public void GroupAcknowledgeProducesValidCallsForAllOrders()
        {
            foreach (ChatterPersona persona in (ChatterPersona[])System.Enum.GetValues(typeof(ChatterPersona)))
            {
                foreach (WingOrder order in (WingOrder[])System.Enum.GetValues(typeof(WingOrder)))
                {
                    string groupLine = ChatterDialogue.GroupAcknowledge(persona, order.ToString(), "Two and Three", seed: 5);
                    Assert.False(string.IsNullOrWhiteSpace(groupLine));
                    Assert.Contains("Two and Three", groupLine);
                }
            }
        }
    }
}
