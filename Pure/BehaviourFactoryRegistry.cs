using System;
using System.Collections.Generic;

namespace WingCommand
{
 /// <summary>Factory registrations have identity until replaced or removed.</summary>
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

            // Registering the same delegate still creates a fresh lifetime.
            registrations[behaviourId] = new Registration(factory);
        }

        public Registration Find(string behaviourId) =>
            !string.IsNullOrEmpty(behaviourId) && registrations.TryGetValue(behaviourId, out Registration entry)
                ? entry : null;

        public bool Remove(string behaviourId) => registrations.Remove(behaviourId);
        public void Clear() => registrations.Clear();

        public bool RemoveIfCurrent(string behaviourId, Registration registration)
        {
            // Remove only the failed registration, preserving any successor the factory installed
            // before throwing.
            if (!ReferenceEquals(Find(behaviourId), registration)) return false;
            return Remove(behaviourId);
        }
    }

 /// <summary>Caches state per aircraft while sharing only registrations.</summary>
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

            // Publish a new state only after successful construction; failed replacements preserve the
            // previous cache.
            TState state = registration.Create(context);
            entries[behaviourId] = new Entry(registration, state);
            return state;
        }
    }
}
