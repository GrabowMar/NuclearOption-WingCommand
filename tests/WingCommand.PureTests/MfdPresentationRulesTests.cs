using Xunit;

namespace WingCommand.PureTests
{
    public class MfdPresentationRulesTests
    {
        [Theory]
        [InlineData(0f, 600f)]
        [InlineData(float.NaN, 600f)]
        [InlineData(400f, float.PositiveInfinity)]
        public void InvalidDimensions_DoNotProduceAnInteractivePanel(float width, float height)
        {
            Assert.Equal(0f, MfdPresentationRules.FitScale(width, height, 300f, 800f));
        }

        [Fact]
        public void SpectatorControls_ConstrainSizeWithoutMovingTheMapCenter()
        {
            var fit = MfdPresentationRules.FitBesideBezel(400f, 800f, true,
                -900f, 900f, -280f, 400f, -450f, -400f, 10f, 0f);
            Assert.Equal(0f, fit.Top - 800f * fit.Scale / 2f);
            Assert.True(fit.Top - 800f * fit.Scale >= -280f);
        }

        [Theory]
        [InlineData(true, -600f, -550f)]
        [InlineData(false, 550f, 600f)]
        public void StandalonePanel_FitsOutsideBezelWithoutCrossingViewport(
            bool left, float bezelLeft, float bezelRight)
        {
            var fit = MfdPresentationRules.FitBesideBezel(400f, 600f, left,
                -900f, 900f, -400f, 400f, bezelLeft, bezelRight, 10f);
            Assert.InRange(fit.Scale, 0.01f, 1f);
            Assert.InRange(fit.X, -900f, 900f - 400f * fit.Scale);
            Assert.InRange(fit.Top - 600f * fit.Scale, -400f, 400f);
            Assert.InRange(fit.Top, -400f, 400f);
            if (left) Assert.True(fit.X + 400f * fit.Scale <= bezelLeft - 10f);
            else Assert.True(fit.X >= bezelRight + 10f);
        }
    }
}
