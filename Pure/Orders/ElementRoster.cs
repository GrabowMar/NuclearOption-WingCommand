using System.Collections.Generic;

namespace WingCommand
{
    internal struct ScopeTarget
    {
        /// <summary>The element the order acts on (0 = A), or -1 with <see cref="Reason"/>.</summary>
        public int Element;
        /// <summary>The whole wing: the caller merges every element into A first.</summary>
        public bool Everyone;
        /// <summary>Members were moved into <see cref="Element"/> to carry this order.</summary>
        public bool Detached;
        public string Reason;
    }

    /// <summary>Which element each member flies in (spec WMC program §3.3): A (0) holds everyone by default; an order for
    /// some members detaches them into the first free letter; an order for exactly an element's members retasks it; a
    /// merge returns an element to A and frees its letter. Members are aircraft persistent ids, listed in join order.</summary>
    internal sealed class ElementRoster
    {
        public const int MaxElements = WingScope.MaxElements, MaxName = 12;

        private readonly List<uint> order = new List<uint>();
        private readonly Dictionary<uint, int> of = new Dictionary<uint, int>();
        private readonly string[] names = new string[MaxElements];
        private readonly List<uint> picked = new List<uint>();
        private readonly List<KeyValuePair<uint, int>> moved = new List<KeyValuePair<uint, int>>();

        public static string Letter(int e) => ((char)('A' + e)).ToString();

        public void Add(uint id)
        {
            if (of.ContainsKey(id)) return;
            of[id] = 0;
            order.Add(id);
        }

        public void Remove(uint id)
        {
            if (!of.TryGetValue(id, out int e)) return;
            of.Remove(id);
            order.Remove(id);
            if (e > 0 && Count(e) == 0) names[e] = null;
        }

        public int ElementOf(uint id) => of.TryGetValue(id, out int e) ? e : 0;

        public int Count(int e)
        {
            int n = 0;
            foreach (uint id in order)
                if (of[id] == e) n++;
            return n;
        }

        public bool InUse(int e) => e == 0 || (e > 0 && e < MaxElements && Count(e) > 0);

        public string Name(int e) => names[e] ?? Letter(e);

        /// <summary>Null when renamed, else the reason.</summary>
        public string Rename(int e, string name)
        {
            string n = (name ?? "").Trim().ToUpperInvariant();
            if (n.Length == 0) return "no name";
            if (n.Length > MaxName) return "name too long";
            names[e] = n;
            return null;
        }

        public void Members(int e, List<uint> into)
        {
            into.Clear();
            foreach (uint id in order)
                if (of[id] == e) into.Add(id);
        }

        /// <summary>Why an order for <paramref name="scope"/> would reach nobody (null: it reaches someone); moves no one
        /// (review P2 I5).</summary>
        public string Check(in WingScope scope)
        {
            switch (scope.Kind)
            {
                case ScopeKind.Element:
                    return InUse(scope.Element) ? null : "element " + Letter(scope.Element) + " is empty";
                case ScopeKind.Members:
                    if (scope.Members != null)
                        foreach (uint id in scope.Members)
                            if (of.ContainsKey(id)) return null;
                    return "nobody selected is in the wing";
                default:
                    return null;
            }
        }

        /// <summary>Puts the members the last detach moved back where they were (a refused order: review P2 m9).</summary>
        public void UndoDetach()
        {
            foreach (KeyValuePair<uint, int> m in moved)
                if (of.ContainsKey(m.Key)) of[m.Key] = m.Value;
            moved.Clear();
        }

        public ScopeTarget Resolve(in WingScope scope)
        {
            moved.Clear();
            switch (scope.Kind)
            {
                case ScopeKind.Wing:
                    return new ScopeTarget { Element = 0, Everyone = true };
                case ScopeKind.Element:
                    if (!InUse(scope.Element)) return Refuse("element " + Letter(scope.Element) + " is empty");
                    return new ScopeTarget { Element = scope.Element };
            }
            picked.Clear();
            if (scope.Members != null)
                foreach (uint id in scope.Members)
                    if (of.ContainsKey(id) && !picked.Contains(id)) picked.Add(id);
            if (picked.Count == 0) return Refuse("nobody selected is in the wing");
            if (picked.Count == order.Count) return new ScopeTarget { Element = 0, Everyone = true };
            for (int e = 0; e < MaxElements; e++)
                if (Count(e) == picked.Count && AllIn(e)) return new ScopeTarget { Element = e };
            int free = -1;
            for (int e = 1; e < MaxElements && free < 0; e++)
                if (Count(e) == 0) free = e;
            if (free < 0) return Refuse("all four elements are in use");
            names[free] = null;   // a reused letter starts without the last element's name
            foreach (uint id in picked)
            {
                int was = of[id];
                moved.Add(new KeyValuePair<uint, int>(id, was));
                of[id] = free;
                if (was > 0 && Count(was) == 0) names[was] = null;
            }
            return new ScopeTarget { Element = free, Detached = true };
        }

        public void Merge(int e)
        {
            if (e <= 0) return;
            foreach (uint id in order)
                if (of[id] == e) of[id] = 0;
            names[e] = null;
        }

        public void MergeAll()
        {
            for (int e = 1; e < MaxElements; e++) Merge(e);
        }

        private bool AllIn(int e)
        {
            foreach (uint id in picked)
                if (of[id] != e) return false;
            return true;
        }

        private static ScopeTarget Refuse(string reason) => new ScopeTarget { Element = -1, Reason = reason };
    }
}
