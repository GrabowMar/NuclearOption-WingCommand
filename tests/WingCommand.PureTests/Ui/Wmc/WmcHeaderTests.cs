using Xunit;

namespace WingCommand.PureTests
{
    public class WmcHeaderTests
    {
        private static SnapshotMember M(int slot, float fuel, float ammo, SnapshotFlags flags = 0, MemberDuty duty = MemberDuty.Formation) =>
            new SnapshotMember
            {
                Slot = (byte)slot, Fuel = SnapshotBuilder.Fraction(fuel), Ammo = SnapshotBuilder.Fraction(ammo), Flags = (byte)flags,
                Duty = (byte)duty,
            };

        [Fact]
        public void TitleCountsTheWingWhoIsUpAndWhoIsComing()
        {
            Assert.Equal("WING 3/4 · 3 AIRBORNE · 1 INBOUND", WmcHeader.Title(3, 4, 3, 1, false, false, out string s1));
            Assert.Equal("live", s1);
            Assert.Equal("WING 1/3 · NONE AIRBORNE", WmcHeader.Title(1, 3, 0, 0, false, false, out _));
            Assert.Equal("NO WING", WmcHeader.Title(0, 3, 0, 0, false, false, out string s2));
            Assert.Equal("inert", s2);
            Assert.Equal("WING 0/3 · 1 INBOUND", WmcHeader.Title(0, 3, 0, 1, false, false, out _));
        }

        [Fact]
        public void AClientsTitleSaysWhoseWingItIsAndWhenTheLinkIsLost()
        {
            Assert.Equal("CLIENT · HOST'S WING 2/3", WmcHeader.Title(2, 3, 2, 0, true, false, out string s1));
            Assert.Equal("info", s1);
            Assert.Equal("LINK LOST · WING 2/3", WmcHeader.Title(2, 3, 2, 0, true, true, out string s2));
            Assert.Equal("danger", s2);
        }

        [Fact]
        public void VitalsNameTheFirstMemberWithEachCall()
        {
            var rows = new[]
            {
                M(0, 0.8f, 1f), M(1, 0.4f, 0f, SnapshotFlags.Winchester | SnapshotFlags.Joker), M(2, 0.6f, 0.5f, 0, MemberDuty.Defending),
            };
            WingVitals v = WingVitals.Of(rows, 3);
            Assert.Equal(3, v.Count);
            Assert.Equal(0.4f, v.MinFuel, 2);
            Assert.Equal(0f, v.MinAmmo, 2);
            Assert.Equal(1, v.JokerSlot);
            Assert.Equal(-1, v.BingoSlot);
            Assert.Equal(1, v.WinchesterSlot);
            Assert.Equal(2, v.DefendingSlot);
        }

        [Fact]
        public void FuelChipEscalatesFromMinimumToJokerToBingo()
        {
            Assert.Equal("FUEL MIN 42%", WmcHeader.Fuel(WingVitals.Of(new[] { M(0, 0.42f, 1f) }, 1), out string s1));
            Assert.Equal("live", s1);
            Assert.Equal("JOKER #3", WmcHeader.Fuel(WingVitals.Of(new[] { M(1, 0.3f, 1f, SnapshotFlags.Joker) }, 1), out string s2));
            Assert.Equal("warn", s2);
            var both = new[] { M(0, 0.3f, 1f, SnapshotFlags.Joker), M(2, 0.1f, 1f, SnapshotFlags.Bingo) };
            Assert.Equal("BINGO #4", WmcHeader.Fuel(WingVitals.Of(both, 2), out string s3));
            Assert.Equal("danger", s3);
            Assert.Equal("FUEL —", WmcHeader.Fuel(WingVitals.Of(new SnapshotMember[0], 0), out string s4));
            Assert.Equal("inert", s4);
        }

        [Fact]
        public void AmmoChipCallsTheFirstWinchester()
        {
            Assert.Equal("AMMO MIN 60%", WmcHeader.Ammo(WingVitals.Of(new[] { M(0, 1f, 0.6f) }, 1), out string s1));
            Assert.Equal("live", s1);
            Assert.Equal("WINCHESTER #2", WmcHeader.Ammo(WingVitals.Of(new[] { M(0, 1f, 0f, SnapshotFlags.Winchester) }, 1), out string s2));
            Assert.Equal("warn", s2);
            Assert.Equal("AMMO —", WmcHeader.Ammo(WingVitals.Of(new SnapshotMember[0], 0), out string s3));
            Assert.Equal("inert", s3);
        }

        [Fact]
        public void ThreatChipPutsAMissileAboveHostilesInReach()
        {
            WingVitals calm = WingVitals.Of(new[] { M(0, 1f, 1f) }, 1);
            Assert.Equal("THREAT CLEAR", WmcHeader.Threat(calm, 0, out string s1));
            Assert.Equal("live", s1);
            Assert.Equal("THREAT 2 AIR", WmcHeader.Threat(calm, 2, out string s2));
            Assert.Equal("warn", s2);
            WingVitals hit = WingVitals.Of(new[] { M(2, 1f, 1f, 0, MemberDuty.Defending) }, 1);
            Assert.Equal("MISSILE #4", WmcHeader.Threat(hit, 2, out string s3));
            Assert.Equal("danger", s3);
            Assert.Equal("THREAT —", WmcHeader.Threat(calm, -1, out string s4));
            Assert.Equal("inert", s4);
        }

        [Fact]
        public void ModeChipNamesTheArmedOrderElseWhoRunsTheWing()
        {
            Assert.Equal("ARMED ATTACK", WmcHeader.Mode(MapMode.Attack, false, out string m1));
            Assert.Equal("warn", m1);
            Assert.Equal("HOST", WmcHeader.Mode(MapMode.Off, false, out string m2));
            Assert.Equal("live", m2);
            Assert.Equal("CLIENT", WmcHeader.Mode(MapMode.Off, true, out string m3));
            Assert.Equal("info", m3);
        }

        [Fact]
        public void EveryChipTextFitsFifteenCharacters()
        {
            foreach (MapMode m in System.Enum.GetValues(typeof(MapMode)))
                Assert.True(WmcHeader.Mode(m, false, out _).Length <= WmcHeader.ChipChars, m.ToString());
            var worst = new[] { M(6, 0.05f, 0f, SnapshotFlags.Bingo | SnapshotFlags.Winchester, MemberDuty.Defending) };
            WingVitals v = WingVitals.Of(worst, 1);
            Assert.True(WmcHeader.Fuel(v, out _).Length <= WmcHeader.ChipChars);
            Assert.True(WmcHeader.Ammo(v, out _).Length <= WmcHeader.ChipChars);
            Assert.True(WmcHeader.Threat(v, 99, out _).Length <= WmcHeader.ChipChars);
            Assert.True(WmcHeader.Threat(WingVitals.Of(new[] { M(0, 1f, 1f) }, 1), 99, out _).Length <= WmcHeader.ChipChars);
        }
    }
}
