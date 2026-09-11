using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Immutable brain decision passed to the aircraft adapter.</summary>
    internal readonly struct WingDecision
    {
        public readonly WingResolution Resolution;
        public readonly WingFlightProfile Flight;
        public readonly int OrderRevision;
        public readonly bool BehaviourChanged;
        public readonly bool NeedsControlUpdate;
        public readonly bool LeavesMissileBreak;
        public readonly bool DefenceCleared;
        public readonly bool DefencePending;

        public WingDecision(in WingResolution resolution, in WingFlightProfile flight, int revision,
            bool changed, bool needsControlUpdate, bool leavesMissileBreak,
            bool defenceCleared, bool defencePending)
        {
            Resolution = resolution;
            Flight = flight;
            OrderRevision = revision;
            BehaviourChanged = changed;
            NeedsControlUpdate = needsControlUpdate;
            LeavesMissileBreak = leavesMissileBreak;
            DefenceCleared = defenceCleared;
            DefencePending = defencePending;
        }
    }

    /// <summary>Per-aircraft decision memory without live aircraft references or order writes. Fidelity
    /// gates work, arbitration selects ownership, influences tune flight, and the adapter applies
    /// results.</summary>
    internal sealed class WingMemberBrain
    {
        public WingResolution Current { get; private set; }
        public WingFlightProfile Flight { get; private set; } = WingFlightProfile.Neutral;
        private int appliedRevision = -1;
        private float enteredAt;
        private float nextEvaluation;
        private float lastWarning = float.NegativeInfinity;
        private bool pending = true;
        private bool defencePending;

        public bool Defensive => Current.BehaviourId == WingBehaviours.MissileBreak;
        public float SecondsInBehaviour(float now) => Math.Max(0f, now - enteredAt);
        public float SecondsSinceWarning(float now) => float.IsNegativeInfinity(lastWarning)
            ? 999f : Math.Max(0f, now - lastWarning);

        public void RequestEvaluation() => pending = true;

        /// <summary>Bypass normal decision cadence for urgent telemetry or lost control.</summary>
        public bool BeginUpdate(float now, bool warned, bool controlLost, bool force, float interval)
        {
            if (warned) lastWarning = now;
            if (!force && !pending && !warned && !Defensive && !controlLost && now < nextEvaluation)
                return false;
            nextEvaluation = now + Math.Max(0f, interval);
            pending = false;
            return true;
        }

        /// <summary>Evaluate without mutation so the adapter can retire stale tasks and resample before
        /// commit; speculation must not reset hold clocks.</summary>
        public WingDecision Evaluate(in WingFlightSituation telemetry, int orderRevision,
            bool controlLost, bool smartMode, List<WingReflexTrace> trace = null)
        {
            WingResolution next = WingArbiter.Resolve(in telemetry.Situation,
                Current.ReflexId, smartMode, WingAi.Reflexes, trace);
            WingFlightProfile flight = WingAi.BlendFlight(in telemetry, smartMode);
            WingResolution current = Current;
            bool changed = !next.SameAs(in current);
            bool retasked = next.BehaviourId == WingBehaviours.Task && appliedRevision != orderRevision;
            bool encountered = defencePending || next.BehaviourId == WingBehaviours.MissileBreak ||
                (next.BehaviourId == WingBehaviours.TerrainAbort && telemetry.Situation.MissileWarned);
            bool cleared = encountered && !telemetry.Situation.MissileWarned &&
                telemetry.Situation.SecondsSinceMissileWarning >= WingTuning.PanicClearSeconds &&
                next.BehaviourId != WingBehaviours.MissileBreak;
            return new WingDecision(in next, in flight, orderRevision,
                changed, changed || retasked || controlLost,
                Defensive && next.BehaviourId != WingBehaviours.MissileBreak,
                cleared, encountered && !cleared);
        }

        public void Commit(in WingDecision decision, float now)
        {
            Flight = decision.Flight;
            defencePending = decision.DefencePending;
            if (decision.BehaviourChanged) enteredAt = now;
            // Refresh diagnostic score and reason even when controller ownership is unchanged.
            Current = decision.Resolution;
            if (decision.NeedsControlUpdate) appliedRevision = decision.OrderRevision;
        }

        /// <summary>Commit task fallback when an extension cannot provide usable state.</summary>
        public void FallBackToTask(float now, bool rejectUnavailable = true)
        {
            if (rejectUnavailable) WingAi.RejectBehaviour(Current.ReflexId, Current.BehaviourId);
            Current = new WingResolution(WingBehaviours.Task, string.Empty, WingReflexBand.Task, 1f);
            enteredAt = now;
            RequestEvaluation();
        }
    }
}
