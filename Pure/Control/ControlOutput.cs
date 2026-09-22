// Filled by the flight controller (Task 6) and the ground writer (M3); read by the engine writer.
#pragma warning disable CS0649

namespace WingCommand
{
    /// <summary>Normalised demands in the Pure sign convention (pitch + nose-up, roll + right, yaw +
    /// nose-right). The engine writer maps them onto the game's ControlInputs.</summary>
    internal struct ControlOutput
    {
        public float Pitch, Roll, Yaw, Throttle, Brake;
        /// <summary>Airbrake demanded; throttle is exactly 0 while set (the game opens brakes only there).</summary>
        public bool Airbrake;
    }
}
