using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class WingMapBadgeGeometryTests
    {
        [Theory]
        [InlineData(8f, 8f)]
        [InlineData(15f, 15f)]
        [InlineData(8f, 20f)]
        [InlineData(30f, 30f)]
        public void AllAircraftHeadingsHaveClearSpaceBetweenSilhouetteAndRing(float width, float height)
        {
            WingMapBadgeGeometry badge = WingMapBadgeGeometry.ForIcon(width, height);
            // Enclose every rotated bounding-box corner within the ring.
            float cornerRadius = (float)Math.Sqrt(width * width + height * height) / 2f;
            Assert.True(badge.InnerRadiusPixels - WingMapBadgeGeometry.FeatherPixels - cornerRadius >= 1.89f);
            Assert.True(badge.CommandHalfExtentPixels > badge.OuterRadiusPixels + 2f);
        }

        [Fact]
        public void StrokeStaysOnePixelWhenIconSizeChanges()
        {
            foreach (float iconSize in new[] { 5f, 10f, 15f, 25f, 45f })
            {
                WingMapBadgeGeometry badge = WingMapBadgeGeometry.ForIcon(iconSize, iconSize);
                Assert.Equal(1f, badge.OuterRadiusPixels - badge.InnerRadiusPixels, precision: 4);
            }
        }

        [Fact]
        public void MapZoomCompensationDoesNotChangeBadgeSize()
        {
            WingMapBadgeGeometry reference = WingMapBadgeGeometry.ForIcon(15f, 15f);
            foreach (float mapScale in new[] { 0.1f, 0.5f, 2f, 10f })
            {
                // Model native inverse zoom before screen projection.
                float projectedIconSize = 15f / mapScale * mapScale;
                WingMapBadgeGeometry badge = WingMapBadgeGeometry.ForIcon(projectedIconSize, projectedIconSize);
                Assert.Equal(reference.InnerRadiusPixels, badge.InnerRadiusPixels, precision: 4);
            }
        }

        [Fact]
        public void HiddenOrNotYetSizedIconProducesFiniteLegibleGeometry()
        {
            foreach (float size in new[] { 0f, float.NaN, float.PositiveInfinity })
            {
                WingMapBadgeGeometry badge = WingMapBadgeGeometry.ForIcon(size, size);
                Assert.Equal(8.5f, badge.InnerRadiusPixels);
                Assert.True(badge.CommandHalfExtentPixels < 15f);
            }
        }
    }
}
