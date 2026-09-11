using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingFlightProfileTests : IDisposable
    {
        public WingFlightProfileTests() { WingAi.Clear(); WingAi.FaultReporter = null; }
        public void Dispose() { WingAi.Clear(); WingAi.FaultReporter = null; }

        [Fact]
        public void IndependentInputsContributeTogetherWithoutAWinningOverride()
        {
            WingAi.RegisterInfluence(new Influence("b.energy", new WingFlightContribution(0.8f,
                captureGain: 0.8f, dampingScale: 1.4f, bankScale: 0.7f)));
            WingAi.RegisterInfluence(new Influence("a.capture", new WingFlightContribution(1f, captureGain: 1.3f)));
            var snapshot = Situation();
            WingFlightProfile result = WingAi.BlendFlight(in snapshot, true);
            Assert.InRange(result.CaptureGain, 1.139f, 1.141f);
            Assert.InRange(result.DampingScale, 1.319f, 1.321f);
            Assert.InRange(result.BankScale, 0.759f, 0.761f);
            Assert.Equal(WingOrder.Formation, snapshot.Situation.Order);
        }

        [Fact]
        public void RegistrationsAreDeterministicAndReplacementDoesNotDoubleCount()
        {
            var a = new Influence("a", new WingFlightContribution(0.7f, captureGain: 1.2f));
            var b = new Influence("b", new WingFlightContribution(0.3f, captureGain: 0.8f));
            var snapshot = Situation();
            WingAi.RegisterInfluence(b); WingAi.RegisterInfluence(a);
            float first = WingAi.BlendFlight(in snapshot, true).CaptureGain;
            WingAi.Clear();
            WingAi.RegisterInfluence(a); WingAi.RegisterInfluence(b); WingAi.RegisterInfluence(a);
            Assert.Equal(first, WingAi.BlendFlight(in snapshot, true).CaptureGain);
            Assert.True(WingAi.UnregisterInfluence("b"));
            Assert.True(WingAi.BlendFlight(in snapshot, true).CaptureGain > first);
        }

        [Fact]
        public void BadInfluenceCannotPoisonHealthyInputsAndReportsOnlyOnce()
        {
            int errors = 0;
            WingAi.FaultReporter = (_, _) => errors++;
            WingAi.RegisterInfluence(new Influence("bad", new WingFlightContribution(float.NaN)));
            WingAi.RegisterInfluence(new Influence("good", new WingFlightContribution(1f, captureGain: 1.2f)));
            var snapshot = Situation();
            for (int i = 0; i < 3; i++) Assert.Equal(1.2f, WingAi.BlendFlight(in snapshot, true).CaptureGain);
            Assert.Equal(1, errors);
            WingAi.RegisterInfluence(new Influence("bad", new WingFlightContribution(1f, dampingScale: 1.2f)));
            Assert.Equal(1.2f, WingAi.BlendFlight(in snapshot, true).DampingScale);
        }

        [Fact]
        public void StrongAgilityRequestsCannotCancelSafetyReductionsOrExceedBounds()
        {
            WingAi.RegisterInfluence(new Influence("safe", new WingFlightContribution(1f, bankScale: 0.65f)));
            for (int i = 0; i < 20; i++)
                WingAi.RegisterInfluence(new Influence("gain." + i,
                    new WingFlightContribution(100f, captureGain: 100f, bankScale: 100f)));
            var snapshot = Situation();
            var result = WingAi.BlendFlight(in snapshot, true);
            Assert.Equal(1.35f, result.CaptureGain);
            Assert.Equal(0.65f, result.BankScale);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(6f)]
        [InlineData(8f)]
        [InlineData(25f)]
        [InlineData(70f)]
        public void InfluenceNeverRaisesAnExistingTerrainOrLaunchBankCap(float limit)
        {
            float result = WingFlightProfile.LimitBank(limit, 8f, 0.65f);
            Assert.InRange(result, 0f, limit);
        }

        [Fact]
        public void BuiltInsReactJointlyToDistanceClosureConditionAndExperience()
        {
            WingInfluences.RegisterDefaults();
            var far = Situation(error: 4000f, closing: 0f);
            var closing = Situation(error: 4000f, closing: 50f);
            var damaged = Situation(error: 4000f, closing: 0f, integrity: 0.3f);
            var veteran = Situation(error: 4000f, closing: 0f, skill: 1f);
            WingFlightProfile a = WingAi.BlendFlight(in far, true);
            WingFlightProfile b = WingAi.BlendFlight(in closing, true);
            WingFlightProfile c = WingAi.BlendFlight(in damaged, true);
            WingFlightProfile d = WingAi.BlendFlight(in veteran, true);
            Assert.True(a.CaptureGain > b.CaptureGain);
            Assert.True(c.CaptureGain < a.CaptureGain);
            Assert.True(c.BankScale < a.BankScale);
            Assert.True(c.DampingScale > a.DampingScale);
            Assert.True(d.DampingScale < a.DampingScale);
            Assert.True(d.SpacingScale < a.SpacingScale);
        }

        [Fact]
        public void SharedSpacingKeepsTrailSlotsOrderedDespiteDifferentPersonalProfiles()
        {
            float scale = 0.85f;
            foreach (float personal in new[] { 1f, 1.6f, 0.85f })
                scale = WingFlightProfile.CombineSpacing(scale, personal);
            Assert.Equal(1.6f, scale);
            float previous = 0f;
            for (int slot = 1; slot <= 8; slot++)
            {
                float aft = FormationLayout.Slot(FormationShape.Trail, slot).Back * scale * 120f;
                Assert.True(aft > previous);
                previous = aft;
            }
        }

        [Fact]
        public void PerformanceDropsOptionalInfluencesButKeepsConditionAndCapture()
        {
            WingInfluences.RegisterDefaults();
            var snapshot = Situation(error: 4000f, integrity: 0.3f);
            var profile = WingAi.BlendFlight(in snapshot, false);
            Assert.True(profile.CaptureGain > 1f);
            Assert.True(profile.BankScale < 1f);
        }

        [Fact]
        public void VagrantEnergyInfluenceUsesTheSameStallEnvelopeAsItsController()
        {
            WingInfluences.RegisterDefaults();
            float minimum = FormationGuidance.MinimumAirspeed(180f, 100f);
            var slow = new WingSituation(airspeed: 55f, takeoffSpeed: 35f);
            var cruise = new WingSituation(airspeed: 80f, takeoffSpeed: 35f);
            var lowEnergy = new WingFlightSituation(in slow, 0f, 300f, 0f, 68f, 0.5f)
                .WithMinimumAirspeed(minimum);
            var healthy = new WingFlightSituation(in cruise, 0f, 300f, 0f, 68f, 0.5f)
                .WithMinimumAirspeed(minimum);

            WingFlightProfile cautious = WingAi.BlendFlight(in lowEnergy, false);
            WingFlightProfile settled = WingAi.BlendFlight(in healthy, false);
            Assert.True(cautious.BankScale < settled.BankScale);
            Assert.True(cautious.DampingScale > settled.DampingScale);
            Assert.True(cautious.SpacingScale > settled.SpacingScale);
            Assert.Equal(WingOrder.Formation, lowEnergy.Situation.Order);
            Assert.Equal(35f, lowEnergy.Situation.TakeoffSpeed);
        }

        private static WingFlightSituation Situation(float error = 0f, float closing = 0f,
            float integrity = 1f, float skill = 0f)
        {
            var situation = new WingSituation(integrity: integrity);
            return new WingFlightSituation(in situation, error, 300f, closing, 100f, skill);
        }

        private sealed class Influence : IWingInfluence
        {
            public string Id { get; }
            public bool RequiresSmartMode => false;
            private readonly WingFlightContribution contribution;
            public Influence(string id, WingFlightContribution contribution) { Id = id; this.contribution = contribution; }
            public WingFlightContribution Evaluate(in WingFlightSituation situation) => contribution;
        }
    }
}
