using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Registration identity survives only until its next replacement or removal.</summary>
    internal sealed class BehaviourFactoryRegistry<TContext, TState> where TState : class
    {
        internal sealed class Registration
        {
            private readonly Func<TContext, TState> factory;
            internal Registration(Func<TContext, TState> factory) => this.factory = factory;
            internal TState Create(TContext context) => factory(context);
        }

        private readonly Dictionary<string, Registration> registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);

        public void Register(string behaviourId, Func<TContext, TState> factory)
        {
            if (string.IsNullOrEmpty(behaviourId))
                throw new ArgumentException("A behaviour needs an id.", nameof(behaviourId));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            // Even re-registering the same delegate is a new registration lifetime.
            registrations[behaviourId] = new Registration(factory);
        }

        public Registration Find(string behaviourId) =>
            !string.IsNullOrEmpty(behaviourId) && registrations.TryGetValue(behaviourId, out Registration entry)
                ? entry : null;

        public bool Remove(string behaviourId) => registrations.Remove(behaviourId);
        public void Clear() => registrations.Clear();

        public bool RemoveIfCurrent(string behaviourId, Registration registration)
        {
            // A factory may replace its own registration before throwing. Its failure
            // must not remove the successor that a later evaluation should observe.
            if (!ReferenceEquals(Find(behaviourId), registration)) return false;
            return Remove(behaviourId);
        }
    }

    /// <summary>Each aircraft owns its state objects; only factory registrations are shared.</summary>
    internal sealed class BehaviourStateCache<TContext, TState> where TState : class
    {
        private readonly struct Entry
        {
            public readonly BehaviourFactoryRegistry<TContext, TState>.Registration Registration;
            public readonly TState State;
            public Entry(BehaviourFactoryRegistry<TContext, TState>.Registration registration, TState state)
            { Registration = registration; State = state; }
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public bool Contains(string behaviourId) =>
            !string.IsNullOrEmpty(behaviourId) && entries.ContainsKey(behaviourId);

        public void ObserveMissing(string behaviourId) => entries[behaviourId] = new Entry(null, null);

        public bool IsCurrent(string behaviourId,
            BehaviourFactoryRegistry<TContext, TState>.Registration registration) =>
            entries.TryGetValue(behaviourId, out Entry entry)
                ? ReferenceEquals(entry.Registration, registration) : registration == null;

        public TState GetOrCreate(string behaviourId,
            BehaviourFactoryRegistry<TContext, TState>.Registration registration, TContext context)
        {
            if (entries.TryGetValue(behaviourId, out Entry entry) &&
                ReferenceEquals(entry.Registration, registration)) return entry.State;

            // Publish only after construction succeeds. A throwing replacement does
            // not overwrite the last known state, and is never mistaken for a hit.
            TState state = registration.Create(context);
            entries[behaviourId] = new Entry(registration, state);
            return state;
        }
    }
}
