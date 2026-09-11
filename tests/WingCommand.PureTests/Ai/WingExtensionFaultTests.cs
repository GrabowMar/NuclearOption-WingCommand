using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingExtensionFaultTests : IDisposable
    {
        public WingExtensionFaultTests() { WingAi.Clear(); WingAi.FaultReporter = null; }
        public void Dispose() { WingAi.Clear(); WingAi.FaultReporter = null; }

        [Theory]
        [InlineData("Id")]
        [InlineData("Band")]
        [InlineData("BehaviourId")]
        [InlineData("MinimumSeconds")]
        [InlineData("RequiresSmartMode")]
        [InlineData("Score")]
        [InlineData("CanHold")]
        [InlineData("InterruptsMinimumHold")]
        public void EveryExtensionCallbackIsIsolatedEvenWhenTheReporterAlsoThrows(string fault)
        {
            int reports = 0;
            WingAi.FaultReporter = (_, _) => { reports++; throw new Exception("reporter"); };
            var broken = new Reflex("broken", WingReflexBand.Survival) { FailAt = fault, Minimum = 10f };
            var healthy = new Reflex("healthy");
            var situation = new WingSituation(secondsInBehaviour: 0.1f);
            var candidates = new IWingReflex[] { broken, healthy };
            for (int i = 0; i < 3; i++)
                Assert.Equal("healthy", WingArbiter.Resolve(in situation, "broken", false, candidates).ReflexId);
            Assert.Equal(1, reports);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-1f)]
        public void InvalidHoldDurationCannotTrapTheIncumbent(float duration)
        {
            var broken = new Reflex("broken", WingReflexBand.Survival) { Minimum = duration };
            var healthy = new Reflex("healthy");
            var situation = new WingSituation();
            Assert.Equal("healthy", WingArbiter.Resolve(in situation, "broken", true,
                new IWingReflex[] { broken, healthy }).ReflexId);
        }

        [Fact]
        public void AnInvalidBandCannotOutrankSurvival()
        {
            var broken = new Reflex("broken", (WingReflexBand)(-1));
            var survival = new Reflex("survival", WingReflexBand.Survival);
            var situation = new WingSituation();
            Assert.Equal("survival", WingArbiter.Resolve(in situation, null, true,
                new IWingReflex[] { broken, survival }).ReflexId);
        }

        [Fact]
        public void FailedReplacementLeavesThePreviousHealthyRegistrationAvailable()
        {
            var healthy = new Reflex("plugin.reflex");
            WingAi.Register(healthy);
            WingAi.Register(new Reflex("plugin.reflex") { FailAt = "BehaviourId" });
            Assert.Same(healthy, Assert.Single(WingAi.Reflexes));
            var situation = new WingSituation();
            Assert.Equal("plugin.reflex", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
        }

        [Fact]
        public void ExistingMetadataCannotBreakAnotherRegistrationOrUnregister()
        {
            var broken = new Reflex("z.first", WingReflexBand.Safety);
            WingAi.Register(broken);
            broken.FailAt = "Id";
            WingAi.Register(new Reflex("a.second", WingReflexBand.Safety));
            Assert.Equal(2, WingAi.Reflexes.Count);
            var situation = new WingSituation();
            Assert.Equal("a.second", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            Assert.True(WingAi.Unregister("z.first"));
        }

        [Fact]
        public void MetadataIsCoherentWithinADecisionAndDynamicBehaviourChangesNextDecision()
        {
            var reflex = new Reflex("dynamic") { Behaviour = "phase.one" };
            WingAi.Register(reflex);
            reflex.Reads.Clear();
            reflex.OnScore = () => reflex.Behaviour = "phase.two";
            var situation = new WingSituation();
            var first = WingArbiter.Resolve(in situation, "dynamic", true, WingAi.Reflexes);
            Assert.Equal("phase.one", first.BehaviourId);
            foreach (string property in new[] { "Band", "BehaviourId", "MinimumSeconds", "RequiresSmartMode" })
                Assert.Equal(1, reflex.Reads[property]);
            Assert.False(reflex.Reads.ContainsKey("Id"));
            var second = WingArbiter.Resolve(in situation, "dynamic", true, WingAi.Reflexes);
            Assert.Equal("phase.two", second.BehaviourId);
            Assert.False(second.SameAs(in first));
        }

        [Fact]
        public void ARegistrationMadeByAScoreCallbackTakesEffectOnTheNextDecision()
        {
            var original = new Reflex("original");
            original.OnScore = () => WingAi.Register(new Reflex("new.safety", WingReflexBand.Safety));
            WingAi.Register(original);
            var situation = new WingSituation();
            Assert.Equal("original", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            Assert.Equal("new.safety", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
        }

        [Fact]
        public void NewMissionAndExplicitReplacementCanRetryAQuarantinedReflex()
        {
            var reflex = new Reflex("retry");
            WingAi.Register(reflex);
            reflex.FailAt = "Band";
            var situation = new WingSituation();
            Assert.Equal(string.Empty, WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            reflex.FailAt = null;
            WingAi.ResetFaults();
            Assert.Equal("retry", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            reflex.FailAt = "Score";
            Assert.Equal(string.Empty, WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            WingAi.Register(new Reflex("retry"));
            Assert.Equal("retry", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
        }

        [Fact]
        public void InfluenceMetadataAndReporterFaultsPreserveHealthyContributions()
        {
            int reports = 0;
            WingAi.FaultReporter = (_, _) => { reports++; throw new Exception("reporter"); };
            var broken = new Influence("bad");
            WingAi.RegisterInfluence(broken);
            WingAi.RegisterInfluence(new Influence("good"));
            broken.ThrowMetadata = true;
            var situation = default(WingFlightSituation);
            for (int i = 0; i < 3; i++)
                Assert.Equal(1.2f, WingAi.BlendFlight(in situation, false).CaptureGain);
            Assert.Equal(1, reports);
            Assert.True(WingAi.UnregisterInfluence("bad"));
        }

        [Fact]
        public void FailedInfluenceReplacementDoesNotDisableTheHealthyOriginal()
        {
            WingAi.RegisterInfluence(new Influence("same"));
            WingAi.RegisterInfluence(new Influence("same") { ThrowMetadata = true });
            var situation = default(WingFlightSituation);
            Assert.Equal(1.2f, WingAi.BlendFlight(in situation, false).CaptureGain);
        }

        [Fact]
        public void RestoringAFactoryRevivesOnlyReflexesWaitingForThatBehaviour()
        {
            WingAi.Register(new Reflex("recoverable") { Behaviour = "plugin.restored" });
            WingAi.Register(new Reflex("still.missing") { Behaviour = "plugin.missing" });
            WingAi.RejectBehaviour("recoverable", "plugin.restored");
            WingAi.RejectBehaviour("still.missing", "plugin.missing");
            var situation = new WingSituation();
            Assert.Equal(string.Empty, WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            WingAi.RestoreBehaviour("plugin.restored");
            Assert.Equal("recoverable", WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            Assert.True(WingAi.IsFaulted("still.missing"));
        }

        [Theory]
        [InlineData("Score")]
        [InlineData("Band")]
        public void RegisteringAFactoryCannotClearARealProviderFault(string fault)
        {
            var broken = new Reflex("broken") { Behaviour = "plugin.behaviour" };
            WingAi.Register(broken);
            broken.FailAt = fault;
            var situation = new WingSituation();
            Assert.Equal(string.Empty, WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            WingAi.RejectBehaviour("broken", "plugin.behaviour");
            WingAi.RestoreBehaviour("plugin.behaviour");
            broken.FailAt = null;
            Assert.Equal(string.Empty, WingArbiter.Resolve(in situation, null, true, WingAi.Reflexes).ReflexId);
            Assert.True(WingAi.IsFaulted("broken"));
        }

        private sealed class Reflex : IWingReflex, IWingReflexLifecycle
        {
            private readonly string id;
            private readonly WingReflexBand band;
            public string FailAt;
            public float Minimum;
            public string Behaviour = WingBehaviours.Task;
            public Action OnScore;
            public readonly Dictionary<string, int> Reads = new Dictionary<string, int>();
            public Reflex(string id, WingReflexBand band = WingReflexBand.Task) { this.id = id; this.band = band; }
            public string Id => Read("Id", id);
            public WingReflexBand Band => Read("Band", band);
            public string BehaviourId => Read("BehaviourId", Behaviour);
            public float MinimumSeconds => Read("MinimumSeconds", Minimum);
            public bool RequiresSmartMode => Read("RequiresSmartMode", false);
            public bool InterruptsMinimumHold => Read("InterruptsMinimumHold", false);
            public bool CanHold(in WingSituation situation) => Read("CanHold", true);
            public float Score(in WingSituation situation, bool incumbent)
            { OnScore?.Invoke(); return Read("Score", 1f); }
            private T Read<T>(string property, T value)
            {
                Reads[property] = Reads.TryGetValue(property, out int count) ? count + 1 : 1;
                if (FailAt == property) throw new InvalidOperationException(property);
                return value;
            }
            public override int GetHashCode() => throw new InvalidOperationException("plugin hash");
            public override bool Equals(object obj) => throw new InvalidOperationException("plugin equality");
        }

        private sealed class Influence : IWingInfluence
        {
            public string Id { get; }
            public bool ThrowMetadata;
            public bool RequiresSmartMode => ThrowMetadata ? throw new InvalidOperationException("metadata") : false;
            public Influence(string id) { Id = id; }
            public WingFlightContribution Evaluate(in WingFlightSituation situation) => new WingFlightContribution(1f, captureGain: 1.2f);
        }
    }
}
