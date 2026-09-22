using NOAvionics;
using Xunit;

namespace WingCommand.PureTests
{
    public sealed class AvionicsClaimTests
    {
        public AvionicsClaimTests()
        {
            BezelRegistry.Reset();
            MapPicker.Reset();
        }

        [Fact]
        public void TwoClaimsCannotShareASlotAndWingPointBlocksSupport()
        {
            Assert.True(BezelRegistry.TryClaim(BezelRegistry.Wmc, true, 6, 6, (_, __) => true,
                out bool left, out int slot));
            Assert.True(left);
            Assert.Equal(0, slot);
            Assert.True(BezelRegistry.TryClaim(BezelRegistry.Ops, true, 6, 6, (_, __) => true,
                out bool opsLeft, out int opsSlot));
            Assert.True(opsLeft);
            Assert.NotEqual(slot, opsSlot);
            BezelRegistry.Release(BezelRegistry.Wmc);
            Assert.False(BezelRegistry.IsClaimed(BezelRegistry.Wmc));

            Assert.True(MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureLeft, "HOLD ARMED"));
            Assert.False(MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, "SUPPORT"));
            MapPicker.Disarm(MapPicker.WingPoint);
            Assert.True(MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, "SUPPORT"));
        }
    }

    public sealed class WingDoctrineTests
    {
        [Fact]
        public void PresetsMatchTheReplacedRoeBundles()
        {
            AssertDoctrine(WingDoctrine.Reserve, MissileGuard.Wing, MissileResponse.Press,
                FormationInterval.Close, true, TargetPolicy.Hold, EngagementReach.Slot, "RESERVE");
            AssertDoctrine(WingDoctrine.Escort, MissileGuard.Lead, MissileResponse.Break,
                FormationInterval.Standard, true, TargetPolicy.Cover, EngagementReach.Slot, "ESCORT");
            AssertDoctrine(WingDoctrine.Sweep, MissileGuard.Wing, MissileResponse.Break,
                FormationInterval.Open, true, TargetPolicy.Both, EngagementReach.Long, "SWEEP");
        }

        [Fact]
        public void TurningSpreadOffMakesReserveCustom()
        {
            var custom = new WingDoctrine(MissileGuard.Wing, MissileResponse.Press,
                FormationInterval.Close, false, TargetPolicy.Hold, EngagementReach.Slot);
            Assert.Equal("CUSTOM", custom.PatternName);
        }

        [Fact]
        public void OffForcesBreakSoAStoredPressCannotNoseHold()
        {
            var doctrine = new WingDoctrine(MissileGuard.Off, MissileResponse.Press,
                FormationInterval.Close, true, TargetPolicy.Hold, EngagementReach.Slot);
            Assert.Equal(MissileResponse.Break, doctrine.Response);
        }

        [Theory]
        [InlineData("Free", "SWEEP")]
        [InlineData("Tight", "ESCORT")]
        [InlineData("Escort", "ESCORT")]
        [InlineData("Hold", "RESERVE")]
        [InlineData("", "RESERVE")]
        [InlineData(null, "RESERVE")]
        public void LegacyRoeNamesMigrate(string legacy, string pattern)
        {
            Assert.Equal(pattern, WingDoctrine.FromLegacy(legacy).PatternName);
        }

        [Fact]
        public void PatternNameAndFieldFormBothParse()
        {
            Assert.Equal(WingDoctrine.Reserve, Parse("Reserve"));
            Assert.Equal(WingDoctrine.Reserve, Parse("Wing,Press,Close,Spread,Hold,Slot"));
            Assert.Equal(WingDoctrine.Escort, Parse("Lead,Break,Std,Spread,Cover,Slot"));
            Assert.False(WingDoctrine.TryParse("nope", out _));
            Assert.False(WingDoctrine.TryParse("Wing,Press,Close", out _));
        }

        [Fact]
        public void DoctrineLineWinsOverDefaultRoe()
        {
            const string text = "[Engagement]\nDefaultRoe = Free\nDoctrine = Escort\n";
            Assert.Equal(WingDoctrine.Escort, WingDoctrine.FromConfigText(text));
        }

        [Fact]
        public void MissingDoctrineKeyReadsDefaultRoe()
        {
            Assert.Equal(WingDoctrine.Sweep, WingDoctrine.FromConfigText("DefaultRoe = Free"));
            Assert.Equal(WingDoctrine.Escort, WingDoctrine.FromConfigText("DefaultRoe = Escort"));
            Assert.Equal(WingDoctrine.Reserve, WingDoctrine.FromConfigText("DefaultRoe = Hold"));
        }

        [Fact]
        public void CustomCyclesToReserveAndThePresetsCycleInOrder()
        {
            Assert.Equal(WingDoctrine.Escort, WingDoctrine.Reserve.NextPattern());
            Assert.Equal(WingDoctrine.Sweep, WingDoctrine.Escort.NextPattern());
            Assert.Equal(WingDoctrine.Reserve, WingDoctrine.Sweep.NextPattern());
            var custom = new WingDoctrine(MissileGuard.Self, MissileResponse.Break,
                FormationInterval.Open, false, TargetPolicy.Air, EngagementReach.Long);
            Assert.Equal(WingDoctrine.Reserve, custom.NextPattern());
        }

        [Fact]
        public void IntervalCloseIsStickyAndLocksTheEchelon()
        {
            Assert.Equal(0.7f, WingDoctrineRules.SpacingScale(FormationInterval.Close));
            Assert.Equal(1f, WingDoctrineRules.SpacingScale(FormationInterval.Standard));
            Assert.Equal(1.5f, WingDoctrineRules.SpacingScale(FormationInterval.Open));
            Assert.True(WingDoctrineRules.StickyTrack(FormationInterval.Close));
            Assert.False(WingDoctrineRules.EchelonSwap(FormationInterval.Close));
            Assert.False(WingDoctrineRules.StickyTrack(FormationInterval.Open));
            Assert.True(WingDoctrineRules.EchelonSwap(FormationInterval.Open));
            Assert.Equal(1.5f, WingDoctrineRules.AppliedSpacingScale(FormationInterval.Open, false, 1.45f));
            Assert.Equal(1.5f, WingDoctrineRules.AppliedSpacingScale(FormationInterval.Open, true, 1.45f));
            Assert.Equal(1.45f, WingDoctrineRules.AppliedSpacingScale(FormationInterval.Close, true, 1.45f));
        }

        [Fact]
        public void ReachAndAllowFollowTheTargetAxis()
        {
            Assert.Equal(6000f, WingDoctrineRules.EngageRange(EngagementReach.Slot));
            Assert.Equal(12000f, WingDoctrineRules.EngageRange(EngagementReach.Long));
            Assert.Equal(12000f, WingDoctrineRules.ExplicitOrderRange());
            Assert.Equal(DoctrineAllow.None, WingDoctrineRules.StandingAllow(TargetPolicy.Hold));
            Assert.Equal(DoctrineAllow.None, WingDoctrineRules.StandingAllow(TargetPolicy.Cover));
            Assert.Equal(DoctrineAllow.AirOnly, WingDoctrineRules.StandingAllow(TargetPolicy.Air));
            Assert.Equal(DoctrineAllow.GroundOnly, WingDoctrineRules.StandingAllow(TargetPolicy.Ground));
            Assert.Equal(DoctrineAllow.AirAndGround, WingDoctrineRules.StandingAllow(TargetPolicy.Both));
        }

        [Fact]
        public void ProtecteeOrderIsSelfThenWingOrLeaderFirst()
        {
            var ranks = new ProtecteeRank[3];
            Assert.Equal(0, WingDoctrineRules.CopyProtecteeOrder(MissileGuard.Off, ranks));
            Assert.Equal(1, WingDoctrineRules.CopyProtecteeOrder(MissileGuard.Self, ranks));
            Assert.Equal(ProtecteeRank.Self, ranks[0]);
            Assert.Equal(3, WingDoctrineRules.CopyProtecteeOrder(MissileGuard.Wing, ranks));
            Assert.Equal(ProtecteeRank.Self, ranks[0]);
            Assert.Equal(ProtecteeRank.Leader, ranks[1]);
            Assert.Equal(ProtecteeRank.Wingman, ranks[2]);
            Assert.Equal(3, WingDoctrineRules.CopyProtecteeOrder(MissileGuard.Lead, ranks));
            Assert.Equal(ProtecteeRank.Leader, ranks[0]);
            Assert.Equal(ProtecteeRank.Self, ranks[1]);
            Assert.Equal(ProtecteeRank.Wingman, ranks[2]);
        }

        private static void AssertDoctrine(WingDoctrine doctrine, MissileGuard guard, MissileResponse response,
            FormationInterval interval, bool spread, TargetPolicy targets, EngagementReach reach, string name)
        {
            Assert.Equal(guard, doctrine.Guard);
            Assert.Equal(response, doctrine.Response);
            Assert.Equal(interval, doctrine.Interval);
            Assert.Equal(spread, doctrine.SpreadWhenThreatened);
            Assert.Equal(targets, doctrine.Targets);
            Assert.Equal(reach, doctrine.Reach);
            Assert.Equal(name, doctrine.PatternName);
        }

        private static WingDoctrine Parse(string text)
        {
            Assert.True(WingDoctrine.TryParse(text, out WingDoctrine value));
            return value;
        }
    }
}
