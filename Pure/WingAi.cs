using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WingCommand
{
    /// <summary>Public registry of exclusive reflexes and composable flight influences.</summary>
    public static class WingAi
    {
        public const int ApiVersion = 1;

        internal readonly struct ReflexSnapshot
        {
            public readonly IWingReflex Source;
            public readonly string Id, BehaviourId;
            public readonly WingReflexBand Band;
            public readonly float MinimumSeconds;
            public readonly bool RequiresSmartMode;
            public ReflexSnapshot(IWingReflex source, string id, string behaviourId,
                WingReflexBand band, float minimumSeconds, bool requiresSmartMode)
            {
                Source = source; Id = id; BehaviourId = behaviourId; Band = band;
                MinimumSeconds = minimumSeconds; RequiresSmartMode = requiresSmartMode;
            }
        }

        private readonly struct InfluenceRegistration
        {
            public readonly IWingInfluence Source;
            public readonly string Id;
            public InfluenceRegistration(IWingInfluence source, string id) { Source = source; Id = id; }
        }

        // Copy on registration: callbacks cannot mutate an evaluation already in progress.
        private static ReflexSnapshot[] registrations = Array.Empty<ReflexSnapshot>();
        private static IReadOnlyList<IWingReflex> reflexes = Array.Empty<IWingReflex>();
        private static InfluenceRegistration[] influences = Array.Empty<InfluenceRegistration>();
        private static readonly HashSet<string> faulted = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> unavailableBehaviours = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> faultedInfluences = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<object> faultedInstances = new HashSet<object>(ReferenceComparer.Instance);

        /// <summary>Host diagnostics. A reporter fault never escapes into flight decisions.</summary>
        public static Action<string, Exception> FaultReporter { get; set; }
        /// <summary>Read-only registration snapshot, ordered by band and stable Id.</summary>
        public static IReadOnlyList<IWingReflex> Reflexes => reflexes;

        /// <summary>
        /// Add or replace by stable Id. Validate metadata before replacing an existing
        /// registration; a throwing extension is reported and ignored.
        /// </summary>
        public static void Register(IWingReflex reflex)
        {
            if (reflex == null) throw new ArgumentNullException(nameof(reflex));
            if (!TrySnapshot(reflex, out ReflexSnapshot candidate, registering: true)) return;
            var updated = new List<ReflexSnapshot>(registrations);
            updated.RemoveAll(item => string.Equals(item.Id, candidate.Id, StringComparison.Ordinal));
            updated.Add(candidate);
            updated.Sort((a, b) => a.Band != b.Band
                ? a.Band.CompareTo(b.Band) : string.CompareOrdinal(a.Id, b.Id));
            Publish(updated);
            faulted.Remove(candidate.Id);
            unavailableBehaviours.Remove(candidate.Id);
            faultedInstances.Remove(reflex);
        }

        /// <summary>Remove by cached identity, without invoking extension getters.</summary>
        public static bool Unregister(string id)
        {
            var updated = new List<ReflexSnapshot>(registrations);
            if (updated.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) == 0) return false;
            Publish(updated);
            return true;
        }

        private static void Publish(List<ReflexSnapshot> updated)
        {
            registrations = updated.ToArray();
            var sources = new IWingReflex[registrations.Length];
            for (int i = 0; i < sources.Length; i++) sources[i] = registrations[i].Source;
            reflexes = Array.AsReadOnly(sources);
        }

        // Metadata is extension code too. Read once per decision so ranking, hold
        // validity and the emitted behavior all use the same coherent values.
        internal static bool TrySnapshot(IWingReflex reflex, out ReflexSnapshot snapshot, bool registering = false)
        {
            snapshot = default;
            if (reflex == null || (!registering && faultedInstances.Contains(reflex))) return false;
            string id = null;
            if (!registering)
                foreach (ReflexSnapshot item in registrations)
                    if (ReferenceEquals(item.Source, reflex)) { id = item.Id; break; }
            try
            {
                id ??= reflex.Id;
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("A reflex needs a stable Id.");
                if (!registering && faulted.Contains(id)) return false;
                WingReflexBand band = reflex.Band;
                string behaviourId = reflex.BehaviourId;
                float minimum = reflex.MinimumSeconds;
                bool smart = reflex.RequiresSmartMode;
                if (band < WingReflexBand.Survival || band > WingReflexBand.Task)
                    throw new InvalidOperationException("A reflex needs a valid precedence band.");
                if (string.IsNullOrWhiteSpace(behaviourId))
                    throw new InvalidOperationException("A reflex needs a BehaviourId.");
                if (minimum < 0f || float.IsNaN(minimum) || float.IsInfinity(minimum))
                    throw new InvalidOperationException("MinimumSeconds must be finite and nonnegative.");
                snapshot = new ReflexSnapshot(reflex, id, behaviourId, band, minimum, smart);
                return true;
            }
            catch (Exception e)
            {
                FaultInstance(reflex, id, e, faulted, disableId: !registering);
                return false;
            }
        }

        internal static float SafeScore(in ReflexSnapshot reflex, in WingSituation situation, bool incumbent)
        {
            if (IsFaulted(in reflex)) return 0f;
            try
            {
                float score = reflex.Source.Score(in situation, incumbent);
                if (float.IsNaN(score) || float.IsInfinity(score))
                    throw new InvalidOperationException("Score must be finite.");
                return score < 0f ? 0f : score > 1f ? 1f : score;
            }
            catch (Exception e) { FaultInstance(reflex.Source, reflex.Id, e, faulted); return 0f; }
        }

        internal static bool IsFaulted(string id) => id != null && faulted.Contains(id);
        internal static bool IsFaulted(in ReflexSnapshot reflex) =>
            faultedInstances.Contains(reflex.Source) || IsFaulted(reflex.Id);

        internal static void RejectBehaviour(string reflexId, string behaviourId)
        {
            // Missing factories are recoverable availability failures. Never replace
            // a genuine provider fault with this weaker, automatically retryable reason.
            if (string.IsNullOrEmpty(reflexId) ||
                (faulted.Contains(reflexId) && !unavailableBehaviours.ContainsKey(reflexId))) return;
            unavailableBehaviours[reflexId] = behaviourId;
            if (!faulted.Add(reflexId)) return;
            ReportFault(reflexId, new InvalidOperationException("Reflex selected an unavailable behavior: " + behaviourId));
        }

        internal static void RestoreBehaviour(string behaviourId)
        {
            var restored = new List<string>();
            foreach (KeyValuePair<string, string> unavailable in unavailableBehaviours)
                if (string.Equals(unavailable.Value, behaviourId, StringComparison.Ordinal)) restored.Add(unavailable.Key);
            foreach (string id in restored)
            { unavailableBehaviours.Remove(id); faulted.Remove(id); }
        }

        internal static bool CanHold(in ReflexSnapshot reflex, in WingSituation situation)
        {
            if (IsFaulted(in reflex)) return false;
            try { return !(reflex.Source is IWingReflexLifecycle lifecycle) || lifecycle.CanHold(in situation); }
            catch (Exception e) { FaultInstance(reflex.Source, reflex.Id, e, faulted); return false; }
        }

        internal static bool InterruptsHold(in ReflexSnapshot reflex)
        {
            if (IsFaulted(in reflex)) return false;
            try { return reflex.Source is IWingReflexLifecycle lifecycle && lifecycle.InterruptsMinimumHold; }
            catch (Exception e) { FaultInstance(reflex.Source, reflex.Id, e, faulted); return false; }
        }

        /// <summary>Add or replace a flight influence, ordered by its cached stable Id.</summary>
        public static void RegisterInfluence(IWingInfluence influence)
        {
            if (influence == null) throw new ArgumentNullException(nameof(influence));
            string id = null;
            try
            {
                id = influence.Id;
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("An influence needs a stable Id.");
                _ = influence.RequiresSmartMode;
            }
            catch (Exception e)
            { FaultInstance(influence, id, e, faultedInfluences, disableId: false); return; }
            var updated = new List<InfluenceRegistration>(influences);
            updated.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            updated.Add(new InfluenceRegistration(influence, id));
            updated.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            influences = updated.ToArray();
            faultedInfluences.Remove(id);
            faultedInstances.Remove(influence);
        }

        public static bool UnregisterInfluence(string id)
        {
            var updated = new List<InfluenceRegistration>(influences);
            if (updated.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) == 0) return false;
            influences = updated.ToArray();
            return true;
        }

        internal static WingFlightProfile BlendFlight(in WingFlightSituation situation, bool smartMode)
        {
            float capture = 1f, spacing = 1f, damping = 1f, bank = 1f;
            foreach (InfluenceRegistration registration in influences)
            {
                IWingInfluence influence = registration.Source;
                if (faultedInstances.Contains(influence) || faultedInfluences.Contains(registration.Id)) continue;
                WingFlightContribution contribution;
                try
                {
                    if (!smartMode && influence.RequiresSmartMode) continue;
                    contribution = influence.Evaluate(in situation);
                    if (!contribution.IsFinite) throw new InvalidOperationException("Flight contribution must be finite.");
                }
                catch (Exception e)
                { FaultInstance(influence, registration.Id, e, faultedInfluences); continue; }
                float weight = WingFlightProfile.Clamp(contribution.Weight, 0f, 1f);
                capture += (WingFlightProfile.Clamp(contribution.CaptureGain, 0.65f, 1.35f) - 1f) * weight;
                spacing += (WingFlightProfile.Clamp(contribution.SpacingScale, 0.85f, 1.6f) - 1f) * weight;
                damping += (WingFlightProfile.Clamp(contribution.DampingScale, 1f, 1.5f) - 1f) * weight;
                bank = Math.Min(bank, 1f + (WingFlightProfile.Clamp(contribution.BankScale, 0.65f, 1f) - 1f) * weight);
            }
            return new WingFlightProfile(capture, spacing, damping, bank);
        }

        private static void FaultInstance(object source, string id, Exception error, HashSet<string> ids, bool disableId = true)
        {
            if (!faultedInstances.Add(source)) return;
            if (disableId && ReferenceEquals(ids, faulted) && id != null) unavailableBehaviours.Remove(id);
            if (disableId && !string.IsNullOrEmpty(id) && !ids.Add(id)) return;
            ReportFault(id ?? source.GetType().FullName, error);
        }

        private static void ReportFault(string id, Exception error)
        {
            try { FaultReporter?.Invoke(id, error); }
            catch (Exception) { /* Diagnostics cannot interrupt the surviving extensions. */ }
        }

        /// <summary>Retry quarantined extensions at the start of a new mission.</summary>
        public static void ResetFaults()
        { faulted.Clear(); faultedInfluences.Clear(); faultedInstances.Clear(); unavailableBehaviours.Clear(); }

        internal static void Clear()
        {
            registrations = Array.Empty<ReflexSnapshot>();
            reflexes = Array.Empty<IWingReflex>();
            influences = Array.Empty<InfluenceRegistration>();
            ResetFaults();
        }

        // Never invoke plugin Equals/GetHashCode while quarantining a faulty plugin.
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
