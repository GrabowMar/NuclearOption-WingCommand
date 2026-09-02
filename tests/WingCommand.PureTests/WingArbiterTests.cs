using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The arbiter is the one place that decides what a wingman does, so these tests are
    /// mostly about the guarantees rather than the arithmetic: that bands really are
    /// absolute, that registration order really cannot matter, and that somebody else's
    /// broken reflex really cannot stop the wing from resolving.
    /// </summary>
    [Collection("WingAi")]
    public class WingArbiterTests : IDisposable
    {
        public WingArbiterTests() => WingAi.Clear();
        public void Dispose() => WingAi.Clear();

        /// <summary>A reflex with a fixed score, for exercising the ladder itself.</summary>
        private sealed class Fake : IWingReflex
        {
            private readonly float score;
            private readonly float incumbentScore;

            public Fake(string id, WingReflexBand band, float score,
                        float minimumSeconds = 0f, bool smartOnly = false,
                        float? incumbentScore = null)
            {
                Id = id;
                Band = band;
                this.score = score;
                this.incumbentScore = incumbentScore ?? score;
                MinimumSeconds = minimumSeconds;
                RequiresSmartMode = smartOnly;
            }

            public string Id { get; }
            public WingReflexBand Band { get; }
            public string BehaviourId => "behaviour." + Id;
            public float MinimumSeconds { get; }
            public bool RequiresSmartMode { get; }

            public float Score(in WingSituation s, bool incumbent) =>
                incumbent ? incumbentScore : score;
        }

        private sealed class Exploding : IWingReflex
        {
            public string Id => "test.exploding";
            public WingReflexBand Band => WingReflexBand.Survival;
            public string BehaviourId => "behaviour.exploding";
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent) =>
                throw new InvalidOperationException("boom");
        }

        private static WingResolution Resolve(WingSituation situation, string active = null,
                                              bool smart = true) =>
            WingArbiter.Resolve(in situation, active, smart, WingAi.Reflexes);

        // ------------------------------------------------------------- bands are absolute

        [Fact]
        public void LowerBandWinsEvenWithAFarLowerScore()
        {
            WingAi.Register(new Fake("a.survival", WingReflexBand.Survival, 0.01f));
            WingAi.Register(new Fake("b.task", WingReflexBand.Task, 1f));

            Assert.Equal("a.survival", Resolve(new WingSituation()).ReflexId);
        }

        [Fact]
        public void AHigherBandCannotBeReachedWhileALowerOneScores()
        {
            WingAi.Register(new Fake("a.safety", WingReflexBand.Safety, 0.2f));
            WingAi.Register(new Fake("b.cohesion", WingReflexBand.Cohesion, 1f));
            WingAi.Register(new Fake("c.task", WingReflexBand.Task, 1f));

            Assert.Equal(WingReflexBand.Safety, Resolve(new WingSituation()).Band);
        }

        [Fact]
        public void AReflexScoringZeroStandsDown()
        {
            WingAi.Register(new Fake("a.survival", WingReflexBand.Survival, 0f));
            WingAi.Register(new Fake("b.task", WingReflexBand.Task, 1f));

            Assert.Equal("b.task", Resolve(new WingSituation()).ReflexId);
        }

        // ------------------------------------------------------ order cannot change things

        [Fact]
        public void RegistrationOrderDoesNotChangeTheOutcome()
        {
            WingAi.Register(new Fake("z.late", WingReflexBand.Cohesion, 0.5f));
            WingAi.Register(new Fake("a.early", WingReflexBand.Cohesion, 0.9f));
            string forwards = Resolve(new WingSituation()).ReflexId;

            WingAi.Clear();
            WingAi.Register(new Fake("a.early", WingReflexBand.Cohesion, 0.9f));
            WingAi.Register(new Fake("z.late", WingReflexBand.Cohesion, 0.5f));

            Assert.Equal(forwards, Resolve(new WingSituation()).ReflexId);
            Assert.Equal("a.early", forwards);
        }

        [Fact]
        public void EqualScoresInABandBreakOnIdNotInsertionOrder()
        {
            WingAi.Register(new Fake("z.second", WingReflexBand.Cohesion, 0.5f));
            WingAi.Register(new Fake("a.first", WingReflexBand.Cohesion, 0.5f));

            Assert.Equal("a.first", Resolve(new WingSituation()).ReflexId);
        }

        [Fact]
        public void RegisteringTheSameIdReplacesRatherThanDuplicates()
        {
            WingAi.Register(new Fake("shared", WingReflexBand.Cohesion, 0.2f));
            WingAi.Register(new Fake("shared", WingReflexBand.Survival, 0.9f));

            Assert.Single(WingAi.Reflexes);
            Assert.Equal(WingReflexBand.Survival, Resolve(new WingSituation()).Band);
        }

        // ------------------------------------------------------------------ minimum holds

        [Fact]
        public void AMinimumHoldKeepsControlAfterItsOwnScoreFalls()
        {
            WingAi.Register(new Fake("a.break", WingReflexBand.Survival, 0f, minimumSeconds: 2f));
            WingAi.Register(new Fake("b.task", WingReflexBand.Task, 1f));

            WingSituation held = new WingSituation(secondsInBehaviour: 1f);
            Assert.Equal("a.break", Resolve(held, active: "a.break").ReflexId);

            WingSituation expired = new WingSituation(secondsInBehaviour: 2.5f);
            Assert.Equal("b.task", Resolve(expired, active: "a.break").ReflexId);
        }

        [Fact]
        public void AMinimumHoldIsNotImmunityToAStrongerBand()
        {
            WingAi.Register(new Fake("a.survival", WingReflexBand.Survival, 1f));
            WingAi.Register(new Fake("b.cohesion", WingReflexBand.Cohesion, 0f, minimumSeconds: 5f));

            WingSituation s = new WingSituation(secondsInBehaviour: 0.5f);
            Assert.Equal("a.survival", Resolve(s, active: "b.cohesion").ReflexId);
        }

        // -------------------------------------------------------------------- hysteresis

        [Fact]
        public void AnIncumbentCanHoldOnAScoreThatWouldNotHaveWonItControl()
        {
            // The shape every two-threshold reflex uses: nothing when asked cold, something
            // when asked as the incumbent.
            WingAi.Register(new Fake("a.recall", WingReflexBand.Cohesion, 0f, incumbentScore: 0.3f));
            WingAi.Register(new Fake("b.task", WingReflexBand.Task, 1f));

            Assert.Equal("b.task", Resolve(new WingSituation()).ReflexId);
            Assert.Equal("a.recall", Resolve(new WingSituation(), active: "a.recall").ReflexId);
        }

        // ---------------------------------------------------------------- fault isolation

        [Fact]
        public void AThrowingReflexIsDisabledAndTheWingStillResolves()
        {
            string reported = null;
            WingAi.FaultReporter = (id, e) => reported = id;

            WingAi.Register(new Exploding());
            WingAi.Register(new Fake("z.task", WingReflexBand.Task, 1f));

            Assert.Equal("z.task", Resolve(new WingSituation()).ReflexId);
            Assert.Equal("test.exploding", reported);
            Assert.True(WingAi.IsFaulted("test.exploding"));

            WingAi.FaultReporter = null;
        }

        [Fact]
        public void AFaultIsReportedOnceNotEveryTick()
        {
            int reports = 0;
            WingAi.FaultReporter = (id, e) => reports++;

            WingAi.Register(new Exploding());
            WingAi.Register(new Fake("z.task", WingReflexBand.Task, 1f));

            for (int i = 0; i < 5; i++) Resolve(new WingSituation());

            Assert.Equal(1, reports);
            WingAi.FaultReporter = null;
        }

        [Fact]
        public void ResetFaultsGivesADisabledReflexAnotherChance()
        {
            WingAi.Register(new Exploding());
            WingAi.Register(new Fake("z.task", WingReflexBand.Task, 1f));
            Resolve(new WingSituation());
            Assert.True(WingAi.IsFaulted("test.exploding"));

            WingAi.ResetFaults();
            Assert.False(WingAi.IsFaulted("test.exploding"));
        }

        [Fact]
        public void ResolutionIsTotalEvenWithNothingRegistered()
        {
            WingResolution r = Resolve(new WingSituation());
            Assert.Equal(WingBehaviours.Task, r.BehaviourId);
            Assert.False(string.IsNullOrEmpty(r.BehaviourId));
        }

        // --------------------------------------------------------------- performance mode

        [Fact]
        public void PerformanceModeDropsSmartOnlyReflexesButNotTheRest()
        {
            WingAi.Register(new Fake("a.smart", WingReflexBand.Safety, 1f, smartOnly: true));
            WingAi.Register(new Fake("b.task", WingReflexBand.Task, 1f));

            Assert.Equal("a.smart", Resolve(new WingSituation(), smart: true).ReflexId);
            Assert.Equal("b.task", Resolve(new WingSituation(), smart: false).ReflexId);
        }

        // -------------------------------------------------------------------------- trace

        [Fact]
        public void TheTraceExplainsTheWholeLadderNotJustTheWinner()
        {
            WingAi.Register(new Fake("a.safety", WingReflexBand.Safety, 0.4f));
            WingAi.Register(new Fake("b.cohesion", WingReflexBand.Cohesion, 0.9f));
            WingAi.Register(new Fake("c.task", WingReflexBand.Task, 1f));

            List<WingReflexTrace> trace = new List<WingReflexTrace>();
            WingSituation s = new WingSituation();
            WingArbiter.Resolve(in s, null, true, WingAi.Reflexes, trace);

            Assert.Equal(3, trace.Count);
            Assert.Single(trace, t => t.Won);
            Assert.Equal("a.safety", trace.Find(t => t.Won).Id);
        }
    }
}
