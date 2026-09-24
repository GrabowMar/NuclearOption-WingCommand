using Xunit;

namespace WingCommand.PureTests
{
    public class RoomFooterTests
    {
        [Fact]
        public void AHoveredTooltipComesFirst()
        {
            // Review P5 I3: the room covers the bezel's status strip, so its footer says what the pointer is on.
            Assert.Equal("Loadout templates arrive in a later update.",
                RoomFooter.Text("Loadout templates arrive in a later update.", "Point 2 added", 1f, "Scroll to zoom"));
        }

        [Fact]
        public void ARecentToastComesNextThenTheHint()
        {
            Assert.Equal("Right-click an enemy on the map", RoomFooter.Text(null, "Right-click an enemy on the map", 2f, "Scroll to zoom"));
            Assert.Equal("Scroll to zoom", RoomFooter.Text("", "Right-click an enemy on the map", RoomFooter.ToastSeconds + 0.1f, "Scroll to zoom"));
            Assert.Equal("Scroll to zoom", RoomFooter.Text(null, null, 0f, "Scroll to zoom"));
            Assert.Equal("", RoomFooter.Text(null, null, 0f, null));
        }
    }
}
