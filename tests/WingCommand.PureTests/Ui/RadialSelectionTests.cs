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

        [Fact]
        public void DeadzoneAndSectorGeometryCalculationsAreAccurate()
        {
            Assert.True(RadialSelection.IsInDeadzone(0f, 0f));
            Assert.True(RadialSelection.IsInDeadzone(30f, 40f)); // magnitude 50 < 64
            Assert.False(RadialSelection.IsInDeadzone(64f, 0f));
            Assert.False(RadialSelection.IsInDeadzone(50f, 50f)); // magnitude ~70.7 > 64

            Assert.Equal(0f, RadialSelection.SectorAngle(0, 6));
            Assert.Equal(60f, RadialSelection.SectorAngle(1, 6));
            Assert.Equal(180f, RadialSelection.SectorAngle(3, 6));

            Assert.Equal(58.8f, RadialSelection.SectorSweep(6, 1.2f), 1);
            Assert.Equal(0f - 29.4f, RadialSelection.SectorStartAngle(0, 6, 1.2f), 1);
            Assert.Equal(60f - 29.4f, RadialSelection.SectorStartAngle(1, 6, 1.2f), 1);
        }

        [Fact]
        public void StatusFormattingProducesReadableAvionicsText()
        {
            Assert.Equal("1 WINGMAN  •  ROE: TIGHT\nFORMATION: WEDGE",
                RadialSelection.FormatSquadronSubtitle(1, "TIGHT", "WEDGE"));
            Assert.Equal("3 WINGMEN  •  ROE: FREE\nFORMATION: COMBAT SPREAD",
                RadialSelection.FormatSquadronSubtitle(3, "FREE", "COMBAT SPREAD"));

            Assert.Equal("TARGET: T-90M",
                RadialSelection.FormatTargetSubtitle(true, "T-90M", "NO TARGET LOCKED"));
            Assert.Equal("NO TARGET LOCKED",
                RadialSelection.FormatTargetSubtitle(false, null, "NO TARGET LOCKED"));

            Assert.Equal("TIGHT  ▶  FREE",
                RadialSelection.FormatRoeTransition("TIGHT", "FREE"));

            Assert.Equal("MOVE TO SELECT  •  R-CLICK CANCEL",
                RadialSelection.FormatHint(inDeadzone: true, isAvailable: true));
            Assert.Equal("LOCK TARGET ON HUD TO ORDER",
                RadialSelection.FormatHint(inDeadzone: false, isAvailable: false, isTargetRequired: true));
            Assert.Equal("ORDER UNAVAILABLE",
                RadialSelection.FormatHint(inDeadzone: false, isAvailable: false, isTargetRequired: false));
            Assert.Equal("RELEASE TO CONFIRM",
                RadialSelection.FormatHint(inDeadzone: false, isAvailable: true));
        }

        private static int AtAngle(double degrees, int previous) => RadialSelection.FromPointer(
            (float)Math.Sin(degrees * Math.PI / 180) * 140,
            (float)Math.Cos(degrees * Math.PI / 180) * 140, previous, 6);
    }
}
