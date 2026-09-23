namespace WingCommand
{
    /// <summary>The only writer of a wingman's ControlInputs: every axis in the game's convention, then the
    /// fly-by-wire filter exactly once per tick (calling it twice double-integrates the FBW trims).</summary>
    internal static class ControlWriter
    {
        public static void Fly(Aircraft a, in ControlOutput o)
        {
            ControlInputs inputs = a.GetInputs();
            StickInputs s = EngineSticks.FromPure(o);
            inputs.pitch = s.Pitch;
            inputs.roll = s.Roll;
            inputs.yaw = s.Yaw;
            inputs.throttle = s.Throttle;
            inputs.brake = s.Brake;
            a.FilterInputs();
        }

        public static StickInputs Read(ControlInputs inputs) => new StickInputs
        {
            Pitch = inputs.pitch, Roll = inputs.roll, Yaw = inputs.yaw, Throttle = inputs.throttle, Brake = inputs.brake,
        };
    }
}
