using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One immutable decision sent from a member's brain to its aircraft adapter.</summary>
    internal readonly struct WingDecision
    {
        public readonly WingResolution Resolution;
        public readonly WingFlightProfile Flight;
        public readonly int OrderRevision;
        public readonly bool BehaviourChanged;
        public readonly bool NeedsControlUpdate;
        public readonly bool LeavesMissileBreak;

        public WingDecision(in WingResolution resolution, in WingFlightProfile flight, int revision,
            bool changed, bool needsControlUpdate, bool leavesMissileBreak)
        {
            Resolution = resolution;
            Flight = flight;
            OrderRevision = revision;
            BehaviourChanged = changed;
            NeedsControlUpdate = needsControlUpdate;
            LeavesMissileBreak = leavesMissileBreak;
        }
    }

    /// <summary>
    /// Per-aircraft decision memory. WingFidelity supplies mission fidelity, WingArbiter
    /// chooses ownership, influences tune flight, and the aircraft adapter applies the
    /// result. This class holds no aircraft reference and never writes a standing order.
    /// </summary>
    internal sealed class WingMemberBrain
    {
        public WingResolution Current { get; private set; }
        public WingFlightProfile Flight { get; private set; } = WingFlightProfile.Neutral;
        private int appliedRevision = -1;
        private float enteredAt;
        private float nextEvaluation;
        private float lastWarning = float.NegativeInfinity;
        private bool pending = true;

        public bool Defensive => Current.BehaviourId == WingBehaviours.MissileBreak;
        public float SecondsInBehaviour(float now) => Math.Max(0f, now - enteredAt);
        public float SecondsSinceWarning(float now) => float.IsNegativeInfinity(lastWarning)
            ? 999f : Math.Max(0f, now - lastWarning);

        public void RequestEvaluation() => pending = true;

        /// <summary>Urgent telemetry and a lost controller bypass the normal decision cadence.</summary>
        public bool BeginUpdate(float now, bool warned, bool controlLost, bool force, float interval)
        {
            if (warned) lastWarning = now;
            if (!force && !pending && !warned && !Defensive && !controlLost && now < nextEvaluation)
                return false;
            nextEvaluation = now + Math.Max(0f, interval);
            pending = false;
            return true;
        }

        /// <summary>
        /// Read-only evaluation: the adapter may retire an expired one-shot order and
        /// resample before committing. Speculative decisions cannot reset hold clocks.
        /// </summary>
        public WingDecision Evaluate(in WingFlightSituation telemetry, int orderRevision,
            bool controlLost, bool smartMode, List<WingReflexTrace> trace = null)
        {
            WingResolution next = WingArbiter.Resolve(in telemetry.Situation,
                Current.ReflexId, smartMode, WingAi.Reflexes, trace);
            WingFlightProfile flight = WingAi.BlendFlight(in telemetry, smartMode);
            WingResolution current = Current;
            bool changed = !next.SameAs(in current);
            bool retasked = next.BehaviourId == WingBehaviours.Task && appliedRevision != orderRevision;
            return new WingDecision(in next, in flight, orderRevision,
                changed, changed || retasked || controlLost,
                Defensive && next.BehaviourId != WingBehaviours.MissileBreak);
        }

        public void Commit(in WingDecision decision, float now)
        {
            Flight = decision.Flight;
            if (decision.BehaviourChanged) enteredAt = now;
            // Refresh the reason/score even while the same controller stays active.
            // Diagnostics must describe this sample, not its original entry score.
            Current = decision.Resolution;
            if (decision.NeedsControlUpdate) appliedRevision = decision.OrderRevision;
        }

        /// <summary>Keep the committed owner truthful when an extension cannot supply its state.</summary>
        public void FallBackToTask(float now, bool rejectUnavailable = true)
        {
            if (rejectUnavailable) WingAi.RejectBehaviour(Current.ReflexId, Current.BehaviourId);
            Current = new WingResolution(WingBehaviours.Task, string.Empty, WingReflexBand.Task, 1f);
            enteredAt = now;
            RequestEvaluation();
        }
    }
}
