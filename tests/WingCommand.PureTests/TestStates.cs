namespace WingCommand.PureTests
{
    /// <summary>Aircraft states as the sensor fills them, for tests that need a flying aircraft.</summary>
    internal static class TestStates
    {
        public static AircraftState Flying(Vec3 pos, Vec3 vel)
        {
            Vec3 fwd = vel.Normalized;
            return new AircraftState
            {
                Pos = pos, Vel = vel, Fwd = fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, fwd).Normalized,
                Tas = vel.Length, Qbar = Isa.DynamicPressure(pos.Y, vel.Length), Nz = 1f, RadarAlt = pos.Y,
                FbwActive = true, Dt = 1f / 60f,
            };
        }
    }
}
