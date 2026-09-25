using Xunit;

namespace WingCommand.PureTests
{
    public class MapInputTests
    {
        [Theory]
        [InlineData(0, 1, 0, 2, true)]  // HUD exposed through transparent water.
        [InlineData(0, 9, 1, 2, true)]  // Canvas sorting takes precedence over render order.
        [InlineData(1, 0, 0, 2, false)] // Foreground WMC panel still blocks commands.
        [InlineData(0, 3, 0, 2, false)] // Later canvas at the same sorting priority.
        [InlineData(0, 2, 0, 2, false)] // Map icons remain eligible for hit resolution.
        public void MapHitFilteringPreservesForegroundUi(int sort, int render, int mapSort, int mapRender, bool behind)
        {
            Assert.Equal(behind, MapSelectionPolicy.IsBehindMap(sort, render, mapSort, mapRender));
        }

        [Theory]
        [InlineData(true, true, true, true)]    // Rewired's Select on press, the mouse click follows: wait for it
        [InlineData(true, false, true, false)]  // a controller with no mouse gesture selects at once
        [InlineData(false, true, true, false)]  // the mouse click itself selects
        [InlineData(true, true, false, false)]  // controller proximity pick away from the pointer selects
        public void OnePhysicalClickSelectsOnce(bool controller, bool mouse, bool overIcon, bool defer)
        {
            Assert.Equal(defer, MapSelectionPolicy.DeferToMouseClick(controller, mouse, overIcon));
        }
    }
}
