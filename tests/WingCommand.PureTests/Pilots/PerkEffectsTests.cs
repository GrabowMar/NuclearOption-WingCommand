using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class PerkEffectsTests
    {
        private static PerkEffects With(params PilotPerk[] perks) => PerkEffects.For(new List<PilotPerk>(perks));

        [Fact]
        public void NoPerksIsNeutral()
        {
            foreach (PerkEffects fx in new[] { PerkEffects.For(null), With(), PerkEffects.None })
            {
                Assert.Equal(1f, fx.IntervalScale);
                Assert.Equal(1f, fx.ReachScale);
                Assert.Equal(1f, fx.BoresightScale);
                Assert.Equal(1f, fx.KeepScale);
                Assert.Equal(0f, fx.ClearanceDelta);
                Assert.Equal(0f, fx.ReactionDelta);
                Assert.Equal(0f, fx.BreakRange);
                Assert.Equal(1, fx.SplashShots);
                Assert.Equal(20000f, fx.MaxRange(20000f, true, 9000f, 600f, 0f));
            }
        }

        [Fact]
        public void EachPerkSetsItsModifier()
        {
            Assert.Equal(PerkEffects.QuickDrawInterval, With(PilotPerk.QuickDraw).IntervalScale);
            Assert.Equal(PerkEffects.StandoffReach, With(PilotPerk.Standoff).ReachScale);
            Assert.Equal(PerkEffects.SnapshotBoresight, With(PilotPerk.Snapshot).BoresightScale);
            Assert.Equal(PerkEffects.TargetMasterKeep, With(PilotPerk.TargetMaster).KeepScale);
            Assert.Equal(PerkEffects.TerrainHuggerClearance, With(PilotPerk.TerrainHugger).ClearanceDelta);
            Assert.Equal(PerkEffects.EarlyWarningReaction, With(PilotPerk.EarlyWarning).ReactionDelta);
            Assert.Equal(PerkEffects.BreakTurnRange, With(PilotPerk.BreakTurn).BreakRange);
            Assert.Equal(2, With(PilotPerk.SalvoSpecialist).SplashShots);
        }

        [Theory]
        [InlineData(400f, 10f, true)]
        [InlineData(300f, 10f, false)]    // too slow a closure
        [InlineData(400f, 60f, false)]    // not head-on
        public void HeadOnJoustOnlyHeadOnAndFast(float closing, float aspectDeg, bool bonus)
        {
            float r = With(PilotPerk.HeadOnJoust).MaxRange(10000f, false, 1000f, closing, aspectDeg);
            Assert.Equal(bonus ? 10000f * PerkEffects.HeadOnRange : 10000f, r, 1);
        }

        [Theory]
        [InlineData(true, 5000f, true)]
        [InlineData(true, 3000f, false)]  // too low
        [InlineData(false, 5000f, false)] // infrared
        public void ApexHunterOnlyRadarAndHigh(bool radar, float altitude, bool bonus)
        {
            float r = With(PilotPerk.ApexHunter).MaxRange(10000f, radar, altitude, 0f, 90f);
            Assert.Equal(bonus ? 10000f * PerkEffects.ApexRange : 10000f, r, 1);
        }

        [Fact]
        public void SnapshotKeepsNeverAsNever()
        {
            PerkEffects fx = With(PilotPerk.Snapshot);
            Assert.Equal(0f, fx.Boresight(0f));
            Assert.Equal(20f * PerkEffects.SnapshotBoresight, fx.Boresight(20f), 3);
        }
    }
}
