using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// Selection used for tactical commands. It is deliberately unrelated to
    /// <c>DynamicMap.selectedIcons</c>, which is also the player's weapon target list.
    /// </summary>
    internal sealed class WingCommandSelection
    {
        internal enum Mode
        {
            All,
            Explicit,
        }

        private readonly HashSet<WingMember> selected = new HashSet<WingMember>();
        private readonly List<WingMember> stale = new List<WingMember>();

        public Mode CurrentMode { get; private set; } = Mode.All;

        public bool IsAll => CurrentMode == Mode.All;
        public bool IsExplicit => CurrentMode == Mode.Explicit;
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

            // A second click on an already-selected individual plane deselects it.
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
                // A modified click while ALL is active starts a fresh explicit selection.
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

            // RemoveWhere would allocate a capturing predicate on this per-frame path.
            stale.Clear();
            foreach (WingMember member in selected)
                if (member == null || !member.Alive || !wing.Contains(member))
                    stale.Add(member);
            for (int i = 0; i < stale.Count; i++) selected.Remove(stale[i]);
            stale.Clear();
        }

        public List<WingMember> Snapshot(WingRegistry wing)
        {
            var result = new List<WingMember>();
            if (wing == null) return result;

            if (CurrentMode == Mode.All)
            {
                foreach (WingMember member in wing.Members)
                {
                    if (member != null && member.Alive) result.Add(member);
                }
                return result;
            }

            Prune(wing);
            foreach (WingMember member in wing.Members)
            {
                if (member != null && member.Alive && selected.Contains(member))
                    result.Add(member);
            }
            return result;
        }

        public string Summary(WingRegistry wing)
        {
            int total = wing?.Count ?? 0;
            if (CurrentMode == Mode.All) return "ALL " + total;
            return selected.Count == 0 ? "NONE" : selected.Count + " OF " + total;
        }

        public void Reset() => SelectAll();
    }
}
