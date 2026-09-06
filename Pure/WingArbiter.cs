using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>What the arbiter decided, and enough of why to log or draw it.</summary>
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

        /// <summary>
        /// Whether this is the same decision as the one already in force. Compared on the
        /// reflex and behaviour: different reflexes can fly the same behaviour, and an
        /// extension can change its behaviour while retaining its reflex identity.
        /// </summary>
        public bool SameAs(in WingResolution other) =>
            string.Equals(ReflexId, other.ReflexId, StringComparison.Ordinal) &&
            string.Equals(BehaviourId, other.BehaviourId, StringComparison.Ordinal);

        public override string ToString() =>
            ReflexId + " (" + Band + " " + Score.ToString("0.00") + ") -> " + BehaviourId;
    }

    /// <summary>One reflex's showing in a resolution pass. Diagnostic only.</summary>
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

    /// <summary>
    /// Picks which behaviour a wingman flies this tick.
    ///
    /// Two rules, in this order, and the order is the whole design:
    ///
    /// <list type="number">
    /// <item><b>Bands are absolute.</b> The lowest-numbered band with anything to say wins
    /// outright. Scores are never compared across bands, so no amount of mistuning — ours or
    /// a third party's — can put a formation tweak ahead of a missile break.</item>
    /// <item><b>Score decides inside a band.</b> Highest wins; ties break on id, so the
    /// answer never depends on registration order.</item>
    /// </list>
    ///
    /// Everything that used to be a bespoke boolean lives here instead. Hysteresis is
    /// declared by each reflex rather than applied from outside: a reflex is told whether it
    /// is the one in control and widens its own release threshold, which is how a stateless
    /// reflex expresses "recall at the leash, release at half of it" — and why a break still
    /// preempts a recall instantly. Minimum holds are one rule rather than three timers.
    /// </summary>
    internal static class WingArbiter
    {
        /// <summary>
        /// The fallback when nothing is registered at all. Not reachable in the built
        /// plugin — the Task reflex always scores — but a resolution has to be total, and a
        /// null behaviour id would be a crash rather than a degraded wingman.
        /// </summary>
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

            // Evaluate the complete set. Rank explicitly by band, score and stable Id;
            // callers and extensions do not have to preserve registry insertion order.
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

                // Hysteresis is the reflex's own business: it is told whether it is the one
                // in control and widens its own release threshold accordingly. The arbiter
                // deliberately adds no incumbency bonus of its own - a blanket bonus cannot
                // express "recall at the leash, release at half of it", and having both
                // mechanisms would be two ways to tune one behaviour.
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

            // A score/lifecycle fault may have disabled the incumbent during this pass.
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
