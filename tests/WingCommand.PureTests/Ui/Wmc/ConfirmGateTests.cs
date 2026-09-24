using Xunit;

namespace WingCommand.PureTests
{
    public class ConfirmGateTests
    {
        [Fact]
        public void TheSecondPressOnTheSameTargetConfirms()
        {
            var g = new ConfirmGate();
            Assert.False(g.Press("#3", 10f));
            Assert.True(g.Press("#3", 12f));
            Assert.False(g.Press("#3", 12.5f));   // a confirmed gate starts over
        }

        [Fact]
        public void AnotherTargetArmsAgain()
        {
            // Review P3 I4: asked about #3, pressed again for #2 #4 - that is a new question, not a yes.
            var g = new ConfirmGate();
            Assert.False(g.Press("#3", 10f));
            Assert.False(g.Press("#2 #4", 11f));
            Assert.True(g.Press("#2 #4", 12f));
        }

        [Fact]
        public void ALatePressArmsAgain()
        {
            var g = new ConfirmGate();
            Assert.False(g.Press("#3", 10f));
            Assert.False(g.Press("#3", 10f + ConfirmGate.Window + 0.1f));
            Assert.True(g.Press("#3", 11f + ConfirmGate.Window));
        }

        [Fact]
        public void IsArmedOnlyForTheAskedTargetInsideTheWindow()
        {
            var g = new ConfirmGate();
            Assert.False(g.IsArmed("#3", 10f));
            g.Press("#3", 10f);
            Assert.True(g.IsArmed("#3", 12f));
            Assert.False(g.IsArmed("#2", 12f));
            Assert.False(g.IsArmed("#3", 13.5f));
        }
    }
}
