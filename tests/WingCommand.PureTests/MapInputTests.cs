using Xunit;

namespace WingCommand.PureTests
{
    public class MapInputTests
    {
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
