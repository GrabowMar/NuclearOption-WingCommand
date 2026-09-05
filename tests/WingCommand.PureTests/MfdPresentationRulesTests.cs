using Xunit;

namespace WingCommand.Tests
{
    public class MfdPresentationRulesTests
    {
        [Theory]
        [InlineData(true, 0f)]
        [InlineData(false, 0f)]
        [InlineData(true, -20f)]
        public void AsymmetricReservesDoNotShiftPanelAwayFromMapCenter(bool left, float mapCenter)
        {
            const float height = 1000f, bottom = -420f, top = 514f;
            var fit = MfdPresentationRules.FitBesideBezel(470, height, left,
                -952, 952, bottom, top, left ? -510 : 450, left ? -450 : 510, 8, mapCenter);
            Assert.InRange(fit.Scale, float.Epsilon, 1f);
            Assert.True(System.Math.Abs(fit.Top - height * fit.Scale * 0.5f - mapCenter) < 0.001f);
            Assert.True(fit.Top <= top + 0.001f);
            Assert.True(fit.Top - height * fit.Scale >= bottom - 0.001f);
        }

        [Theory]
        // Screenshot: 1784-wide viewport with the left bezel starting at x=317.
        [InlineData(true, -884f, 884f, -385f, 479f, -575f, -519f)]
        [InlineData(false, -884f, 884f, -385f, 479f, 384f, 440f)]
        [InlineData(true, -632f, 632f, -360f, 454f, -520f, -464f)]
        [InlineData(false, -1712f, 1712f, -600f, 694f, 480f, 536f)]
        public void SideBayFitKeepsTheWholePanelOnScreenAndClearOfBezel(
            bool left, float minX, float maxX, float bottom, float top, float bezelLeft, float bezelRight)
        {
            const float width = 470f, height = 1200f, gap = 8f;
            var fit = MfdPresentationRules.FitBesideBezel(width, height, left,
                minX, maxX, bottom, top, bezelLeft, bezelRight, gap);
            Assert.InRange(fit.Scale, float.Epsilon, 1f);
            Assert.True(fit.X >= minX - 0.001f);
            Assert.True(fit.X + width * fit.Scale <= maxX + 0.001f);
            Assert.True(fit.Top - height * fit.Scale >= bottom - 0.001f);
            Assert.True(fit.Top <= top + 0.001f);
            float spaceAbove = top - fit.Top;
            float spaceBelow = fit.Top - height * fit.Scale - bottom;
            Assert.True(System.Math.Abs(spaceAbove - spaceBelow) < 0.001f);
            if (left) Assert.True(fit.X + width * fit.Scale <= bezelLeft - gap + 0.001f);
            else Assert.True(fit.X >= bezelRight + gap - 0.001f);
        }

        [Fact]
        public void FullSideBayAllowsLargerPanelThanShortOptionsTemplate()
        {
            float oldScale = MfdPresentationRules.FitScale(470, 1200, 300, 600);
            var fit = MfdPresentationRules.FitBesideBezel(470, 1200, true,
                -884, 884, -385, 479, -575, -519, 8);
            Assert.True(fit.Scale > oldScale);
        }

        [Fact]
        public void MissingSideSpaceDoesNotProduceANegativeScale()
        {
            var fit = MfdPresentationRules.FitBesideBezel(470, 1200, true,
                -500, 500, -400, 400, -520, -464, 8);
            Assert.Equal(0f, fit.Scale);
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public void StandaloneAndDisabledLayoutsKeepVanillaBehavior(
            bool boscali, bool fit, bool available, bool expanded) =>
            Assert.Equal(expanded, MfdPresentationRules.UseExpanded(boscali, fit, available));

        [Theory]
        [InlineData(470f, 596f, 300f, 600f)]
        [InlineData(470f, 780f, 400f, 500f)]
        [InlineData(470f, 596f, 900f, 900f)]
        [InlineData(470f, 596f, 100f, 100f)]
        public void UniformFitStaysInsideBothBoundsWithoutEnlarging(
            float width, float height, float availableWidth, float availableHeight)
        {
            float scale = MfdPresentationRules.FitScale(width, height, availableWidth, availableHeight);
            Assert.InRange(scale, float.Epsilon, 1f);
            Assert.True(width * scale <= availableWidth + 0.001f);
            Assert.True(height * scale <= availableHeight + 0.001f);
            Assert.True(scale == 1f ||
                System.Math.Abs(width * scale - availableWidth) < 0.001f ||
                System.Math.Abs(height * scale - availableHeight) < 0.001f);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void InvalidDimensionsDeferFitting(float invalid)
        {
            Assert.Equal(0f, MfdPresentationRules.FitScale(invalid, 596, 300, 600));
            Assert.Equal(0f, MfdPresentationRules.FitScale(470, invalid, 300, 600));
            Assert.Equal(0f, MfdPresentationRules.FitScale(470, 596, invalid, 600));
            Assert.Equal(0f, MfdPresentationRules.FitScale(470, 596, 300, invalid));
        }
    }
}
