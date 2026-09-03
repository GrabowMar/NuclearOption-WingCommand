using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// Which aircraft a wingman formates on once the player has named a flight lead. The
    /// rule is deliberately tiny: a follower forms on the designated lead; the lead itself,
    /// and everyone when no lead is set, forms on the wing leader (the player).
    /// </summary>
    public class FlightLeadPolicyTests
    {
        [Fact]
        public void NoDesignatedLeadEveryoneFormsOnTheWingLeader()
        {
            Assert.Equal("player",
                FlightLeadPolicy.FormationLeader(isThisMemberTheLead: false,
                                                 designatedLead: null, wingLeader: "player"));
        }

        [Fact]
        public void AFollowerFormsOnTheDesignatedLead()
        {
            Assert.Equal("lead",
                FlightLeadPolicy.FormationLeader(isThisMemberTheLead: false,
                                                 designatedLead: "lead", wingLeader: "player"));
        }

        [Fact]
        public void TheLeadItselfStillFormsOnTheWingLeader()
        {
            Assert.Equal("player",
                FlightLeadPolicy.FormationLeader(isThisMemberTheLead: true,
                                                 designatedLead: "lead", wingLeader: "player"));
        }

        [Fact]
        public void ANullWingLeaderPassesThroughForTheLead()
        {
            Assert.Null(
                FlightLeadPolicy.FormationLeader<string>(isThisMemberTheLead: true,
                                                         designatedLead: "lead", wingLeader: null));
        }
    }
}
