using Xunit;

namespace WingCommand
{
    public sealed class CargoProgressTests
    {
        [Fact]
        public void Every_partial_drop_restarts_the_no_progress_timeout()
        {
            var progress = new CargoProgressTracker();
            progress.Reset(amount: 3, now: 0f);

            Assert.False(progress.IsStalled(now: 44f, timeout: 45f));
            Assert.True(progress.Observe(amount: 2, now: 44f));
            Assert.False(progress.IsStalled(now: 88f, timeout: 45f));
            Assert.True(progress.Observe(amount: 1, now: 88f));
            Assert.False(progress.IsStalled(now: 132f, timeout: 45f));
            Assert.True(progress.IsStalled(now: 133f, timeout: 45f));
            Assert.True(progress.MadeProgress);
        }

        [Fact]
        public void Unchanged_or_increased_cargo_does_not_fake_progress()
        {
            var progress = new CargoProgressTracker();
            progress.Reset(amount: 2, now: 10f);

            Assert.False(progress.Observe(amount: 2, now: 30f));
            Assert.False(progress.Observe(amount: 3, now: 40f));
            Assert.True(progress.IsStalled(now: 55f, timeout: 45f));
            Assert.False(progress.MadeProgress);
        }
    }
}
