using System.Globalization;
using Xunit;

namespace WingCommand.PureTests
{
    public class WingHudTextTests
    {
        [Theory]
        [InlineData((int)BehaviourId.Rejoin, false, "JOIN")]
        [InlineData((int)BehaviourId.StationKeep, false, "SLOT")]
        [InlineData((int)BehaviourId.HoldOverhead, false, "HOLD")]
        [InlineData((int)BehaviourId.Trail, false, "TRAIL")]
        [InlineData((int)BehaviourId.Rejoin, true, "BEHIND")]
        public void PhaseCodes(int behaviour, bool behind, string expected) =>
            Assert.Equal(expected, WingHudText.Phase((BehaviourId)behaviour, behind));

        [Fact]
        public void BindingShowsTheMostUrgentLimit()
        {
            Assert.Equal("", WingHudText.Binding(default));
            Assert.Equal("BANK TERR", WingHudText.Binding(new BindingReport { BankBy = ConstraintId.Terrain }));
            Assert.Equal("NZ ENV", WingHudText.Binding(new BindingReport { NzBy = ConstraintId.Envelope }));
            Assert.Equal("VERT TERR", WingHudText.Binding(new BindingReport { VerticalBy = ConstraintId.Terrain }));
            Assert.Equal("COLL", WingHudText.Binding(new BindingReport { CollisionActive = true, BankBy = ConstraintId.Authority }));
            Assert.Equal("GCAS", WingHudText.Binding(new BindingReport { GcasActive = true, CollisionActive = true }));
        }

        [Fact]
        public void MemberRowNumbersWingmenFromTwo() =>
            Assert.Equal("2  SLOT     12m  BANK TERR", WingHudText.Member(0, "SLOT", 12.4f, "BANK TERR"));

        [Fact]
        public void MemberRowWithoutBindingHasNoTrailingSpace() =>
            Assert.Equal("4  JOIN   1850m", WingHudText.Member(2, "JOIN", 1849.6f, ""));

        [Fact]
        public void AutopilotAnnunciatorListsModesAndTargets()
        {
            var h = new HoldSpec
            {
                Lateral = LateralHold.Heading, HeadingDeg = 270.4f, Vertical = VerticalHold.Altitude, AltitudeM = 3200.2f,
                Speed = true, SpeedMps = 220f,
            };
            Assert.Equal("AP HDG 270  ALT 3200  SPD 792", WingHudText.Autopilot(h, false, false));
            Assert.Equal("AP (HDG 270)  ALT 3200  SPD 792", WingHudText.Autopilot(h, true, false));
        }

        [Fact]
        public void HeadingWrapsToThreeDigits()
        {
            var h = new HoldSpec { Lateral = LateralHold.Heading, HeadingDeg = -0.2f };
            Assert.Equal("AP HDG 000", WingHudText.Autopilot(h, false, false));
            h.HeadingDeg = 359.7f;
            Assert.Equal("AP HDG 000", WingHudText.Autopilot(h, false, false));
        }

        [Fact]
        public void AnnunciatorIsInvariantUnderAPolishLocale()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                var h = new HoldSpec { Lateral = LateralHold.Level, Vertical = VerticalHold.VerticalSpeed, VerticalSpeedMps = 2.5f };
                Assert.Equal("AP LVL  VS +2.5", WingHudText.Autopilot(h, false, false));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void NothingEngagedShowsNothing() => Assert.Equal("", WingHudText.Autopilot(default, false, false));

        [Fact]
        public void DutyShowsFightAndRecovery()
        {
            Assert.Equal("FIGHT", WingHudText.Duty(true, false, RecoveryIntent.Rtb));
            Assert.Equal("RTB", WingHudText.Duty(false, true, RecoveryIntent.Rtb));
            Assert.Equal("REFIT", WingHudText.Duty(false, true, RecoveryIntent.Refit));
            Assert.Null(WingHudText.Duty(false, false, RecoveryIntent.Rtb));
        }

        [Fact]
        public void BingoTimeShowsUnderTenMinutes()
        {
            Assert.Equal("B 4:05", WingHudText.BingoTime(245f));
            Assert.Equal("", WingHudText.BingoTime(600f));
            Assert.Equal("", WingHudText.BingoTime(float.PositiveInfinity));
            Assert.Equal("B 0:00", WingHudText.BingoTime(-3f));
            Assert.Equal("2  FIGHT   812m  B 4:05", WingHudText.Member(0, "FIGHT", 812f, "", "B 4:05"));
        }
    }
}
