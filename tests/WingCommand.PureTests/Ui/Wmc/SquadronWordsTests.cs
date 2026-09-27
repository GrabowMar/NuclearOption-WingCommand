using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class SquadronWordsTests
    {
        private static readonly PilotStatus[] All = (PilotStatus[])Enum.GetValues(typeof(PilotStatus));

        [Fact]
        public void EachStatusReadsOneWayOnTheRowAndTheStamp()
        {
            Assert.Equal("FLYING #2", SquadronWords.Row(PilotStatus.Flying, false, 2));
            Assert.Equal("FLYING #2", SquadronWords.Stamp(PilotStatus.Flying, false, 2));
            // A seat kept while its aircraft is not a wing member (no number): the word alone.
            Assert.Equal("FLYING", SquadronWords.Row(PilotStatus.Flying, false, 0));
            Assert.Equal("FREE · NEXT UP", SquadronWords.Row(PilotStatus.Free, true, 0));
            Assert.Equal("NEXT UP", SquadronWords.Stamp(PilotStatus.Free, true, 0));
            Assert.Equal("FREE", SquadronWords.Row(PilotStatus.Free, false, 0));
            Assert.Equal("DOWNED — SAR", SquadronWords.Row(PilotStatus.Downed, false, 0));
            Assert.Equal("SAR #3 GOING", SquadronWords.Row(PilotStatus.Rescue, false, 3));
            Assert.Equal("MIA", SquadronWords.Row(PilotStatus.Missing, false, 0));
            Assert.Equal("KIA", SquadronWords.Stamp(PilotStatus.Kia, false, 0));
            Assert.Equal("INBOUND", SquadronWords.Row(PilotStatus.Inbound, false, 0));
        }

        [Fact]
        public void EveryRowWordAndStampFitsItsBox()
        {
            foreach (PilotStatus s in All)
                foreach (bool next in new[] { true, false })
                {
                    Assert.True(SquadronWords.Row(s, next, 7).Length <= SquadronWords.RowChars, s.ToString());
                    Assert.True(SquadronWords.Stamp(s, next, 7).Length <= SquadronWords.StampChars, s.ToString());
                }
        }

        [Fact]
        public void TheRailAndTheWordAgreeOnTrouble()
        {
            Assert.Equal("live", SquadronWords.Rail(PilotStatus.Flying, false));
            Assert.Equal("info", SquadronWords.Rail(PilotStatus.Free, true));
            Assert.Equal("inert", SquadronWords.Rail(PilotStatus.Free, false));
            Assert.Equal("warn", SquadronWords.Rail(PilotStatus.Downed, false));
            Assert.Equal("danger", SquadronWords.Rail(PilotStatus.Kia, false));
            Assert.Equal("bad", SquadronWords.Level(PilotStatus.Captured));
            Assert.Equal("ok", SquadronWords.Level(PilotStatus.Flying));
        }

        [Fact]
        public void RankBadgesAreTheFiveRankLetters()
        {
            Assert.Equal("R", SquadronWords.Badge(WingRank.Rookie));
            Assert.Equal("L", SquadronWords.Badge(WingRank.Legend));
        }

        [Fact]
        public void TheHeadCountsEachPilotOnce()
        {
            // Spec bezel v2: the PILOTS · READY · LOST tiles are gone; the head carries them.
            Assert.Equal("10 PILOTS · 3 FLYING · 5 FREE · 2 LOST", SquadronWords.Head(10, 3, 5, 2));
            Assert.Equal("1 PILOT · 1 FREE", SquadronWords.Head(1, 0, 1, 0));
            Assert.Equal("NO PILOTS", SquadronWords.Head(0, 0, 0, 0));
            Assert.True(SquadronWords.Head(99, 99, 99, 99).Length <= 45);
        }

        [Fact]
        public void TheSlotLineSaysWhereThePilotIsInWords()
        {
            Assert.Equal("#2 · ELEMENT A · SLOT", SquadronWords.FlyingSlot(2, "ELEMENT A", "SLOT"));
            Assert.Equal("NEXT UP · FLIES THE NEXT LAUNCH", SquadronWords.FreeSlot(true));
            Assert.Equal("LOCAL SAR · BACK IN 4:59", SquadronWords.LocalSarSlot("4:59"));
            Assert.Equal("KIA · EXPLOSION · BY FS-12 REVOKER", SquadronWords.KiaSlot("explosion", "FS-12 Revoker"));
            Assert.Equal("KIA", SquadronWords.KiaSlot(null, null));
            Assert.True(SquadronWords.KiaSlot(new string('c', 80), new string('k', 80)).Length <= SquadronWords.SlotChars);
            Assert.Contains("44 CR", SquadronWords.DownedSlot("44 CR", true));
            // Review R6: with no helicopter able to go (an all fixed-wing wing), the slot line does not offer AIR SAR.
            Assert.DoesNotContain("AIR SAR", SquadronWords.DownedSlot("44 CR", false));
            Assert.Contains("NO HELICOPTER", SquadronWords.DownedSlot("44 CR", false));
            Assert.Contains("44 CR", SquadronWords.DownedSlot("44 CR", false));
            Assert.Contains("SEARCHES 5:00", SquadronWords.MissingSlot("44 CR", "5:00"));
        }

        [Fact]
        public void TheRecordUsesSingularForms()
        {
            Assert.Equal("3 KILLS · 2 SORTIES", SquadronWords.Record(3, 2));
            Assert.Equal("1 KILL · 1 SORTIE", SquadronWords.Record(1, 1));
            Assert.Equal("RADIO · AGGRESSIVE", SquadronWords.Persona("Aggressive"));
        }

        [Fact]
        public void AirSarAsksOnlyForAPilotDownOnLandAndNamesTheHelicopter()
        {
            Assert.Null(SquadronWords.AirWhy(PilotStatus.Downed, false));
            Assert.Contains("LOCAL SAR", SquadronWords.AirWhy(PilotStatus.Missing, false));
            Assert.NotNull(SquadronWords.AirWhy(PilotStatus.Flying, false));
            // Review R6: a pilot under a local search is down on land; the reason says what is going on instead.
            Assert.Contains("local search", SquadronWords.AirWhy(PilotStatus.LocalSar, false));
            Assert.Equal(SquadronWords.ClientWhy, SquadronWords.AirWhy(PilotStatus.Downed, true));
            Assert.Equal("AIR SAR", SquadronWords.AirLabel(0));
            Assert.Equal("#3 GOING", SquadronWords.AirLabel(3));
        }

        [Fact]
        public void LocalSarIsOfferedForDownedAndMissingPilotsOnly()
        {
            Assert.Null(SquadronWords.LocalWhy(PilotStatus.Downed, false));
            Assert.Null(SquadronWords.LocalWhy(PilotStatus.Missing, false));
            Assert.NotNull(SquadronWords.LocalWhy(PilotStatus.Flying, false));
            Assert.NotNull(SquadronWords.LocalWhy(PilotStatus.Kia, false));
            // Review R6: a pilot a helicopter is going for is downed; the reason names the helicopter.
            Assert.Contains("helicopter", SquadronWords.LocalWhy(PilotStatus.Rescue, false));
            Assert.Equal("LOCAL SAR?", SquadronWords.LocalLabel(true, null));
            Assert.Equal("4:59 LEFT", SquadronWords.LocalLabel(false, "4:59"));
            Assert.Contains("44 CR", SquadronWords.LocalAsk("HATCH", "44 CR", "5:00"));
        }

        [Fact]
        public void EverySarAndReleaseLabelFitsItsButton()
        {
            foreach (string s in new[] { SquadronWords.AirLabel(7), SquadronWords.AirLabel(0), SquadronWords.LocalLabel(true, null),
                         SquadronWords.LocalLabel(false, "99:59"), SquadronWords.ReleaseLabel(true), SquadronWords.ReleaseLabel(false) })
                Assert.True(s.Length <= SquadronWords.SarChars, s);
        }

        [Fact]
        public void TheAlertNamesTheDownedOrMissingPilot()
        {
            // Review R6: the alert names the trouble, not a button that may be off (no helicopter; the ejection check).
            Assert.Equal("HATCH DOWNED — SAR NEEDED", SquadronWords.Alert(PilotStatus.Downed, "HATCH"));
            Assert.Equal("HATCH MIA — NO SIGNAL", SquadronWords.Alert(PilotStatus.Missing, "HATCH"));
            Assert.Null(SquadronWords.Alert(PilotStatus.Flying, "HATCH"));
        }

        [Fact]
        public void NoSquadronWordUsesAnEllipsisDaggerOrBoxShape()
        {
            foreach (PilotStatus s in All)
                foreach (string w in new[] { SquadronWords.Row(s, true, 2), SquadronWords.Stamp(s, false, 2), SquadronWords.Alert(s, "X") ?? "" })
                    foreach (char c in w)
                    {
                        Assert.False(c == '…' || c == '†' || (c >= '■' && c <= '◿'), w);
                    }
            foreach (string w in new[] { SquadronWords.RowTip, SquadronWords.RecruitTip, SquadronWords.AirTip, SquadronWords.ReleaseTip,
                         SquadronWords.LocalTip("44 CR"), SquadronWords.StudioTip, SquadronWords.Empty, SquadronWords.ClientWhy,
                         SquadronWords.LocalPending, SquadronWords.LocalGone, SquadronWords.NoFunds, SquadronWords.LocalNeeds("44 CR", "9 CR") })
                foreach (char c in w)
                    Assert.False(c == '…' || c == '†' || (c >= '■' && c <= '◿') || (c >= '←' && c <= '⇿'), w);
        }
    }
}
