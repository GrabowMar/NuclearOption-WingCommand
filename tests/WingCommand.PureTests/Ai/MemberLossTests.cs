using Xunit;

namespace WingCommand.PureTests
{
    public class MemberLossTests
    {
        [Fact]
        public void ReleasedAndReturnedHomeAreNotLosses()
        {
            Assert.Equal(TransitionReason.Released, MemberLoss.Of(true, false, true, true, true));
            // Returned to inventory disables the aircraft, but it came home (PilotFates checks home first too).
            Assert.Equal(TransitionReason.Released, MemberLoss.Of(false, true, true, false, false));
            Assert.False(MemberLoss.IsLoss(TransitionReason.Released));
        }

        [Fact]
        public void EjectedBeatsDisabledAndDeadOrDisabledIsKilled()
        {
            Assert.Equal(TransitionReason.Ejected, MemberLoss.Of(false, false, true, false, true));
            Assert.Equal(TransitionReason.Killed, MemberLoss.Of(false, false, true, true, false));
            Assert.Equal(TransitionReason.Killed, MemberLoss.Of(false, false, true, false, false));
            Assert.True(MemberLoss.IsLoss(TransitionReason.Killed));
            Assert.True(MemberLoss.IsLoss(TransitionReason.Ejected));
        }

        [Fact]
        public void NothingKnownIsGone()
        {
            Assert.Equal(TransitionReason.Gone, MemberLoss.Of(false, false, false, false, false));
            Assert.True(MemberLoss.IsLoss(TransitionReason.Gone));
        }
    }
}
