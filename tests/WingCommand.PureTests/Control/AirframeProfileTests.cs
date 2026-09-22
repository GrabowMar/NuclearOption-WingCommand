using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class AirframeProfileTests
    {
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
            Assert.Equal(171.887f, p.RollRateMaxDps, 2);
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
