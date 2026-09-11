using System;

namespace WingCommand
{
    /// <summary>Versioned standing intent; completions may retire only their starting revision.</summary>
    internal sealed class StandingOrder<T>
    {
        private readonly Func<T, T, bool> sameIntent;
        public T Current { get; private set; }
        public int Revision { get; private set; }

        public StandingOrder(T initial, Func<T, T, bool> sameIntent)
        {
            Current = initial;
            this.sameIntent = sameIntent ?? throw new ArgumentNullException(nameof(sameIntent));
        }

        public bool Set(T next)
        {
            if (sameIntent(Current, next)) return false;
            Current = next;
            Revision++;
            return true;
        }

        public bool TryComplete(int startedRevision, T next, out bool changed)
        {
            changed = false;
            if (Revision != startedRevision) return false;
            changed = Set(next);
            return true;
        }
    }
}
