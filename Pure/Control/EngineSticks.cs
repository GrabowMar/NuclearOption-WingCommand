namespace WingCommand
{
// Filled by the engine writer and reader, and by tests.
#pragma warning disable CS0649
    /// <summary>Stick values in the game's ControlInputs convention (pitch + is nose down).</summary>
    internal struct StickInputs
    {
        public float Pitch, Roll, Yaw, Throttle, Brake;
    }
#pragma warning restore CS0649

    /// <summary>Maps Pure demands to the game's ControlInputs and back.
    /// <list type="bullet">
    /// <item>The game's pitch is + nose down. The fly-by-wire drives the local x rate (Unity +x = nose down)
    /// toward pitch·gain, and the native autopilot writes negative pitch for a target above the nose.</item>
    /// <item>Roll + is a right roll and yaw + is nose right in both conventions.</item>
    /// <item>The airbrake opens only at zero throttle.</item>
    /// </list></summary>
    internal static class EngineSticks
    {
        public const float PitchSign = -1f, RollSign = 1f, YawSign = 1f;

        public static StickInputs FromPure(in ControlOutput o) => new StickInputs
        {
            Pitch = Scalar.Clamp(PitchSign * o.Pitch, -1f, 1f),
            Roll = Scalar.Clamp(RollSign * o.Roll, -1f, 1f),
            Yaw = Scalar.Clamp(YawSign * o.Yaw, -1f, 1f),
            Throttle = o.Airbrake ? 0f : Scalar.Clamp01(o.Throttle),
            Brake = Scalar.Clamp01(o.Brake),
        };

        public static ControlOutput ToPure(in StickInputs s) => new ControlOutput
        {
            Pitch = PitchSign * s.Pitch,
            Roll = RollSign * s.Roll,
            Yaw = YawSign * s.Yaw,
            Throttle = s.Throttle,
            Airbrake = s.Throttle <= 0f,
        };
    }
}
