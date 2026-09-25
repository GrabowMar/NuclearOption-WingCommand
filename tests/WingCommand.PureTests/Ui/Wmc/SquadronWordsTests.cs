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
        public void TheHeadAndTheCaptionsCountEachPilotOnce()
        {
            Assert.Equal("10 PILOTS · 1 SAR · 1 KIA", SquadronWords.Head(10, 1, 1));
            Assert.Equal("1 PILOT", SquadronWords.Head(1, 0, 0));
            Assert.Equal("NO PILOTS", SquadronWords.Head(0, 0, 0));
            Assert.Equal("2 FLYING · 1 INBOUND", SquadronWords.PilotsCaption(2, 1));
            Assert.Equal("NONE FLYING", SquadronWords.PilotsCaption(0, 0));
            Assert.Equal("NEXT HATCH", SquadronWords.ReadyCaption("HATCH"));
            Assert.Equal("NEW PILOT AT LAUNCH", SquadronWords.ReadyCaption(null));
            Assert.Equal("1 SAR · 1 KIA · 1 POW", SquadronWords.LostCaption(1, 1, 1));
            Assert.Equal("NONE LOST", SquadronWords.LostCaption(0, 0, 0));
            Assert.True(SquadronWords.LostCaption(99, 99, 99).Length <= SquadronWords.CaptionChars);
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
            Assert.Contains("44 CR", SquadronWords.DownedSlot("44 CR"));
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
            Assert.Equal("HATCH DOWNED — AIR SAR OR LOCAL SAR", SquadronWords.Alert(PilotStatus.Downed, "HATCH"));
            Assert.Equal("HATCH MIA — LOCAL SAR CAN SEARCH", SquadronWords.Alert(PilotStatus.Missing, "HATCH"));
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
        }
    }
}
