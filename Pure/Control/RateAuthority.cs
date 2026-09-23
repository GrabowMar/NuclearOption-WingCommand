using System;

namespace WingCommand
{
    /// <summary>Learns an aircraft's roll authority in flight: the roll rate (deg/s) a full roll stick gives through
    /// the game's fly-by-wire at the current speed. The FBW's rate loop is weak on most airframes and the ailerons
    /// are aero-limited, so the native maximum overstates it several times (CI-22: 286 commanded, ≈ 95°/s real).
    /// <list type="bullet">
    /// <item>Model: the roll rate follows the stick through a first-order lag, p ≈ G·u_f, with u_f the stick lagged
    /// by <see cref="LagSeconds"/>, so it learns while the stick keeps moving.</item>
    /// <item>Only ticks with a clear stick (|u_f| ≥ <see cref="MinStick"/>) and a rate in its direction teach; each
    /// pulls G toward p/u_f with time constant <see cref="LearnSeconds"/>.</item>
    /// <item>G stays within [<see cref="FloorDps"/>, <see cref="CeilingFactor"/>·seed].</item>
    /// </list></summary>
    internal sealed class RollAuthority
    {
        public static float LagSeconds = 0.25f, LearnSeconds = 2f, MinStick = 0.15f;
        public static float FloorDps = 15f, CeilingFactor = 2.5f;

        private float lagged, seed;

        public float RateDps { get; private set; }

        public void Reset(float seedDps)
        {
            seed = Math.Max(FloorDps, seedDps);
            RateDps = seed;
            lagged = 0f;
        }

        /// <param name="appliedStick">Roll stick applied on the previous tick (Pure sign, + right).</param>
        /// <param name="rollRateDps">Measured roll rate now (+ right).</param>
        public void Update(float appliedStick, float rollRateDps, float dt)
        {
            if (dt <= 0f) return;
            lagged += (appliedStick - lagged) * Math.Min(1f, dt / LagSeconds);
            if (Math.Abs(lagged) < MinStick || lagged * rollRateDps <= 0f) return;
            float measured = rollRateDps / lagged;
            RateDps += (measured - RateDps) * Math.Min(1f, dt / LearnSeconds);
            RateDps = Scalar.Clamp(RateDps, FloorDps, CeilingFactor * seed);
        }
    }
}
