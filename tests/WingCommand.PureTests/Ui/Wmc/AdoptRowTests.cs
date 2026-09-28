using Xunit;

namespace WingCommand.PureTests
{
    public class AdoptRowTests
    {
        [Fact]
        public void TheRowIsHiddenWhenNothingCanBeAdopted()
        {
            Assert.False(AdoptRow.Visible(0, false));
            Assert.True(AdoptRow.Visible(2, false));
        }

        [Fact]
        public void TheRowIsHiddenOnAClient() => Assert.False(AdoptRow.Visible(2, true));

        [Fact]
        public void TheLabelCountsAndPricesTheAdoption()
        {
            Assert.Equal("ADOPT 2 · 50 CR", AdoptRow.Label(2, 50f, false));
            Assert.Equal("ADOPT 1 · FREE", AdoptRow.Label(1, 0f, false));
        }

        [Fact]
        public void TheLabelAsksWhileTheSecondPressIsDue()
        {
            Assert.Equal("ADOPT 2 · 50 CR?", AdoptRow.Label(2, 50f, true));
            Assert.Equal("Adopt 2 aircraft for 50 CR? Press ADOPT again", AdoptRow.Ask(2, 50f));
            Assert.Equal("Adopt 1 aircraft for FREE? Press ADOPT again", AdoptRow.Ask(1, 0f));
        }

        [Fact]
        public void TheNoteSaysHowManyMoreCannotJoinAndWhy()
        {
            Assert.Equal("FRIENDLY AI SELECTED ON THE MAP", AdoptRow.Note(0, null));
            string note = AdoptRow.Note(1, "already flying for another player's wing");
            Assert.StartsWith("+1 CANNOT JOIN · ALREADY", note);
            Assert.True(note.Length <= AdoptRow.NoteChars, note);
            Assert.DoesNotContain("…", note);
            Assert.Equal("+2 CANNOT JOIN", AdoptRow.Note(2, ""));
        }

        [Fact]
        public void TheConfirmKeyChangesWhenTheSelectionChanges()
        {
            string a = AdoptRow.Key(new uint[] { 11, 12 });
            Assert.Equal(a, AdoptRow.Key(new uint[] { 11, 12 }));
            Assert.NotEqual(a, AdoptRow.Key(new uint[] { 11, 13 }));
            Assert.NotEqual(a, AdoptRow.Key(new uint[] { 11 }));
        }
    }
}
