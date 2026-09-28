namespace WingCommand.FlightSim
{
    /// <summary>Tiltwing for the FlightSim (sim only, spec M2 §8.1): a fixed-wing plant above the conversion band and the
    /// helicopter plant below it, like the game's auto-tilt moving the nacelles with speed. Crossing the band hands the
    /// position, velocity, heading and bank to the other plant (it converts at once; the pipeline's own conversion keeps
    /// a dwell, so both can briefly disagree, as they may in game).</summary>
    internal sealed class TiltwingPlant : ISimPlant
    {
        private readonly PlantParams plane;
        private readonly RotaryParams rotor;
        private readonly float low, high;
        private FixedWingPlant wing;
        private RotaryPlant rotary;

        public TiltwingPlant(PlantParams plane, RotaryParams rotor, float conversionLow, float conversionHigh,
            Vec3 position, Vec3 velocity, float headingDeg)
        {
            this.plane = plane;
            this.rotor = rotor;
            low = conversionLow;
            high = conversionHigh;
            if (velocity.Length >= 0.5f * (low + high)) wing = new FixedWingPlant(plane, position, velocity.Length, headingDeg);
            else rotary = new RotaryPlant(rotor, position, velocity, headingDeg);
        }

        public bool PlaneMode => wing != null;
        public int Conversions { get; private set; }
        private ISimPlant Active => wing != null ? wing : (ISimPlant)rotary;

        public Vec3 Position => Active.Position;
        public Vec3 Velocity => Active.Velocity;

        public AircraftState Read(float dt) => Active.Read(dt);

        public void Step(in ControlOutput output, float dt)
        {
            Active.Step(output, dt);
            float speed = Active.Velocity.Length;
            if (wing != null && speed < low)
            {
                rotary = new RotaryPlant(rotor, wing.Position, wing.Velocity, wing.HeadingDeg);
                rotary.SetAttitude(0f, wing.BankDeg);
                wing = null;
                Conversions++;
            }
            else if (rotary != null && speed > high)
            {
                wing = new FixedWingPlant(plane, rotary.Position, speed, rotary.HeadingDeg);
                wing.SetBankState(rotary.RollDeg);
                wing.SetThrottleState(wing.TrimThrottle());
                rotary = null;
                Conversions++;
            }
        }
    }
}
