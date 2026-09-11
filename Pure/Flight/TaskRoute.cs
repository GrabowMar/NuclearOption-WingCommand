using System;
using System.Collections;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Current leg first. Refit suspends the plan; a replacement command clears it.</summary>
    internal sealed class TaskRoute<T> : IReadOnlyList<T>
    {
        private readonly List<T> legs = new List<T>();
        private T[] suspended;
        private bool suspendedRepeat;
        public bool Repeat { get; private set; }
        public int Count => legs.Count;
        public T this[int index] => legs[index];
        public void Add(T leg) => legs.Add(leg);
        public bool SetRepeat(bool enabled)
        {
            if (enabled && Count < 2) return false;
            Repeat = enabled;
            return true;
        }
        public void Clear()
        {
            legs.Clear();
            Repeat = false;
            CancelSuspension();
        }
        public void CancelSuspension() => suspended = null;
        public void Suspend(T current)
        {
            suspended = Count > 0 ? legs.ToArray() : new[] { current };
            suspendedRepeat = Repeat;
            legs.Clear();
            Repeat = false;
        }
        public T Restore(Predicate<T> valid, T fallback)
        {
            legs.Clear();
            if (suspended != null)
                foreach (T leg in suspended)
                    if (valid(leg)) legs.Add(leg);
            Repeat = suspended != null && suspendedRepeat && Count > 1;
            CancelSuspension();
            return Count > 0 ? legs[0] : fallback;
        }
        public bool Advance(int startedRevision, int currentRevision, out T next)
        {
            next = default;
            if (startedRevision != currentRevision || Count == 0) return false;
            T completed = legs[0];
            legs.RemoveAt(0);
            if (Repeat) legs.Add(completed);
            if (Count == 0) return false;
            next = legs[0];
            return true;
        }
        public IEnumerator<T> GetEnumerator() => legs.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
