using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Up to three explicitly created selection snapshots, scoped to one mission.</summary>
    internal sealed class FlightGroups<T>
    {
        public const int Count = 3;
        private readonly string[] names = new string[Count];
        private readonly List<T>[] members = { new List<T>(), new List<T>(), new List<T>() };
        public string Name(int index) => names[index];
        public bool Exists(int index) => !string.IsNullOrEmpty(names[index]);
        public void Save(int index, string name, IEnumerable<T> selection)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return;
            names[index] = trimmed.Substring(0, Math.Min(12, trimmed.Length));
            members[index].Clear();
            foreach (T member in selection)
                if (!members[index].Contains(member)) members[index].Add(member);
        }
        public IReadOnlyList<T> Members(int index, Predicate<T> valid)
        {
            members[index].RemoveAll(member => !valid(member));
            return members[index];
        }
        public void Clear(int index)
        {
            members[index].Clear();
            names[index] = null;
        }
        public void Reset()
        {
            for (int i = 0; i < Count; i++) Clear(i);
        }
    }
}
