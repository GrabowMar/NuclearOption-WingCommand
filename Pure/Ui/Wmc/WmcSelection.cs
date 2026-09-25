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

        /// <summary>The aircraft INSPECT › (or a LOG line) asked the room to show; 0 for none. It never changes who orders go
        /// to, and any new pick ends it (review R1 I1: the room's card stuck on it and its RTB went to the wrong wingman).</summary>
        public uint Inspected { get; private set; }

        public void Inspect(uint id) => Inspected = id;

        public void Toggle(uint id)
        {
            element = -1;
            Inspected = 0u;
            if (!ids.Remove(id)) ids.Add(id);
        }

        /// <summary>Just this aircraft (a plain click on its map icon).</summary>
        public void SelectOnly(uint id)
        {
            ids.Clear();
            ids.Add(id);
            element = -1;
            Inspected = 0u;
        }

        public void SelectElement(int e, IReadOnlyList<uint> members)
        {
            ids.Clear();
            foreach (uint id in members) ids.Add(id);
            element = e;
            Inspected = 0u;
        }

        public void Clear()
        {
            ids.Clear();
            element = -1;
            Inspected = 0u;
        }

        /// <summary>Drops aircraft no longer in the wing; an element choice whose aircraft moved to another element (a merge,
        /// a detach) becomes a choice of those aircraft (review P3 I1), so the scope never names an emptied letter.</summary>
        public void Prune(SnapshotMember[] rows, int count)
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                int k = WingRows.IndexOf(rows, count, ids[i]);
                if (k < 0) ids.RemoveAt(i);
                else if (element >= 0 && rows[k].Element != element) element = -1;
            }
            if (ids.Count == 0) element = -1;
            if (Inspected != 0u && WingRows.IndexOf(rows, count, Inspected) < 0) Inspected = 0u;
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
