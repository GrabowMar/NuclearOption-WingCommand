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

        [Fact]
        public void PointOrderOwnsHeldClickAndItsReleaseInEitherUpdateOrder()
        {
            var gesture = new MapPointGesture();
            gesture.Consume(frame: 10, leftButtonHeld: true);
            for (int frame = 10; frame <= 15; frame++)
            {
                gesture.Update(frame, leftButtonHeld: true);
                Assert.True(gesture.ConsumesClick(frame));
            }

            // EventSystem may deliver release before the manager gets its Update.
            Assert.True(gesture.ConsumesClick(16));
            gesture.Update(frame: 16, leftButtonHeld: false);
            // Or after it. The release frame is consumed in either case.
            Assert.True(gesture.ConsumesClick(16));
            gesture.Update(frame: 17, leftButtonHeld: false);
            Assert.False(gesture.ConsumesClick(17));
        }

        [Fact]
        public void CancelGestureWithoutAHeldMouseDoesNotBlockTheNextClick()
        {
            var gesture = new MapPointGesture();
            gesture.Consume(frame: 20, leftButtonHeld: false);
            Assert.True(gesture.ConsumesClick(20));
            gesture.Update(frame: 21, leftButtonHeld: false);
            Assert.False(gesture.ConsumesClick(21));
        }

        [Fact]
        public void ClosingOrDisablingMapClearsHeldGestureOwnership()
        {
            var gesture = new MapPointGesture();
            gesture.Consume(frame: 30, leftButtonHeld: true);
            gesture.Reset();
            Assert.False(gesture.ConsumesClick(30));
            gesture.Update(frame: 31, leftButtonHeld: false);
            Assert.False(gesture.ConsumesClick(31));
        }
    }
}
