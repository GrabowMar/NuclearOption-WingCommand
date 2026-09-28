using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class OrbitGeometryTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(90f)]
        [InlineData(225f)]
        public void HoldingSlotsTurnTheSameWayOnSeparateCircles(float degrees)
        {
            float x = (float)Math.Cos(degrees * Math.PI / 180) * 2000f;
            float z = (float)Math.Sin(degrees * Math.PI / 180) * 2000f;
            for (int slot = 1; slot <= 6; slot++)
            {
                var aim = OrbitGeometry.AimOffset(x, z, 2000f, slot, 200f);
                Assert.True(x * aim.z - z * aim.x > 0, "Every slot must use the same turn direction.");
                double radialProjection = (x * aim.x + z * aim.z) / 2000d;
                Assert.InRange(radialProjection, 2000 + (slot - 1) * 200 - 0.01,
                                                 2000 + (slot - 1) * 200 + 0.01);
            }
        }
    }
}
