using Xunit;

namespace WingCommand.PureTests
{
    public class RecruitWordsTests
    {
        [Fact]
        public void EveryoneJoined()
        {
            Assert.Equal("Wolf joins the wing", RecruitWords.Ack(1, 1, "Wolf", null));
            Assert.Equal("3 aircraft join the wing", RecruitWords.Ack(3, 3, "Wolf", null));
        }

        [Fact]
        public void SomeJoinedSaysHowManyAndWhyNotTheRest()
        {
            // Review P4 I3: four selected, one slot free: the answer says so.
            Assert.Equal("1 of 4 joined: the wing is full", RecruitWords.Ack(1, 4, "Wolf", "the wing is full"));
        }

        [Fact]
        public void NobodyJoinedIsARefusalWithTheReason()
        {
            Assert.Null(RecruitWords.Ack(0, 2, null, "the wing is full"));
            Assert.Equal("Cannot recruit: the wing is full", RecruitWords.Refusal("the wing is full"));
            Assert.Equal("No friendly aircraft to recruit", RecruitWords.Refusal(null));
        }
    }
}
