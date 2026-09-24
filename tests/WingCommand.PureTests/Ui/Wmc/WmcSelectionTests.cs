using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class WmcSelectionTests
    {
        private static SnapshotMember[] Rows() => new[]
        {
            new SnapshotMember { Id = 11, Slot = 0, Element = 0 }, new SnapshotMember { Id = 12, Slot = 1, Element = 0 },
            new SnapshotMember { Id = 13, Slot = 2, Element = 1 }, new SnapshotMember { Id = 14, Slot = 3, Element = 1 },
        };

        [Fact]
        public void NothingSelectedIsTheWholeWing()
        {
            var s = new WmcSelection();
            Assert.Equal(ScopeKind.Wing, s.Scope(Rows(), 4).Kind);
            Assert.Equal("WING", s.Label(Rows(), 4));
            Assert.Equal(0u, s.Single);
        }

        [Fact]
        public void ToggleBuildsAMemberScope()
        {
            var s = new WmcSelection();
            s.Toggle(12);
            s.Toggle(13);
            WingScope scope = s.Scope(Rows(), 4);
            Assert.Equal(ScopeKind.Members, scope.Kind);
            Assert.Equal(new uint[] { 12, 13 }, scope.Members);
            Assert.Equal("#3 #4", s.Label(Rows(), 4));
            s.Toggle(12);
            Assert.Equal(13u, s.Single);
        }

        [Fact]
        public void AnElementHeaderSelectsTheElement()
        {
            var s = new WmcSelection();
            s.SelectElement(1, new List<uint> { 13, 14 });
            WingScope scope = s.Scope(Rows(), 4);
            Assert.Equal(ScopeKind.Element, scope.Kind);
            Assert.Equal(1, scope.Element);
            Assert.Equal("ELEMENT B", s.Label(Rows(), 4));
            s.Toggle(13);   // a member click leaves the element scope
            Assert.Equal(ScopeKind.Members, s.Scope(Rows(), 4).Kind);
        }

        [Fact]
        public void SelectingEveryoneIsTheWing()
        {
            var s = new WmcSelection();
            foreach (uint id in new uint[] { 11, 12, 13, 14 }) s.Toggle(id);
            Assert.Equal(ScopeKind.Wing, s.Scope(Rows(), 4).Kind);
        }

        [Fact]
        public void PruneDropsTheGoneAndKeepsTheRest()
        {
            var s = new WmcSelection();
            s.Toggle(12);
            s.Toggle(99);
            s.Prune(Rows(), 4);
            Assert.Equal(1, s.Count);
            Assert.True(s.Contains(12));
        }

        [Fact]
        public void ClearIsTheWing()
        {
            var s = new WmcSelection();
            s.Toggle(12);
            s.Clear();
            Assert.Equal(ScopeKind.Wing, s.Scope(Rows(), 4).Kind);
        }

        [Fact]
        public void AnElementScopeEndsWhenItsMembersMoveOut()
        {
            // Review P3 I1: B merged into A; the selection stays #4 #5 as members, never "ELEMENT B".
            var s = new WmcSelection();
            s.SelectElement(1, new List<uint> { 13, 14 });
            SnapshotMember[] rows = Rows();
            rows[2].Element = 0;
            rows[3].Element = 0;
            s.Prune(rows, 4);
            WingScope scope = s.Scope(rows, 4);
            Assert.Equal(ScopeKind.Members, scope.Kind);
            Assert.Equal(new uint[] { 13, 14 }, scope.Members);
            Assert.Equal("#4 #5", s.Label(rows, 4));
        }

        [Fact]
        public void SelectOnlyReplacesTheSelection()
        {
            var s = new WmcSelection();
            s.SelectElement(1, new List<uint> { 13, 14 });
            s.SelectOnly(12);
            WingScope scope = s.Scope(Rows(), 4);
            Assert.Equal(ScopeKind.Members, scope.Kind);
            Assert.Equal(new uint[] { 12 }, scope.Members);
        }
    }
}
