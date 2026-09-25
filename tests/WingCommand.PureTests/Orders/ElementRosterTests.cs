using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class ElementRosterTests
    {
        private static ElementRoster Four()
        {
            var r = new ElementRoster();
            foreach (uint id in new uint[] { 11, 12, 13, 14 }) r.Add(id);
            return r;
        }

        [Fact]
        public void EveryoneStartsInElementA()
        {
            ElementRoster r = Four();
            Assert.Equal(0, r.ElementOf(13));
            Assert.Equal(4, r.Count(0));
            Assert.True(r.InUse(0));
            Assert.False(r.InUse(1));
            Assert.Equal("A", r.Name(0));
        }

        [Fact]
        public void SelectingSomeMembersDetachesThemIntoTheFirstFreeLetter()
        {
            ElementRoster r = Four();
            ScopeTarget t = r.Resolve(WingScope.OfMembers(13, 14));
            Assert.Equal(1, t.Element);
            Assert.True(t.Detached);
            Assert.Equal(1, r.ElementOf(14));
            Assert.Equal(2, r.Count(0));
        }

        [Fact]
        public void SelectingAWholeElementRetasksIt()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(13, 14));
            ScopeTarget t = r.Resolve(WingScope.OfMembers(14, 13));
            Assert.Equal(1, t.Element);
            Assert.False(t.Detached);
        }

        [Fact]
        public void SelectingPartOfAnElementSplitsIt()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(13, 14));
            ScopeTarget t = r.Resolve(WingScope.OfMembers(14));
            Assert.Equal(2, t.Element);
            Assert.Equal(1, r.Count(1));
            Assert.Equal(1, r.Count(2));
        }

        [Fact]
        public void SelectingEveryoneIsTheWholeWing()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(13));
            ScopeTarget t = r.Resolve(WingScope.OfMembers(11, 12, 13, 14));
            Assert.True(t.Everyone);
            Assert.Equal(0, t.Element);
            Assert.True(r.Resolve(WingScope.Wing).Everyone);
        }

        [Fact]
        public void UnknownIdsAreIgnoredAndNobodyIsRefused()
        {
            ElementRoster r = Four();
            ScopeTarget t = r.Resolve(WingScope.OfMembers(99));
            Assert.Equal(-1, t.Element);
            Assert.Equal("nobody selected is in the wing", t.Reason);
            Assert.Equal(4, r.Count(0));
            Assert.Equal(1, r.Resolve(WingScope.OfMembers(13, 99)).Element);
        }

        [Fact]
        public void AFifthElementIsRefused()
        {
            var r = new ElementRoster();
            foreach (uint id in new uint[] { 1, 2, 3, 4, 5 }) r.Add(id);
            r.Resolve(WingScope.OfMembers(2));
            r.Resolve(WingScope.OfMembers(3));
            r.Resolve(WingScope.OfMembers(4));
            ScopeTarget t = r.Resolve(WingScope.OfMembers(5));
            Assert.Equal(-1, t.Element);
            Assert.Equal("all four elements are in use", t.Reason);
            Assert.Equal(0, r.ElementOf(5));
        }

        [Fact]
        public void AnEmptyElementIsRefusedByLetter()
        {
            ElementRoster r = Four();
            Assert.Equal("element B is empty", r.Resolve(WingScope.OfElement(1)).Reason);
            Assert.Equal(0, r.Resolve(WingScope.OfElement(0)).Element);
        }

        [Fact]
        public void MergeFreesTheLetter()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(13, 14));
            r.Rename(1, "COBRA");
            r.Merge(1);
            Assert.Equal(0, r.ElementOf(13));
            Assert.False(r.InUse(1));
            Assert.Equal("B", r.Name(1));
            r.Resolve(WingScope.OfMembers(12));
            r.Resolve(WingScope.OfMembers(13));
            r.MergeAll();
            Assert.Equal(4, r.Count(0));
        }

        [Fact]
        public void RemoveEmptiesTheElement()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(14));
            r.Remove(14);
            Assert.False(r.InUse(1));
            Assert.Equal(3, r.Count(0));
            Assert.Equal(0, r.ElementOf(14));
        }

        [Fact]
        public void NamesAreShortAndNeverEmpty()
        {
            ElementRoster r = Four();
            Assert.Equal("no name", r.Rename(0, " "));
            Assert.Equal("name too long", r.Rename(0, "ABCDEFGHIJKLM"));
            Assert.Null(r.Rename(0, "viper"));
            Assert.Equal("VIPER", r.Name(0));
        }

        [Fact]
        public void MembersListsAnElementInJoinOrder()
        {
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(14, 12));
            var into = new List<uint>();
            r.Members(1, into);
            Assert.Equal(new uint[] { 12, 14 }, into);
        }

        [Fact]
        public void CheckRefusesEmptyElementsAndStrangersWithoutMovingAnyone()
        {
            // Review P2 I5: an immediate order's scope is checked without detaching.
            ElementRoster r = Four();
            Assert.Null(r.Check(WingScope.Wing));
            Assert.Null(r.Check(WingScope.OfMembers(12, 99)));
            Assert.Equal("nobody selected is in the wing", r.Check(WingScope.OfMembers(99)));
            Assert.Equal("element C is empty", r.Check(WingScope.OfElement(2)));
            Assert.Equal(4, r.Count(0));
        }

        [Fact]
        public void UndoDetachRestoresTheOldElements()
        {
            // Review P2 m9: a refused split leaves everyone where they were.
            ElementRoster r = Four();
            r.Resolve(WingScope.OfMembers(13, 14));
            r.Resolve(WingScope.OfMembers(14));
            r.UndoDetach();
            Assert.Equal(1, r.ElementOf(13));
            Assert.Equal(1, r.ElementOf(14));
            Assert.False(r.InUse(2));
        }

        [Fact]
        public void AReusedLetterStartsWithoutAName()
        {
            ElementRoster r = Four();
            r.Rename(1, "COBRA");
            r.Resolve(WingScope.OfMembers(13));
            Assert.Equal("B", r.Name(1));
        }
    }
}
