namespace WingCommand.FlightSim
{
    /// <summary>Runs the production pipeline against a plant: sensor → guidance → FixedWingPipeline →
    /// plant input. The plant is the only fake.</summary>
    internal sealed class SimPilot
    {
        private readonly FixedWingPlant plant;
        private readonly AirframeProfile profile;

        public SimPilot(FixedWingPlant plant, AirframeProfile profile)
        {
            this.plant = plant;
            this.profile = profile;
            Pipeline.Track(SimSensor.Read(plant, 1f / 60f), new ControlOutput { Throttle = plant.ThrottleActual }, profile);
        }

        public FixedWingPipeline Pipeline { get; } = new FixedWingPipeline();
        public ControlOutput Last { get; private set; }

        public void StepTracking(in FlightIntent intent, float dt, float floorY = float.NaN)
        {
            AircraftState s = SimSensor.Read(plant, dt);
            GuidanceCommand g = TrackingGuidance.Evaluate(intent, s, profile);
            var ctx = new LimitContext { FloorY = floorY, Clearance = intent.TerrainClearance, Aggression = intent.Aggression };
            Apply(Pipeline.Step(g, s, ctx, profile, dt), dt);
        }

        public void StepHold(in HoldSpec hold, float dt)
        {
            AircraftState s = SimSensor.Read(plant, dt);
            GuidanceCommand g = HoldGuidance.Evaluate(hold, s, profile);
            var ctx = new LimitContext { FloorY = float.NaN, Clearance = 60f, Aggression = 0f };
            Apply(Pipeline.Step(g, s, ctx, profile, dt), dt);
        }

        private void Apply(ControlOutput o, float dt)
        {
            Last = o;
            plant.Step(new PlantInput(o.Pitch, o.Roll, o.Throttle), dt);
        }
    }

    internal static class SimProfiles
    {
        /// <summary>Profile the engine derives for RotaryParams.Utility (a UH-90-like helicopter).</summary>
        public static AirframeProfile Utility() => AirframeProfile.Derive(new ProfileInputs
        {
            UnitName = "sim-utility-helo", Class = AirframeClass.Rotary, MaxSpeed = 134f, GLimit = 3f, MaxRadius = 9f,
        });

        /// <summary>Profile the pipeline would derive for PlantParams.GenericFighter.</summary>
        public static AirframeProfile GenericFighter()
        {
            PlantParams pp = PlantParams.GenericFighter;
            float stall = (float)System.Math.Sqrt(pp.MassKg * Scalar.G / (0.5 * 1.225 * pp.WingAreaM2 * pp.ClMax));
            return new AirframeProfile
            {
                Id = "sim-generic",
                StallSpeed = stall,
                CornerSpeed = pp.CornerSpeed,
                MaxSpeed = 300f,
                MilSpeed = 255f,
                RefAirspeed = 200f,
                GLimit = pp.GLimit,
                RollRateMaxDps = pp.RollRateMaxDps,
                CruiseThrottle = 0.45f,
            };
        }

        /// <summary>Profile the engine derives for PlantParams.CoinTurboprop, through the production
        /// <see cref="AirframeProfile.Derive"/> (published stall, FBW corner, g and roll fields).</summary>
        public static AirframeProfile CoinTurboprop()
        {
            PlantParams pp = PlantParams.CoinTurboprop;
            float stall = (float)System.Math.Sqrt(pp.MassKg * Scalar.G / (0.5 * 1.225 * pp.WingAreaM2 * pp.ClMax));
            return AirframeProfile.Derive(new ProfileInputs
            {
                UnitName = "sim-turboprop",
                PublishedStallKmh = stall * 3.6f,
                MaxSpeed = 160f,
                CornerSpeed = pp.CornerSpeed,
                PidReferenceAirspeed = 110f,
                GLimit = pp.GLimit,
                CruiseThrottle = 0.55f,
                FbwMaxRollAngularVel = pp.MaxRollAngularVel,
                FbwGLimit = pp.GLimit,
                FbwCornerSpeed = pp.CornerSpeed,
                MaxRadius = 6f,
            });
        }
    }
}
