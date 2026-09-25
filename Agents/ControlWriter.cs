namespace WingCommand
{
    /// <summary>The only writer of a wingman's ControlInputs: every axis in the game's convention, then the
    /// fly-by-wire filter exactly once per tick (calling it twice double-integrates the FBW trims). A helicopter also
    /// gets its pusher (customAxis1 = Aux); a tiltwing's customAxis1 belongs to the game's auto-tilt and is left alone.</summary>
    internal static class ControlWriter
    {
        public static void Fly(Aircraft a, in ControlOutput o, AirframeClass cls = AirframeClass.FixedWing)
        {
            ControlInputs inputs = a.GetInputs();
            StickInputs s = EngineSticks.FromPure(o);
            inputs.pitch = s.Pitch;
            inputs.roll = s.Roll;
            inputs.yaw = s.Yaw;
            inputs.throttle = s.Throttle;
            inputs.brake = s.Brake;
            if (cls == AirframeClass.Rotary) inputs.customAxis1 = UnityEngine.Mathf.Clamp01(o.Aux);
            a.FilterInputs();
        }

        public static StickInputs Read(ControlInputs inputs) => new StickInputs
        {
            Pitch = inputs.pitch, Roll = inputs.roll, Yaw = inputs.yaw, Throttle = inputs.throttle, Brake = inputs.brake,
        };
    }
}
