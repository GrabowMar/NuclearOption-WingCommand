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

            // A mouse-bound Rewired Select fires first, then the EventSystem on release.
            Dispatch(controller: true);
            Assert.True(selection.IsAll);
            Dispatch(controller: false);
            Assert.Equal(new[] { member }, selection.Snapshot(wing));

            // A second intentional click still deselects; no timer swallows it.
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
        [InlineData(false, true)]  // Keyboard/controller over an icon, without a mouse gesture.
        [InlineData(true, false)]  // Nearest-icon controller selection on an empty map point.
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
