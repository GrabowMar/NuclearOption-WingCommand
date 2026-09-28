namespace WingCommand
{
    /// <summary>One member's radar switching (a field on the member: updated in place).</summary>
    internal struct RadarGate
    {
        public bool Toggled;
        public float SinceToggle;
    }

    /// <summary>The RADAR doctrine axis on the host (spec WMC rebuild R3; design wmc-rebuild/weapons-radar.md C5): the actual
    /// state is compared with the wanted one every tick, so a rearm or a new pod that comes up on is switched off again; a
    /// toggle waits <see cref="MinGap"/> after our last one (each toggle is a reliable message to every observer).</summary>
    internal static class RadarDiscipline
    {
        public static float MinGap = 2f;

        /// <summary>ON always; SILENT only while engaged; OFF never.</summary>
        public static bool Wanted(RadarPolicy policy, bool engaged) =>
            policy == RadarPolicy.On || (policy == RadarPolicy.Silent && engaged);

        /// <summary>True when the radar must be toggled now.</summary>
        public static bool Step(ref RadarGate g, bool hasRadar, bool isOn, bool wanted, float dt)
        {
            g.SinceToggle += dt;
            if (!hasRadar || isOn == wanted) return false;
            if (g.Toggled && g.SinceToggle < MinGap) return false;
            g.Toggled = true;
            g.SinceToggle = 0f;
            return true;
        }
    }
}
