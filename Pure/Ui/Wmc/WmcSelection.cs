using System.Collections.Generic;
using System.Text;

namespace WingCommand
{
    /// <summary>Who the next WMC order is for (spec WMC program §4 scope bar): nothing selected is the wing; a header click
    /// selects an element; member clicks toggle aircraft. Everyone selected is the wing again.</summary>
    internal sealed class WmcSelection
    {
        private readonly List<uint> ids = new List<uint>();
        private int element = -1;

        public int Count => ids.Count;
        public bool Contains(uint id) => ids.Contains(id);
        public uint Single => ids.Count == 1 ? ids[0] : 0u;

        public void Toggle(uint id)
        {
            element = -1;
            if (!ids.Remove(id)) ids.Add(id);
        }

        public void SelectElement(int e, IReadOnlyList<uint> members)
        {
            ids.Clear();
            foreach (uint id in members) ids.Add(id);
            element = e;
        }

        public void Clear()
        {
            ids.Clear();
            element = -1;
        }

        /// <summary>Drops aircraft no longer in the wing.</summary>
        public void Prune(SnapshotMember[] rows, int count)
        {
            for (int i = ids.Count - 1; i >= 0; i--)
                if (WingRows.IndexOf(rows, count, ids[i]) < 0) ids.RemoveAt(i);
            if (ids.Count == 0) element = -1;
        }

        public WingScope Scope(SnapshotMember[] rows, int count)
        {
            if (ids.Count == 0 || ids.Count >= count) return WingScope.Wing;
            if (element >= 0) return WingScope.OfElement(element);
            return WingScope.OfMembers(ids.ToArray());
        }

        public string Label(SnapshotMember[] rows, int count)
        {
            WingScope s = Scope(rows, count);
            if (s.Kind == ScopeKind.Wing) return "WING";
            if (s.Kind == ScopeKind.Element) return "ELEMENT " + ElementRoster.Letter(s.Element);
            var sb = new StringBuilder();
            foreach (uint id in ids)
            {
                int i = WingRows.IndexOf(rows, count, id);
                if (i < 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(WingRows.Number(rows[i].Slot));
            }
            return sb.ToString();
        }
    }
}
