using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationLayoutTests
    {
        // FormationShape is internal, so Theory parameters take the underlying int instead
        // of exposing it in this public test method's signature.
        [Theory]
        [InlineData((int)FormationShape.FingerFour)]
        [InlineData((int)FormationShape.Diamond)]
        [InlineData((int)FormationShape.Trail)]
        public void Slot_LeaderIsAlwaysAtOrigin(int shape)
        {
            SlotLayout leader = FormationLayout.Slot((FormationShape)shape, 0);

            Assert.Equal(0f, leader.Lateral);
            Assert.Equal(0f, leader.Back);
            Assert.Equal(0f, leader.Height);
        }

        [Fact]
        public void Slot_WingmenSitBehindTheLeader()
        {
            SlotLayout wingman = FormationLayout.Slot(FormationShape.FingerFour, 1);

            Assert.True(wingman.Back > 0f);
        }
    }
}
