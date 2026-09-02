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
        /// reflex rather than the behaviour: two reflexes can legitimately fly the same
        /// behaviour, and the transition between them is still worth logging.
        /// </summary>
        public bool SameAs(in WingResolution other) =>
            string.Equals(ReflexId, other.ReflexId, StringComparison.Ordinal);

        public override string ToString() =>
            ReflexId + " (" + Band + " " + Score.ToString("0.00") + ") -> " + BehaviourId;
    }

    /// <summary>One reflex's showing in a resolution pass. Diagnostic only.</summary>
    internal readonly struct WingReflexTrace
    {
        public readonly string Id;
        public readonly WingReflexBand Band;
        public readonly float Score;
        public readonly bool Sticky;
        public readonly bool Won;

        public WingReflexTrace(string id, WingReflexBand band, float score, bool sticky, bool won)
        {
            Id = id;
            Band = band;
            Score = score;
            Sticky = sticky;
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
    /// Everything that used to be a bespoke boolean lives here instead. Hysteresis is one
    /// stickiness bonus applied to the incumbent <i>within its own band</i> — which is why a
    /// break still preempts a recall instantly while a recall no longer flip-flops on the
    /// leash boundary. Minimum holds are one rule rather than three timers.
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

            IWingReflex incumbent = Find(reflexes, activeReflexId);
            bool held = incumbent != null &&
                        incumbent.MinimumSeconds > 0f &&
                        situation.SecondsInBehaviour < incumbent.MinimumSeconds &&
                        Eligible(incumbent, smartMode);

            IWingReflex winner = null;
            float winningScore = 0f;
            WingReflexBand winningBand = WingReflexBand.Task;

            // Reflexes arrive sorted by band, so the first band that produces a score is the
            // answer and everything after it can be skipped - except when a trace was asked
            // for, where the whole ladder is the point.
            for (int i = 0; i < reflexes.Count; i++)
            {
                IWingReflex reflex = reflexes[i];

                if (winner != null && reflex.Band > winningBand && trace == null) break;
                if (!Eligible(reflex, smartMode)) continue;

                bool sticky = incumbent != null &&
                              string.Equals(reflex.Id, incumbent.Id, StringComparison.Ordinal);

                // Hysteresis is the reflex's own business: it is told whether it is the one
                // in control and widens its own release threshold accordingly. The arbiter
                // deliberately adds no incumbency bonus of its own - a blanket bonus cannot
                // express "recall at the leash, release at half of it", and having both
                // mechanisms would be two ways to tune one behaviour.
                float score = WingAi.SafeScore(reflex, in situation, sticky);

                trace?.Add(new WingReflexTrace(reflex.Id, reflex.Band, score, sticky, won: false));

                if (score <= 0f) continue;
                if (winner != null && reflex.Band > winningBand) continue;

                if (winner == null || score > winningScore)
                {
                    winner = reflex;
                    winningScore = score;
                    winningBand = reflex.Band;
                }
            }

            // A minimum hold survives anything in its own band or below it, and nothing in a
            // stronger one. Holding against a lower band would make the hold immunity, which
            // is exactly the failure the bands exist to prevent.
            if (held && (winner == null || winner.Band >= incumbent.Band))
                winner = incumbent;

            if (winner == null) return Fallback;

            MarkWinner(trace, winner.Id);

            float reported = ReferenceEquals(winner, incumbent) && held ? 1f : winningScore;
            return new WingResolution(winner.BehaviourId, winner.Id, winner.Band, reported);
        }

        private static bool Eligible(IWingReflex reflex, bool smartMode) =>
            smartMode || !reflex.RequiresSmartMode;

        private static IWingReflex Find(IReadOnlyList<IWingReflex> reflexes, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < reflexes.Count; i++)
            {
                if (string.Equals(reflexes[i].Id, id, StringComparison.Ordinal)) return reflexes[i];
            }
            return null;
        }

        private static void MarkWinner(List<WingReflexTrace> trace, string id)
        {
            if (trace == null) return;

            for (int i = 0; i < trace.Count; i++)
            {
                if (!string.Equals(trace[i].Id, id, StringComparison.Ordinal)) continue;
                WingReflexTrace t = trace[i];
                trace[i] = new WingReflexTrace(t.Id, t.Band, t.Score, t.Sticky, won: true);
                return;
            }
        }
    }
}
