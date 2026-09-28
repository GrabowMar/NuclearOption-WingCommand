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
                FormationInterval.Close, false, TargetPolicy.Hold, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.On);
            Assert.Equal("CUSTOM", custom.PatternName);
        }

        [Fact]
        public void OffForcesBreakSoAStoredPressCannotNoseHold()
        {
            var doctrine = new WingDoctrine(MissileGuard.Off, MissileResponse.Press,
                FormationInterval.Close, true, TargetPolicy.Hold, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.On);
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
        public void PresetsFlyWithEveryWeaponAndTheRadarOn()
        {
            foreach (WingDoctrine d in new[] { WingDoctrine.Reserve, WingDoctrine.Escort, WingDoctrine.Sweep })
            {
                Assert.Equal(WeaponsPolicy.Auto, d.Weapons);
                Assert.Equal(RadarPolicy.On, d.Radar);
            }
        }

        [Fact]
        public void ASixValueLineReadsWithTheNewAxesDefaulted()
        {
            WingDoctrine d = Parse("Self,Break,Open,NoSpread,Air,Long");
            Assert.Equal(TargetPolicy.Air, d.Targets);
            Assert.Equal(WeaponsPolicy.Auto, d.Weapons);
            Assert.Equal(RadarPolicy.On, d.Radar);
        }

        [Fact]
        public void EveryWeaponsAndRadarValueRoundTripsInTheEightValueLine()
        {
            foreach (WeaponsPolicy w in System.Enum.GetValues(typeof(WeaponsPolicy)))
                foreach (RadarPolicy r in System.Enum.GetValues(typeof(RadarPolicy)))
                {
                    WingDoctrine d = WingDoctrine.Sweep.With(DoctrineAxis.Weapons, (byte)w).With(DoctrineAxis.Radar, (byte)r);
                    Assert.Equal(d, Parse(d.ToString()));
                }
            Assert.Equal("Wing,Break,Open,Spread,Both,Long,Guns,Silent",
                WingDoctrine.Sweep.With(DoctrineAxis.Weapons, (byte)WeaponsPolicy.Guns).With(DoctrineAxis.Radar, (byte)RadarPolicy.Silent).ToString());
            Assert.False(WingDoctrine.TryParse("Wing,Break,Open,Spread,Both,Long,Guns", out _));
        }

        [Fact]
        public void TheProfileNameIgnoresWeaponsAndRadarAndPickingOneClearsThem()
        {
            WingDoctrine silent = WingDoctrine.Sweep.With(DoctrineAxis.Radar, (byte)RadarPolicy.Silent);
            Assert.Equal("SWEEP", silent.PatternName);
            Assert.NotEqual(WingDoctrine.Sweep, silent);
            Assert.Equal(WingDoctrine.Reserve, silent.NextPattern());
            Assert.Equal(WingDoctrine.Escort, WingDoctrine.Reserve.With(DoctrineAxis.Weapons, (byte)WeaponsPolicy.Guns).NextPattern());
        }

        [Fact]
        public void WithChangesOnlyItsOwnAxis()
        {
            WingDoctrine d = WingDoctrine.Escort;
            for (int a = 0; a <= (int)DoctrineAxis.Radar; a++)
            {
                var axis = (DoctrineAxis)a;
                byte v = (byte)(d.Get(axis) == 0 ? 1 : 0);
                WingDoctrine e = d.With(axis, v);
                Assert.Equal(v, e.Get(axis));
                for (int b = 0; b <= (int)DoctrineAxis.Radar; b++)
                    if (b != a && !(axis == DoctrineAxis.Guard && b == (int)DoctrineAxis.Response))
                        Assert.Equal(d.Get((DoctrineAxis)b), e.Get((DoctrineAxis)b));
            }
        }

        [Fact]
        public void EveryValuesNameReadsBackAsThatValue()
        {
            // An order carries the value as its word (SetOverride.Text); a preset's name must never stand in for it.
            for (int a = 0; a <= (int)DoctrineAxis.Radar; a++)
            {
                var axis = (DoctrineAxis)a;
                for (byte v = 0; v < 5; v++)
                {
                    string name = WingDoctrine.ValueName(axis, v);
                    if (name == null) continue;
                    Assert.True(WingDoctrine.TryAxisValue(axis, name, out byte back), $"{axis} {name}");
                    Assert.Equal(v, back);
                }
            }
            Assert.Equal("On", WingDoctrine.ValueName(DoctrineAxis.Radar, (byte)RadarPolicy.On));
            Assert.Equal("Hold", WingDoctrine.ValueName(DoctrineAxis.Targets, (byte)TargetPolicy.Hold));
            Assert.Null(WingDoctrine.ValueName(DoctrineAxis.Radar, 3));
        }

        [Fact]
        public void AnAxisReadsItsOwnWordsOnly()
        {
            Assert.True(WingDoctrine.TryAxisValue(DoctrineAxis.Weapons, "NoAirToGround", out byte w));
            Assert.Equal((byte)WeaponsPolicy.NoAirToGround, w);
            Assert.True(WingDoctrine.TryAxisValue(DoctrineAxis.Spread, "NoSpread", out byte s));
            Assert.Equal(0, s);
            Assert.False(WingDoctrine.TryAxisValue(DoctrineAxis.Radar, "Guns", out _));
            Assert.False(WingDoctrine.TryAxisValue(DoctrineAxis.Radar, "7", out _));
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
                FormationInterval.Open, false, TargetPolicy.Air, EngagementReach.Long, WeaponsPolicy.Guns, RadarPolicy.Off);
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
