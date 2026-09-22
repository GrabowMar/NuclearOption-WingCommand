namespace WingCommand.FlightSim
{
    /// <summary>Plant truth → AircraftState, as the engine sensor will fill it. Flat sea-level terrain;
    /// scenarios with terrain pass their own floor through LimitContext.</summary>
    internal static class SimSensor
    {
        public static AircraftState Read(FixedWingPlant plant, float dt)
        {
            Vec3 v = plant.Velocity;
            Vec3 fwd = v.Normalized;
            return new AircraftState
            {
                Pos = plant.Position,
                Vel = v,
                Acc = plant.Acceleration,
                Fwd = fwd,
                Up = Vec3.Up,
                Right = Vec3.Cross(Vec3.Up, fwd).Normalized,
                BankDeg = plant.BankDeg,
                PitchDeg = plant.GammaDeg,
                GammaDeg = plant.GammaDeg,
                P = plant.RollRateDps,
                Tas = plant.Speed,
                Qbar = Isa.DynamicPressure(plant.Position.Y, plant.Speed),
                Nz = plant.LoadFactor,
                RadarAlt = plant.Position.Y,
                Throttle = plant.ThrottleActual,
                FbwActive = plant.Speed >= 25f,
                AirbrakeOpen = plant.AirbrakeOpen,
                Dt = dt,
            };
        }
    }
}
