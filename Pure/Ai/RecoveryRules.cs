using System;

namespace WingCommand
{
    /// <summary>How long a refit takes on the ground (spec M3 §4): <see cref="BaseSeconds"/> plus time for the fuel and the
    /// munitions to replace, each as the fraction still aboard (0 empty, 1 full).</summary>
    internal static class RefitTimer
    {
        public static float BaseSeconds = 30f, FuelSeconds = 60f, AmmoSeconds = 60f;

        public static float Seconds(float fuelRemaining, float ammoRemaining) =>
            BaseSeconds + FuelSeconds * (1f - Scalar.Clamp01(fuelRemaining)) + AmmoSeconds * (1f - Scalar.Clamp01(ammoRemaining));
    }

    /// <summary>Bingo fuel for one member (spec M3 §4): it learns the burn rate in flight (fraction of the tanks per
    /// second, low-passed over <see cref="RateTau"/>) and trips once when the fuel left no longer covers the flight to the
    /// field (distance / speed at that rate) plus <see cref="ReserveFraction"/> of the tanks. A refuel (fuel back above
    /// the need plus <see cref="ClearMargin"/>) clears it.</summary>
    internal sealed class BingoMonitor
    {
        public static float RateTau = 60f, ReserveFraction = 0.1f, ClearMargin = 0.05f, MinSpeed = 50f;

        private float lastFuel = float.NaN, rate = float.NaN;

        public bool Bingo { get; private set; }
        public float BurnRate => rate;

        /// <summary>True on the tick bingo is first reached.</summary>
        public bool Update(float fuel, float distanceToField, float speed, float dt)
        {
            if (!float.IsNaN(lastFuel) && dt > 0f)
            {
                float instant = Math.Max(0f, (lastFuel - fuel) / dt);
                rate = float.IsNaN(rate) ? instant : rate + (instant - rate) * Math.Min(1f, dt / RateTau);
            }
            lastFuel = fuel;
            if (float.IsNaN(rate) || rate <= 0f) return false;
            float needed = rate * distanceToField / Math.Max(speed, MinSpeed) + ReserveFraction;
            if (Bingo && fuel > needed + ClearMargin) Bingo = false;
            if (Bingo || fuel >= needed) return false;
            Bingo = true;
            return true;
        }
    }
}
