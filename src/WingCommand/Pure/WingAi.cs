using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// The registry of behaviour reflexes, and the mod's one public extension point.
    ///
    /// Deliberately public where the rest of the assembly is internal. Everything else in
    /// this mod is an implementation detail that may move between releases; this is the
    /// surface another plugin builds against, so it is small on purpose — an interface, a
    /// snapshot struct, a band enum and the two calls below.
    ///
    /// Registration is not ordered: reflexes are held sorted by band and then by
    /// <see cref="IWingReflex.Id"/>, so two plugins loading in either order produce the same
    /// wing behaviour. That is the difference between an extension point and a race.
    /// </summary>
    public static class WingAi
    {
        /// <summary>
        /// Bumped when the reflex contract changes shape. A plugin can compare against it
        /// and decline to register rather than failing at a missing member.
        /// </summary>
        public const int ApiVersion = 1;

        private static readonly List<IWingReflex> reflexes = new List<IWingReflex>();
        private static readonly HashSet<string> faulted = new HashSet<string>();

        /// <summary>
        /// Where a reflex fault is reported. Set once by the plugin host; kept as a delegate
        /// so this file stays engine-free and testable, and so a test can assert that a
        /// throwing reflex was reported rather than swallowed.
        /// </summary>
        public static Action<string, Exception> FaultReporter { get; set; }

        /// <summary>Every registered reflex, in resolution order. Never null.</summary>
        public static IReadOnlyList<IWingReflex> Reflexes => reflexes;

        /// <summary>
        /// Add a reflex. Replaces any existing one with the same <see cref="IWingReflex.Id"/>,
        /// which is what lets a plugin override one of the mod's own — registering
        /// <c>"wingcommand.leash-recall"</c> substitutes your recall logic for ours.
        /// </summary>
        public static void Register(IWingReflex reflex)
        {
            if (reflex == null) throw new ArgumentNullException(nameof(reflex));
            if (string.IsNullOrEmpty(reflex.Id))
                throw new ArgumentException("A reflex needs a stable Id.", nameof(reflex));
            if (string.IsNullOrEmpty(reflex.BehaviourId))
                throw new ArgumentException("A reflex needs a BehaviourId.", nameof(reflex));

            Unregister(reflex.Id);
            reflexes.Add(reflex);

            // Sorted on insert rather than on read: resolution runs per wingman per tick,
            // registration runs a handful of times at startup.
            reflexes.Sort(Compare);
        }

        /// <summary>Remove a reflex by id. True when one was actually removed.</summary>
        public static bool Unregister(string id)
        {
            for (int i = 0; i < reflexes.Count; i++)
            {
                if (!string.Equals(reflexes[i].Id, id, StringComparison.Ordinal)) continue;
                reflexes.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Score a reflex without trusting it. A reflex that throws is reported once and
        /// disabled for the rest of the mission: in a modded game a third party's bug must
        /// degrade its own behaviour, not stop the whole wing from resolving.
        /// </summary>
        internal static float SafeScore(IWingReflex reflex, in WingSituation situation,
                                        bool incumbent)
        {
            if (faulted.Contains(reflex.Id)) return 0f;

            try
            {
                float score = reflex.Score(in situation, incumbent);

                // NaN compares false against everything, so an unguarded one would lose every
                // comparison silently rather than being noticed. Treat it as a fault.
                if (float.IsNaN(score)) throw new InvalidOperationException("Score returned NaN.");

                return score < 0f ? 0f : score > 1f ? 1f : score;
            }
            catch (Exception e)
            {
                Fault(reflex.Id, e);
                return 0f;
            }
        }

        private static void Fault(string id, Exception e)
        {
            if (!faulted.Add(id)) return;
            FaultReporter?.Invoke(id, e);
        }

        /// <summary>True when this reflex has been disabled by a fault.</summary>
        internal static bool IsFaulted(string id) => faulted.Contains(id);

        /// <summary>
        /// Clear fault suppressions. Called at mission start: a reflex that threw because of
        /// one mission's state deserves a fresh chance in the next, and one that is simply
        /// broken will fault again immediately at no real cost.
        /// </summary>
        public static void ResetFaults() => faulted.Clear();

        /// <summary>Drop every registration. For tests and for a full teardown.</summary>
        internal static void Clear()
        {
            reflexes.Clear();
            faulted.Clear();
        }

        private static int Compare(IWingReflex a, IWingReflex b)
        {
            int band = ((int)a.Band).CompareTo((int)b.Band);
            return band != 0 ? band : string.CompareOrdinal(a.Id, b.Id);
        }
    }
}
