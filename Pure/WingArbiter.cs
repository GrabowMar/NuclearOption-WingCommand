using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Resolved behaviour with diagnostics explaining the choice.</summary>
    internal readonly struct WingResolution
    {
        public readonly string BehaviourId;
        public readonly string ReflexId;
        public readonly WingReflexBand Band;
        public readonly float Score;

        public WingResolution(string behaviourId, string reflexId, WingReflexBand band, float score)
        {
            BehaviourId = behaviourId;
            ReflexId = reflexId;
            Band = band;
            Score = score;
        }

        /// <summary>Compare both reflex and behaviour identities; either may change
        /// independently.</summary>
        public bool SameAs(in WingResolution other) =>
            string.Equals(ReflexId, other.ReflexId, StringComparison.Ordinal) &&
            string.Equals(BehaviourId, other.BehaviourId, StringComparison.Ordinal);

        public override string ToString() =>
            ReflexId + " (" + Band + " " + Score.ToString("0.00") + ") -> " + BehaviourId;
    }

    /// <summary>Diagnostic score trace for one reflex evaluation.</summary>
    internal readonly struct WingReflexTrace
    {
        public readonly string Id;
        public readonly WingReflexBand Band;
        public readonly float Score;
        public readonly bool Won;

        public WingReflexTrace(string id, WingReflexBand band, float score, bool won)
        {
            Id = id;
            Band = band;
            Score = score;
            Won = won;
        }
    }

    /// <summary>Resolve lowest band first, then highest score, with stable-ID ties. Reflexes define
    /// incumbent hysteresis; the arbiter enforces minimum holds without adding a second incumbency
    /// bonus.</summary>
    internal static class WingArbiter
    {
        /// <summary>Non-null task fallback for an empty registry; normal plugin registration supplies an
        /// always-scoring Task reflex.</summary>
        private static readonly WingResolution Fallback =
            new WingResolution(WingBehaviours.Task, string.Empty, WingReflexBand.Task, 1f);

        public static WingResolution Resolve(
            in WingSituation situation,
            string activeReflexId,
            bool smartMode,
            IReadOnlyList<IWingReflex> reflexes,
            List<WingReflexTrace> trace = null)
        {
            trace?.Clear();
            if (reflexes == null || reflexes.Count == 0) return Fallback;

            WingAi.ReflexSnapshot incumbent = default, winner = default;
            bool hasIncumbent = false, hasWinner = false, held = false;
            float winningScore = 0f;
            WingReflexBand winningBand = WingReflexBand.Task;
            bool winnerInterruptsHold = false;

            // Evaluate all candidates and rank by band, score, and stable ID, independent of insertion
            // order.
            for (int i = 0; i < reflexes.Count; i++)
            {
                if (!WingAi.TrySnapshot(reflexes[i], out WingAi.ReflexSnapshot reflex)) continue;
                if (!smartMode && reflex.RequiresSmartMode) continue;
                bool sticky = string.Equals(reflex.Id, activeReflexId, StringComparison.Ordinal);
                if (sticky)
                {
                    incumbent = reflex;
                    hasIncumbent = true;
                    held = reflex.MinimumSeconds > 0f && situation.SecondsInBehaviour < reflex.MinimumSeconds &&
                        WingAi.CanHold(in reflex, in situation);
                }

                // Let each reflex express hysteresis through incumbent; do not add a blanket score
                // bonus.
                float score = WingAi.SafeScore(in reflex, in situation, sticky);
                bool interruptsHold = score > 0f && WingAi.InterruptsHold(in reflex);
                if (WingAi.IsFaulted(in reflex)) score = 0f;

                trace?.Add(new WingReflexTrace(reflex.Id, reflex.Band, score, won: false));

                if (score <= 0f) continue;
                if (hasWinner && reflex.Band > winningBand) continue;

                if (!hasWinner || reflex.Band < winningBand || score > winningScore ||
                    (score == winningScore && string.CompareOrdinal(reflex.Id, winner.Id) < 0))
                {
                    winner = reflex;
                    hasWinner = true;
                    winningScore = score;
                    winningBand = reflex.Band;
                    winnerInterruptsHold = interruptsHold;
                }
            }

            // Recheck incumbent faults raised during this evaluation before retaining its hold.
            held = held && !WingAi.IsFaulted(in incumbent);
            bool emergency = hasWinner && hasIncumbent && winner.Band <= incumbent.Band &&
                             winnerInterruptsHold;
            if (held && !emergency && (!hasWinner || winner.Band >= incumbent.Band))
            {
                winner = incumbent;
                hasWinner = true;
            }

            if (!hasWinner) return Fallback;

            MarkWinner(trace, winner.Id);

            float reported = hasIncumbent && ReferenceEquals(winner.Source, incumbent.Source) && held ? 1f : winningScore;
            return new WingResolution(winner.BehaviourId, winner.Id, winner.Band, reported);
        }

        private static void MarkWinner(List<WingReflexTrace> trace, string id)
        {
            if (trace == null) return;

            for (int i = 0; i < trace.Count; i++)
            {
                if (!string.Equals(trace[i].Id, id, StringComparison.Ordinal)) continue;
                WingReflexTrace t = trace[i];
                trace[i] = new WingReflexTrace(t.Id, t.Band, t.Score, won: true);
                return;
            }
        }
    }
}
