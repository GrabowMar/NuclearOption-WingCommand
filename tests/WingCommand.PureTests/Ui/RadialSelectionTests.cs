using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RadialSelectionTests
    {
        [Fact]
        public void WheelTracksEverySectorAndCancelsInCentreWithoutBoundaryFlicker()
        {
            for (int sector = 0; sector < 6; sector++)
            {
                double angle = sector * Math.PI / 3;
                Assert.Equal(sector, RadialSelection.FromPointer((float)Math.Sin(angle) * 140, (float)Math.Cos(angle) * 140, -1, 6));
            }
            Assert.Equal(-1, RadialSelection.FromPointer(0, 0, 3, 6));
            Assert.Equal(-1, RadialSelection.FromPointer(10, 20, 3, 6));
            Assert.Equal(0, AtAngle(32, 0));
            Assert.Equal(1, AtAngle(36, 0));
            Assert.Equal(0, AtAngle(328, 0));
            Assert.Equal(5, AtAngle(324, 0));
            Assert.Equal(0, AtAngle(359, -1));
            // Holding still never expires the selection.
            for (int frame = 0; frame < 1000; frame++) Assert.Equal(2, AtAngle(120, 2));
        }

        private static int AtAngle(double degrees, int previous) => RadialSelection.FromPointer(
            (float)Math.Sin(degrees * Math.PI / 180) * 140,
            (float)Math.Cos(degrees * Math.PI / 180) * 140, previous, 6);
    }
}
