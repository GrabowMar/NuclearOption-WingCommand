using Xunit;

namespace WingCommand.PureTests
{
    public class CombatDoctrineTests
    {
        [Theory]
        [InlineData((int)TransitionReason.Winchester, (int)WinchesterAction.Rejoin, (int)BingoAction.Rtb, false, (int)RecoveryIntent.Rtb)]
        [InlineData((int)TransitionReason.Winchester, (int)WinchesterAction.Rtb, (int)BingoAction.Refit, true, (int)RecoveryIntent.Rtb)]
        [InlineData((int)TransitionReason.Winchester, (int)WinchesterAction.Refit, (int)BingoAction.Rtb, true, (int)RecoveryIntent.Refit)]
        [InlineData((int)TransitionReason.Fuel, (int)WinchesterAction.Rejoin, (int)BingoAction.Rtb, true, (int)RecoveryIntent.Rtb)]
        [InlineData((int)TransitionReason.Fuel, (int)WinchesterAction.Rejoin, (int)BingoAction.Refit, true, (int)RecoveryIntent.Refit)]
        [InlineData((int)TransitionReason.Leash, (int)WinchesterAction.Refit, (int)BingoAction.Refit, false, (int)RecoveryIntent.Rtb)]
        [InlineData((int)TransitionReason.Outnumbered, (int)WinchesterAction.Refit, (int)BingoAction.Refit, false, (int)RecoveryIntent.Rtb)]
        public void FollowOnsComeFromTheDoctrine(int reason, int w, int b, bool goes, int expected)
        {
            Assert.Equal(goes, CombatDoctrine.FollowOn((TransitionReason)reason, (WinchesterAction)w, (BingoAction)b, out RecoveryIntent intent));
            if (goes) Assert.Equal((RecoveryIntent)expected, intent);
        }

        [Fact]
        public void OutnumberedFallsBackOnlyAfterTheDwellAndNotWhenOff()
        {
            var j = new OutnumberedJudge();
            Assert.False(j.Update(4, 2, 2f, 4.9f));
            Assert.True(j.Update(4, 2, 2f, 0.2f));
            Assert.False(j.Update(3, 2, 2f, 10f));   // 3 < 2 × 2
            Assert.False(j.Update(9, 2, 0f, 10f));   // ratio 0: off
            Assert.False(OutnumberedJudge.Outnumbered(5, 0, 2f));
        }

        [Fact]
        public void EngageWhileOutnumberedNeedsASecondPressWithinTheWindow()
        {
            var j = new OutnumberedJudge();
            Assert.True(j.AllowEngage(2, 2, 2f, 0f));          // not outnumbered
            Assert.False(j.AllowEngage(6, 2, 2f, 1f));         // refused
            Assert.False(j.AllowEngage(6, 2, 2f, 12f));        // window passed: a fresh refusal
            Assert.True(j.AllowEngage(6, 2, 2f, 15f));         // confirmed
            Assert.True(j.Overridden);
            Assert.False(j.Update(6, 2, 2f, 30f));             // the override holds
            j.Reset();
            Assert.False(j.Overridden);
            Assert.False(j.AllowEngage(6, 2, 2f, 16f));        // reset forgets the refusal too
        }

        [Fact]
        public void SpreadPressureLowersSaturatedTargetsAndThePlayersTarget()
        {
            float free = TargetSpread.Score(1f, 0f, 1000f, 5000f, 0, false, 1);
            Assert.Equal(1f / 1000f, free, 6);
            Assert.True(TargetSpread.Score(1f, 0f, 1000f, 5000f, 1, false, 1) < free);           // one already on it
            Assert.Equal(free, TargetSpread.Score(1f, 0f, 1000f, 5000f, 1, false, 2), 6);         // room for two
            Assert.True(TargetSpread.Score(1f, 0f, 1000f, 5000f, 0, true, 2) < free);            // the player counts as two
            Assert.Equal(free / 2f, TargetSpread.Score(1f, 0f, 1000f, 500f, 0, false, 1), 6);     // beyond 1.2 × max range
            Assert.Equal(1f / 500f, TargetSpread.Score(1f, 0f, 100f, 5000f, 0, false, 1), 6);     // range floor
        }
    }
}
