using Xunit;

namespace WingCommand.PureTests
{
    public class TacticalPauseTests
    {
        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(0.5f, 1f)]
        [InlineData(0.25f, 2f)]
        public void ClosingOrDisablingThePanel_RestoresTheSpeedItReplaced(float slowdown, float original)
        {
            var pause = new TacticalPauseState();
            float speed = pause.Update(true, original, slowdown);
            Assert.Equal(slowdown, speed);
            Assert.Equal(original, pause.Update(false, speed, slowdown));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(2f)]
        public void AnotherTimeController_KeepsItsSpeedWhenWmcCloses(float externalSpeed)
        {
            var pause = new TacticalPauseState();
            Assert.Equal(0.25f, pause.Update(true, 1f, 0.25f));
            Assert.Equal(externalSpeed, pause.Update(true, externalSpeed, 0.25f));
            Assert.Equal(externalSpeed, pause.Update(true, externalSpeed, 0.25f));
            Assert.Equal(externalSpeed, pause.Update(false, externalSpeed, 0.25f));
        }

        [Fact]
        public void OpeningDuringNativePause_DoesNotResumeTheGame()
        {
            var pause = new TacticalPauseState();
            Assert.Equal(0f, pause.Update(true, 0f, 0.25f));
            Assert.Equal(0f, pause.Update(false, 0f, 0.25f));
        }

        [Fact]
        public void LiveScaleChanges_KeepTheOriginalSpeedForRestoration()
        {
            var pause = new TacticalPauseState();
            float speed = pause.Update(true, 1f, 0.25f);
            speed = pause.Update(true, speed, 0f);
            Assert.Equal(0f, speed);
            speed = pause.Update(false, speed, 0f);
            Assert.Equal(1f, speed);
            Assert.Equal(0.5f, pause.Update(true, speed, 0.5f));
        }
    }
}
