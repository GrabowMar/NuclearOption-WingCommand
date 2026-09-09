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
        public void MapHitFilteringPreservesForegroundUi(int sort, int render,
            int mapSort, int mapRender, bool behind)
        {
            Assert.Equal(behind, MapSelectionPolicy.IsBehindMap(sort, render, mapSort, mapRender));
        }

        [Fact]
        public void MousePressAndReleaseToggleSelectionOnlyOncePerPhysicalClick()
        {
            var member = new WingMember();
            var other = new WingMember();
            var wing = new WingRegistry();
            wing.Members.AddRange(new[] { member, other });
            var selection = new WingCommandSelection();

            // Rewired mouse-bound selection precedes EventSystem release.
            Dispatch(controller: true);
            Assert.True(selection.IsAll);
            Dispatch(controller: false);
            Assert.Equal(new[] { member }, selection.Snapshot(wing));

            // A separate click must deselect without timer suppression.
            Dispatch(controller: true);
            Assert.Equal(new[] { member }, selection.Snapshot(wing));
            Dispatch(controller: false);
            Assert.True(selection.IsNone);

            void Dispatch(bool controller)
            {
                if (!MapSelectionPolicy.DeferToMouseClick(controller, mouseGestureActive: true,
                                                          pointerOverIcon: true))
                    selection.ClickMember(member, toggle: false, wing);
            }
        }

        [Theory]
        [InlineData(false, true)]  // Controller/keyboard selection without a mouse gesture.
        [InlineData(true, false)]  // Native nearest-icon selection from an empty map point.
        [InlineData(false, false)]
        public void ControllerSelectionRemainsAvailableWithoutAnEventSystemIconClick(
            bool mouseGestureActive, bool pointerOverIcon)
        {
            var member = new WingMember();
            var wing = new WingRegistry();
            wing.Members.Add(member);
            var selection = new WingCommandSelection();
            selection.DeselectAll();

            if (!MapSelectionPolicy.DeferToMouseClick(controllerSource: true, mouseGestureActive,
                                                      pointerOverIcon))
                selection.ClickMember(member, toggle: false, wing);

            Assert.Equal(new[] { member }, selection.Snapshot(wing));
        }

    }
}
