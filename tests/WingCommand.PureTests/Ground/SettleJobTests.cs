using Xunit;

namespace WingCommand.PureTests
{
    public class SettleJobTests
    {
        private const float Dt = 0.1f;

        private static SettleAction Run(SettleJob job, SettlePhase phase, float seconds, bool rescued = false)
        {
            SettleAction all = SettleAction.None;
            for (float t = 0f; t < seconds - 1e-4f; t += Dt) all |= job.Step(phase, Dt, rescued);
            return all;
        }

        [Fact]
        public void HoldNeverActs()
        {
            var job = new SettleJob(SettleTask.Hold);
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Down, 600f));
        }

        [Fact]
        public void NothingBeforeTouchdown()
        {
            var job = new SettleJob(SettleTask.Cargo);
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Approach, 30f));
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Descend, 30f));
            Assert.Equal(0f, job.DownTotal);
        }

        [Fact]
        public void CargoFiresAtTheDelayThenTakesOff()
        {
            var job = new SettleJob(SettleTask.Cargo);
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Down, SettleJob.CargoDelay - 0.2f));
            Assert.Equal(SettleAction.FireCargo, Run(job, SettlePhase.Down, 0.4f));
            Assert.True(job.Fired);
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Down, SettleJob.CargoWait - 0.5f));
            Assert.Equal(SettleAction.TakeOff, Run(job, SettlePhase.Down, 0.5f));
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Down, 10f));      // once
        }

        [Fact]
        public void CargoFiresOnceAcrossABounce()
        {
            var job = new SettleJob(SettleTask.Cargo);
            Assert.Equal(SettleAction.FireCargo, Run(job, SettlePhase.Down, SettleJob.CargoDelay + 0.2f));
            Assert.Equal(SettleAction.None, Run(job, SettlePhase.Descend, 5f));     // bounced
            SettleAction after = Run(job, SettlePhase.Down, SettleJob.CargoWait);
            Assert.Equal(SettleAction.TakeOff, after);                              // no second fire; the wait carried on
        }

        [Fact]
        public void RescueTakesOffWhenRescuedOrAfterTheWait()
        {
            var rescued = new SettleJob(SettleTask.Rescue);
            Assert.Equal(SettleAction.None, Run(rescued, SettlePhase.Down, 10f));
            Assert.Equal(SettleAction.TakeOff, Run(rescued, SettlePhase.Down, 0.1f, rescued: true));

            var waited = new SettleJob(SettleTask.Rescue);
            Assert.Equal(SettleAction.None, Run(waited, SettlePhase.Down, SettleJob.RescueWait - 0.5f));
            Assert.Equal(SettleAction.TakeOff, Run(waited, SettlePhase.Down, 0.6f));
            Assert.False(waited.Fired);
        }
    }
}
