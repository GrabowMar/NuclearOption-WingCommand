using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class AirframeProfileTests
    {
        [Theory]
        [InlineData(10f, 120f)]
        [InlineData(3f, 85.944f)]
        public void DeriveSeedsRollAuthorityAtTheSmallerOfTheFbwCommandAndTheCap(float maxRollAngularVel, float expected)
        {
            // The FBW commands 0.5·maxRollAngularVel, but its weak rate loop on aero-limited ailerons rolls far
            // slower in game (CI-22: 286 commanded, ≈ 95°/s real); the controller learns the rest in flight.
            AirframeProfile p = AirframeProfile.Derive(new ProfileInputs { FbwMaxRollAngularVel = maxRollAngularVel });
            Assert.Equal(expected, p.RollRateMaxDps, 2);
        }

        [Fact]
        public void DeriveMapsNativeNumbersAndAppliesTheFbwRollQuirk()
        {
            var inputs = new ProfileInputs
            {
                UnitName = "FS-20 Vortex", Class = AirframeClass.FixedWing, GLimit = 10f, PidReferenceAirspeed = 220f,
                MaxSpeed = 340f, CornerSpeed = 180f, LandingSpeed = 78f, CruiseThrottle = 0.55f, MaxRadius = 9f,
                FbwMaxRollAngularVel = 6f, FbwGLimit = 9f, FbwCornerSpeed = 175f,
            };
            AirframeProfile p = AirframeProfile.Derive(inputs);
            Assert.Equal("FS-20 Vortex", p.Id);
            Assert.Equal(60f, p.StallSpeed, 3);
            Assert.Equal(175f, p.CornerSpeed);
            Assert.Equal(340f, p.MaxSpeed);
            Assert.Equal(289f, p.MilSpeed, 3);
            Assert.Equal(220f, p.RefAirspeed);
            Assert.Equal(9f, p.GLimit);
            Assert.Equal(AirframeProfile.RollSeedCapDps, p.RollRateMaxDps, 2);
            Assert.Equal(0.55f, p.CruiseThrottle);
            Assert.Equal(9f, p.MaxRadius);
        }

        [Fact]
        public void PublishedStallSpeedWinsOverLandingSpeed()
        {
            var p = AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 216f, LandingSpeed = 100f });
            Assert.Equal(60f, p.StallSpeed, 3);
        }

        [Fact]
        public void TheWingsStallSpeedWinsOverALowPublishedOne()
        {
            // Overnight 2026-09-28: the EW-25 publishes 119 km/h (33 m/s) but could not hold 1 g at 62 m/s and 71° bank; its
            // envelope let it bank 86° at 67 m/s and it stalled into the ground after take-off.
            var p = AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 119f, LiftStallSpeed = 55f });
            Assert.Equal(55f, p.StallSpeed, 3);
        }

        [Fact]
        public void AHigherPublishedStallSpeedStands() =>
            Assert.Equal(75f, AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 270f, LiftStallSpeed = 60f }).StallSpeed, 3);

        [Fact]
        public void TheWingsEstimateIsCappedAtTwiceThePublishedSpeed() =>
            Assert.Equal(60f, AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 108f, LiftStallSpeed = 90f }).StallSpeed, 3);

        [Fact]
        public void AHelicopterIgnoresItsWings() =>
            Assert.Equal(23f, AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, PublishedStallKmh = 23f * 3.6f, LiftStallSpeed = 40f }).StallSpeed, 3);

        [Fact]
        public void TheStallSpeedFollowsFromWeightAndLift()
        {
            // 1-g lift at sea level: m·g = ½·ρ·V²·ΣCL·S.
            Assert.Equal(50f, LiftStall.Speed(10000f, 2f * 10000f * 9.81f / (1.225f * 2500f)), 2);
            Assert.Equal(0f, LiftStall.Speed(10000f, 0f));
        }

        [Fact]
        public void MissingNativeNumbersKeepGenericDefaults()
        {
            AirframeProfile p = AirframeProfile.Derive(new ProfileInputs());
            Assert.Equal("generic", p.Id);
            Assert.Equal(new AirframeProfile().StallSpeed, p.StallSpeed);
            Assert.Equal(new AirframeProfile().RollRateMaxDps, p.RollRateMaxDps);
        }

        [Fact]
        public void OverridesApplyKnownFieldsAndReportUnknownOnes()
        {
            var p = new AirframeProfile();
            List<string> rejected = p.ApplyOverrides(new Dictionary<string, object>
            {
                ["tauAlong"] = 5.0, ["hasAfterburner"] = false, ["class"] = "Rotary", ["bogus"] = 1L, ["gLimit"] = "nine",
            });
            Assert.Equal(5f, p.TauAlong);
            Assert.False(p.HasAfterburner);
            Assert.Equal(AirframeClass.Rotary, p.Class);
            Assert.Equal(new[] { "bogus", "gLimit" }, rejected);
        }

        [Fact]
        public void LoadedMinimumAndLiftLimitScaleWithLoadFactorAndSpeed()
        {
            var p = new AirframeProfile { StallSpeed = 50f };
            Assert.Equal(60f, p.MinimumSpeed(1f), 3);
            Assert.Equal(120f, p.MinimumSpeed(4f), 3);
            Assert.Equal(4f, p.LiftLimitedG(100f), 3);
        }

        [Fact]
        public void EnergyRateCombinesSpeedChangeAndClimb()
        {
            var s = new AircraftState { Vel = new Vec3(0f, 10f, 200f), Acc = new Vec3(0f, 0f, 4.905f) };
            // V·V̇/g with V̇ = Acc·v̂ = 4.905·(200/200.25) plus ḣ = 10.
            float expected = s.Speed * (4.905f * 200f / s.Speed) / Scalar.G + 10f;
            Assert.Equal(expected, s.EnergyRate, 2);
        }
    }
}
