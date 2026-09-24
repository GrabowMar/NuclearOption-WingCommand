using Xunit;

namespace WingCommand.PureTests
{
    public class BezelLayoutTests
    {
        [Fact]
        public void BodyIsThePanelLessTheChrome()
        {
            Assert.Equal(636f, BezelLayout.Body(896f));
            Assert.Equal(336f, BezelLayout.Body(596f));
        }

        [Fact]
        public void TheListFitsThreeWingmenInTwoElementsOnATallDock()
        {
            float cap = BezelLayout.ListCap(BezelLayout.Body(896f));
            Assert.True(cap >= 2 * BezelLayout.HeaderPitch + 3 * BezelLayout.RowPitch);
            Assert.Equal(116f, BezelLayout.ListCap(BezelLayout.Body(596f)));
        }
    }
}
