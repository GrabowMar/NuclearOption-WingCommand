using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>R7: the SQUADRON notch's words (research squadron-studio §2.3–§2.5; critic resolution 1: R6 owns SquadronWords).</summary>
    public class StudioWordsTests
    {
        [Fact]
        public void TheChipReadsNewSavedEditedOrNotSaved()
        {
            Assert.Equal("NEW", StudioWords.Chip(DraftState.New, out string r0));
            Assert.Equal("SAVED", StudioWords.Chip(DraftState.Saved, out string r1));
            Assert.Equal("EDITED", StudioWords.Chip(DraftState.Edited, out string r2));
            Assert.Equal("NOT SAVED", StudioWords.Chip(DraftState.NotSaved, out string r3));
            Assert.Equal(("info", "live", "warn", "inert"), (r0, r1, r2, r3));
        }

        [Fact]
        public void TheHeadCountsSavedAndThisMissionsPilots()
        {
            Assert.Equal("3 SAVED · 2 THIS MISSION", StudioWords.ListHead(3, 2));
            Assert.Equal("NONE SAVED", StudioWords.ListHead(0, 0));
        }

        [Fact]
        public void TheServiceLineUsesSingularAndPluralCounts()
        {
            Assert.Equal("1 MISSION · 1 SORTIE · 1 KILL", StudioWords.Service(1, 1, 1));
            Assert.Equal("3 MISSIONS · 0 SORTIES · 7 KILLS", StudioWords.Service(3, 0, 7));
            Assert.Equal("BEST RANK VETERAN", StudioWords.BestRank(3, 400));
            Assert.Equal("NO MISSIONS YET", StudioWords.BestRank(0, 0));
        }

        [Fact]
        public void ASavedPilotOffTheRosterAndALiveUnsavedOneSaySoInWords()
        {
            Assert.Contains("RECRUIT", StudioWords.NotInMission);
            Assert.Contains("SAVE", StudioWords.NotSaved);
        }

        [Theory]
        [InlineData((int)PilotStatus.Flying, "IN THE AIR")]
        [InlineData((int)PilotStatus.Inbound, "ON A LAUNCH")]
        [InlineData((int)PilotStatus.Downed, "DOWNED")]
        [InlineData((int)PilotStatus.Rescue, "DOWNED")]
        [InlineData((int)PilotStatus.Missing, "MIA")]
        [InlineData((int)PilotStatus.LocalSar, "LOCAL SAR")]
        [InlineData((int)PilotStatus.Captured, "CAPTURED")]
        [InlineData((int)PilotStatus.Kia, "KIA")]
        public void DischargeReasonsNameWhyThePilotIsNotFree(int status, string word) =>
            Assert.Contains(word, StudioWords.DischargeWhy((PilotStatus)status));

        [Fact]
        public void AFreePilotHasNoDischargeReason() => Assert.Null(StudioWords.DischargeWhy(PilotStatus.Free));

        [Fact]
        public void TheLookValuesReadAsTheStudioShowsThem()
        {
            Assert.Equal("BALD", StudioWords.Hair(0));
            Assert.Equal("3/6", StudioWords.Hair(3));
            Assert.Equal("1/6", StudioWords.Face(0));
            Assert.Equal("4/4", StudioWords.Scene(3));
        }

        [Fact]
        public void TheBioCounterReadsCharactersOfTheLimit() => Assert.Equal("42/280", StudioWords.BioCounter(42));

        [Fact]
        public void TheRecordButtonNamesItsAction()
        {
            Assert.Equal("RECRUIT", StudioWords.RecruitLabel(false, false));
            Assert.Equal("DISCHARGE", StudioWords.RecruitLabel(true, false));
            Assert.Equal("DISCHARGE?", StudioWords.RecruitLabel(true, true));
        }

        [Fact]
        public void EveryButtonLabelFitsItsWidthBudget()
        {
            foreach (string s in new[] { StudioWords.RecruitLabel(true, true), "NEW", "CLONE", "DELETE?", "IMPORT", "EXPORT", "FOLDER",
                         "RANDOM", "RANDOM LOOK", "GENERATE", "SAVE", "REVERT" })
                Assert.True(s.Length <= StudioWords.ButtonChars, s);
        }

        [Fact]
        public void NoWordCarriesAnEllipsisDaggerBoxOrArrow()
        {
            foreach (string w in new[] { StudioWords.NotSaved, StudioWords.NotInMission, StudioWords.Hint, StudioWords.ClientRecord,
                         StudioWords.ImportToast(3, 1, 2), StudioWords.ExportToast(4), StudioWords.EmptyList(2), StudioWords.UnsavedAsk,
                         StudioWords.DeleteAsk("HATCH"), StudioWords.DischargeWhy(PilotStatus.Kia), StudioWords.Service(2, 3, 4) })
                foreach (char c in w)
                    Assert.False(c == '…' || c == '†' || (c >= '■' && c <= '◿') || (c >= '←' && c <= '⇿'), w);
        }
    }
}
