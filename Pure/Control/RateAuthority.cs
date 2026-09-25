using System;

namespace WingCommand
{
    /// <summary>Learns one rate axis's authority in flight: the rate (deg/s) a full stick gives through the game's
    /// fly-by-wire at the current speed. Fixed-wing pipelines learn roll (the FBW's rate loop is weak on most airframes
    /// and the ailerons are aero-limited, so the native maximum overstates it several times: CI-22 286 commanded,
    /// ≈ 95°/s real); rotary pipelines learn pitch, roll and yaw.
    /// <list type="bullet">
    /// <item>Model: the roll rate follows the stick through a first-order lag, p ≈ G·u_f, with u_f the stick lagged
    /// by <see cref="LagSeconds"/>, so it learns while the stick keeps moving.</item>
    /// <item>Only ticks with a clear stick (|u_f| ≥ <see cref="MinStick"/>) and a rate in its direction teach; each
    /// pulls G toward p/u_f with time constant <see cref="LearnSeconds"/>.</item>
    /// <item>G stays within [<see cref="FloorDps"/>, <see cref="CeilingFactor"/>·seed].</item>
    /// </list></summary>
    internal sealed class RateAuthority
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

        /// <param name="appliedStick">Stick applied on this axis on the previous tick (Pure sign).</param>
        /// <param name="rateDps">Measured rate on this axis now (same sign as the stick).</param>
        public void Update(float appliedStick, float rateDps, float dt)
        {
            if (dt <= 0f) return;
            lagged += (appliedStick - lagged) * Math.Min(1f, dt / LagSeconds);
            if (Math.Abs(lagged) < MinStick || lagged * rateDps <= 0f) return;
            float measured = rateDps / lagged;
            RateDps += (measured - RateDps) * Math.Min(1f, dt / LearnSeconds);
            RateDps = Scalar.Clamp(RateDps, FloorDps, CeilingFactor * seed);
        }
    }
}
