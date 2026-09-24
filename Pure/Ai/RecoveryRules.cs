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
    /// field (distance / cruise speed at the rate learned outside afterburner) plus <see cref="ReserveFraction"/> of the tanks. A refuel (fuel back above
    /// the need plus <see cref="ClearMargin"/>) clears it.</summary>
    internal sealed class BingoMonitor
    {
        public static float RateTau = 60f, ReserveFraction = 0.1f, ClearMargin = 0.05f, MinSpeed = 50f, JokerMargin = 0.15f;

        private float lastFuel = float.NaN, rate = float.NaN, need;

        public bool Bingo { get; private set; }
        public float BurnRate => rate;
        /// <summary>Fuel within <see cref="JokerMargin"/> of bingo (spec M5 §6.2); cleared by a refuel as bingo is.</summary>
        public bool Joker { get; private set; }
        /// <summary>True on the check Joker is first reached (not when the fuel went straight past bingo).</summary>
        public bool JokerNow { get; private set; }

        /// <summary>Seconds until bingo at the learned burn rate (+inf before a rate is learned).</summary>
        public float SecondsToBingo => float.IsNaN(rate) || rate <= 0f ? float.PositiveInfinity : (lastFuel - need) / rate;

        /// <summary>True on the tick bingo is first reached. The flight home is judged at <paramref name="cruiseSpeed"/>
        /// and the burn rate learned outside <paramref name="afterburner"/> (in game a defensive break in afterburner, slow
        /// at its end, called Joker and Bingo a second apart 15 km from the field).</summary>
        public bool Update(float fuel, float distanceToField, float cruiseSpeed, float dt, bool afterburner = false)
        {
            JokerNow = false;
            if (!float.IsNaN(lastFuel) && dt > 0f && !afterburner)
            {
                float instant = Math.Max(0f, (lastFuel - fuel) / dt);
                rate = float.IsNaN(rate) ? instant : rate + (instant - rate) * Math.Min(1f, dt / RateTau);
            }
            lastFuel = fuel;
            if (float.IsNaN(rate) || rate <= 0f) return false;
            float needed = rate * distanceToField / Math.Max(cruiseSpeed, MinSpeed) + ReserveFraction;
            need = needed;
            if (Joker && fuel > needed + JokerMargin + ClearMargin) Joker = false;
            if (!Joker && fuel < needed + JokerMargin)
            {
                Joker = true;
                JokerNow = fuel >= needed;
            }
            if (Bingo && fuel > needed + ClearMargin) Bingo = false;
            if (Bingo || fuel >= needed) return false;
            Bingo = true;
            return true;
        }
    }
}
