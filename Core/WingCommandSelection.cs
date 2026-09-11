using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Tactical command selection, independent of DynamicMap.selectedIcons and weapon
    /// targeting.</summary>
    internal sealed class WingCommandSelection
    {
        internal enum Mode
        {
            All,
            Explicit,
        }

        private readonly HashSet<WingMember> selected = new HashSet<WingMember>();
        private readonly List<WingMember> stale = new List<WingMember>();
        internal readonly FlightGroups<WingMember> Groups = new FlightGroups<WingMember>();

        internal IReadOnlyList<WingMember> GroupMembers(int index, WingRegistry wing) =>
            Groups.Members(index, member => member != null && member.Alive && wing != null && wing.Contains(member));

        internal void RecallGroup(int index, WingRegistry wing)
        {
            DeselectAll();
            foreach (WingMember member in GroupMembers(index, wing)) selected.Add(member);
        }

        public Mode CurrentMode { get; private set; } = Mode.All;

        public bool IsAll => CurrentMode == Mode.All;
        public bool IsNone => CurrentMode == Mode.Explicit && selected.Count == 0;

        public void SelectAll()
        {
            selected.Clear();
            CurrentMode = Mode.All;
        }

        public void DeselectAll()
        {
            selected.Clear();
            CurrentMode = Mode.Explicit;
        }

        public void ToggleSelectAll(WingRegistry wing = null)
        {
            int total = wing != null ? wing.Count : 0;
            int count = wing != null ? Snapshot(wing).Count : 0;
            if (SelectionTogglePolicy.ShouldDeselectAll(IsAll, count, total))
            {
                DeselectAll();
            }
            else
            {
                SelectAll();
            }
        }

        public void SelectOnly(WingMember member)
        {
            selected.Clear();
            CurrentMode = Mode.Explicit;
            if (member != null && member.Alive) selected.Add(member);
        }

        public void ClickMember(WingMember member, bool toggle, WingRegistry wing = null)
        {
            if (member == null || !member.Alive) return;

            if (toggle)
            {
                if (CurrentMode == Mode.All && wing != null)
                {
                    selected.Clear();
                    CurrentMode = Mode.Explicit;
                    foreach (WingMember m in wing.Members)
                    {
                        if (m != null && m.Alive && !ReferenceEquals(m, member))
                            selected.Add(m);
                    }
                    return;
                }

                Toggle(member);
                return;
            }

            // Click the sole selected aircraft again to deselect it.
            if (SelectionTogglePolicy.ShouldDeselectMemberOnClick(CurrentMode == Mode.Explicit, selected.Count, selected.Contains(member)))
            {
                DeselectAll();
                return;
            }

            SelectOnly(member);
        }

        public void Toggle(WingMember member)
        {
            if (member == null || !member.Alive) return;

            if (CurrentMode == Mode.All)
            {
                // A modified click changes ALL to a fresh explicit selection.
                selected.Clear();
                CurrentMode = Mode.Explicit;
                selected.Add(member);
                return;
            }

            if (!selected.Add(member)) selected.Remove(member);
        }

        public bool Contains(WingMember member)
        {
            if (member == null || !member.Alive) return false;
            return CurrentMode == Mode.All || selected.Contains(member);
        }

        public void Prune(WingRegistry wing)
        {
            if (CurrentMode == Mode.All || wing == null) return;

            // Reuse stale-member storage; RemoveWhere would allocate a capturing predicate each frame.
            stale.Clear();
            foreach (WingMember member in selected)
                if (member == null || !member.Alive || !wing.Contains(member))
                    stale.Add(member);
            for (int i = 0; i < stale.Count; i++) selected.Remove(stale[i]);
            stale.Clear();
        }

        public List<WingMember> Snapshot(WingRegistry wing, bool wholeWing = false)
        {
            var result = new List<WingMember>();
            if (wing == null) return result;

            if (!wholeWing) Prune(wing);
            foreach (WingMember member in wing.Members)
            {
                if (wholeWing ? member != null && member.Alive : Contains(member)) result.Add(member);
            }
            return result;
        }

        public string Summary(WingRegistry wing)
        {
            int total = wing?.Count ?? 0;
            if (CurrentMode == Mode.All) return "ALL " + total;
            return selected.Count == 0 ? "NONE" : selected.Count + " OF " + total;
        }

        public void Reset()
        {
            SelectAll();
            Groups.Reset();
        }
    }
}
