namespace WingCommand
{
    /// <summary>One tick of the step test's open-loop stick (Pure signs).</summary>
    internal struct StepCommand
    {
        public bool Done;
        public float Pitch, Roll, Throttle;
    }

    /// <summary>The calibration step test (the S2 spike, productised), 30 s:
    /// <list type="table">
    /// <item><term>2.0–2.5 s</term><description>roll +0.5</description></item>
    /// <item><term>5.0–5.5 s</term><description>roll −0.5</description></item>
    /// <item><term>8–9 s</term><description>pitch +0.3</description></item>
    /// <item><term>11–12 s</term><description>pitch −0.3</description></item>
    /// <item><term>14–20 s</term><description>military power (0.89)</description></item>
    /// <item><term>20–26 s</term><description>idle, airbrake out</description></item>
    /// </list>
    /// At all other times the throttle is 0.7 and the stick centred. The fly-by-wire holds attitude with the
    /// stick centred, so the aircraft returns close to level after each pair.</summary>
    internal static class StepSequence
    {
        public static float Duration = 30f;

        public static StepCommand At(float t)
        {
            var c = new StepCommand { Throttle = 0.7f };
            if (t >= Duration)
            {
                c.Done = true;
                return c;
            }
            if (t >= 2f && t < 2.5f) c.Roll = 0.5f;
            else if (t >= 5f && t < 5.5f) c.Roll = -0.5f;
            else if (t >= 8f && t < 9f) c.Pitch = 0.3f;
            else if (t >= 11f && t < 12f) c.Pitch = -0.3f;
            if (t >= 14f && t < 20f) c.Throttle = 0.89f;
            else if (t >= 20f && t < 26f) c.Throttle = 0f;
            return c;
        }
    }
}
