namespace WingCommand.FlightSim
{
    /// <summary>What a sim wing needs from any plant: where it is, what its sensor reads, and a step with the pipeline's
    /// output.</summary>
    internal interface ISimPlant
    {
        Vec3 Position { get; }
        Vec3 Velocity { get; }
        AircraftState Read(float dt);
        void Step(in ControlOutput output, float dt);
    }
}
