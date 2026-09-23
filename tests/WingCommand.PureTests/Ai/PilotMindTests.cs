using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class PilotMindTests
    {
        private const float Dt = 1f / 60f;

        private static MindInput Nominal(float sigma = 0f, float error = 500f) => new MindInput
        {
            Sigma = sigma, SlotError = error, Spacing = 80f, LeaderSpeed = 200f, LoadedMinimum = 72f, LeaderFlying = true,
        };

        private static float RunUntilTransition(PilotMind mind, MindInput m, float maxSeconds, out TransitionReason reason)
        {
            reason = TransitionReason.None;
            for (int i = 1; i <= (int)Math.Round(maxSeconds / Dt); i++)
                if (mind.Tick(m, Dt, out _, out reason)) return i * Dt;
            return float.NaN;
        }

        [Fact]
        public void StartsInRejoinAndCapturesAfterSigmaHoldsOneForThreeSeconds()
        {
            var mind = new PilotMind();
            Assert.Equal(BehaviourId.Rejoin, mind.Current);
            float at = RunUntilTransition(mind, Nominal(sigma: 1f, error: 5f), 10f, out TransitionReason reason);
            Assert.InRange(at, 3f, 3.25f);
            Assert.Equal(BehaviourId.StationKeep, mind.Current);
            Assert.Equal(TransitionReason.Captured, reason);
        }

        [Fact]
        public void LostSlotReturnsToRejoinOnlyAfterTwoSecondsOutside()
        {
            var mind = new PilotMind();
            RunUntilTransition(mind, Nominal(sigma: 1f, error: 5f), 10f, out _);
            for (int i = 0; i < 180; i++) mind.Tick(Nominal(sigma: 1f, error: 5f), Dt, out _, out _);
            float at = RunUntilTransition(mind, Nominal(sigma: 0f, error: 300f), 10f, out TransitionReason reason);
            Assert.InRange(at, 2f, 2.25f);
            Assert.Equal(BehaviourId.Rejoin, mind.Current);
            Assert.Equal(TransitionReason.LostSlot, reason);
        }

        [Fact]
        public void NoTransitionBeforeTheMinimumDwell()
        {
            var mind = new PilotMind();
            RunUntilTransition(mind, Nominal(sigma: 1f, error: 5f), 10f, out _);   // StationKeep; dwell restarts
            MindInput grounded = Nominal(sigma: 1f, error: 5f);
            grounded.LeaderFlying = false;                                          // no persistence of its own
            float at = RunUntilTransition(mind, grounded, 10f, out TransitionReason reason);
            Assert.InRange(at, PilotMind.MinDwell, PilotMind.MinDwell + 0.25f);
            Assert.Equal(TransitionReason.LeaderNotFlying, reason);
        }

        [Fact]
        public void LeaderLossSwitchesToHoldImmediately()
        {
            var mind = new PilotMind();
            mind.Tick(Nominal(), Dt, out _, out _);
            MindInput lost = Nominal();
            lost.LeaderLost = true;
            Assert.True(mind.Tick(lost, Dt, out BehaviourId from, out TransitionReason reason));
            Assert.Equal(BehaviourId.Rejoin, from);
            Assert.Equal(BehaviourId.HoldOverhead, mind.Current);
            Assert.Equal(TransitionReason.LeaderLost, reason);
        }

        [Fact]
        public void SlowLeaderSendsTheMemberToHoldAndAFastOneBringsItBack()
        {
            var mind = new PilotMind();
            MindInput slow = Nominal();
            slow.LeaderSpeed = 80f;    // below loaded minimum 72 + 15
            Assert.InRange(RunUntilTransition(mind, slow, 10f, out TransitionReason why), 3f, 3.25f);
            Assert.Equal(TransitionReason.LeaderSlow, why);
            MindInput fast = Nominal();
            fast.LeaderSpeed = 110f;   // above 72 + 30
            Assert.InRange(RunUntilTransition(mind, fast, 10f, out why), 3f, 3.25f);
            Assert.Equal(BehaviourId.Rejoin, mind.Current);
            Assert.Equal(TransitionReason.LeaderRecovered, why);
        }
    }
}
