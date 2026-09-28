using Xunit;

namespace WingCommand.PureTests
{
    public class FormationPilotSeatTests
    {
        [Fact]
        public void TheSeatStartsAtTheSlotAndEventsCarryIt()
        {
            var p = new FormationPilot(2, AirframeClass.FixedWing);
            Assert.Equal(2, p.Seat);
            p.Seat = 5;
            var events = new WingEventRing();
            p.Log(events, 1f, WingEventKind.FallingBehind);
            Assert.Equal(5, events[0].Member);
        }
    }
}
