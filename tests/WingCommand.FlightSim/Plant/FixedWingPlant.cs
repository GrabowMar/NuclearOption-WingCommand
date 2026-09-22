using System;

namespace WingCommand.FlightSim
{
    /// <summary>Stick and throttle, as written to the game's ControlInputs (pitch/roll in [-1, 1]).</summary>
    internal readonly struct PlantInput
    {
        public readonly float Pitch, Roll, Throttle;

        public PlantInput(float pitch, float roll, float throttle)
        {
            Pitch = pitch;
            Roll = roll;
            Throttle = throttle;
        }
    }

    /// <summary>Reduced-order airframe constants. Defaults are a generic fighter until in-game calibration
    /// (M0 spike S2 / Calibration mode) fits per-airframe values.</summary>
    internal sealed class PlantParams
    {
        public float MassKg = 12000f;
        public float WingAreaM2 = 38f;
        public float Cd0 = 0.022f;
        public float InducedK = 0.12f;
        public float ClMax = 1.4f;
        public float AirbrakeCd = 0.06f;
        public float DryThrustN = 80000f;
        public float AfterburnerThrustN = 125000f;
        public float AfterburnerThrottle = 0.9f;
        public float GLimit = 9f;
        public float NegativeGLimit = 3f;
        public float RollRateMaxDps = 210f;
        public float PitchRateMaxDps = 25f;
        public float RollLagS = 0.18f;
        public float LoadLagS = 0.25f;
        public float EngineLagS = 1.6f;

        public static PlantParams GenericFighter => new PlantParams();
    }

    /// <summary>Point-mass fixed-wing model with a rate-command fly-by-wire: roll stick commands roll rate,
    /// pitch stick commands pitch rate (converted to load factor), both through first-order lags and limited
    /// by the g limit and available lift. Coordinated flight (no sideslip); engine is a first-order lag with
    /// an afterburner step; the airbrake opens only at exactly zero throttle, as in the game.</summary>
    internal sealed class FixedWingPlant
    {
        private const float G = 9.81f;
        private const float Deg = (float)(Math.PI / 180.0);
        private readonly PlantParams p;
        private float gamma, heading, bank, rollRate;

        public FixedWingPlant(PlantParams parameters, Vec3 position, float speed, float headingDeg)
        {
            p = parameters;
            Position = position;
            Speed = speed;
            heading = headingDeg * Deg;
            LoadFactor = 1f;
            ThrottleActual = TrimThrottle();
        }

        public Vec3 Position { get; private set; }
        public float Speed { get; private set; }
        public float LoadFactor { get; private set; }
        public float ThrottleActual { get; private set; }
        public bool AirbrakeOpen { get; private set; }

        public float HeadingDeg
        {
            get
            {
                float d = heading / Deg % 360f;
                return d < 0f ? d + 360f : d;
            }
        }

        public float BankDeg => bank / Deg;
        public float GammaDeg => gamma / Deg;
        public float RollRateDps => rollRate / Deg;

        public Vec3 Velocity => new Vec3(
            (float)(Math.Sin(heading) * Math.Cos(gamma)) * Speed,
            (float)Math.Sin(gamma) * Speed,
            (float)(Math.Cos(heading) * Math.Cos(gamma)) * Speed);

        public void SetThrottleState(float throttle) => ThrottleActual = Clamp(throttle, 0f, 1f);
        public void SetBankState(float bankDeg) => bank = bankDeg * Deg;

        /// <summary>Throttle that balances drag in 1 g level flight at the current speed and height.</summary>
        public float TrimThrottle()
        {
            float drag = Drag(Isa.DynamicPressure(Position.Y, Speed), 1f, airbrake: false);
            return ThrottleFor(drag);
        }

        public void Step(in PlantInput input, float dt)
        {
            // Roll: rate command through a first-order lag.
            float rollCmd = Clamp(input.Roll, -1f, 1f) * p.RollRateMaxDps * Deg;
            rollRate += (rollCmd - rollRate) * Math.Min(1f, dt / p.RollLagS);
            bank = WrapPi(bank + rollRate * dt);

            // Pitch: rate command -> load factor, limited by structure and available lift.
            float qbar = Isa.DynamicPressure(Position.Y, Speed);
            float liftLimit = qbar * p.WingAreaM2 * p.ClMax / (p.MassKg * G);
            float nMax = Math.Min(p.GLimit, liftLimit);
            float nMin = -Math.Min(p.NegativeGLimit, liftLimit);
            float pitchRate = Clamp(input.Pitch, -1f, 1f) * p.PitchRateMaxDps * Deg;
            float nCmd = Speed * pitchRate / G + (float)(Math.Cos(gamma) * Math.Cos(bank));
            nCmd = Clamp(nCmd, nMin, nMax);
            LoadFactor += (nCmd - LoadFactor) * Math.Min(1f, dt / p.LoadLagS);
            LoadFactor = Clamp(LoadFactor, nMin, nMax);

            // Engine and airbrake.
            float throttleCmd = Clamp(input.Throttle, 0f, 1f);
            ThrottleActual += (throttleCmd - ThrottleActual) * Math.Min(1f, dt / p.EngineLagS);
            AirbrakeOpen = throttleCmd == 0f;

            // Point-mass equations of motion (coordinated, no sideslip).
            float thrust = Thrust(ThrottleActual);
            float drag = Drag(qbar, LoadFactor, AirbrakeOpen);
            float speedDot = (thrust - drag) / p.MassKg - G * (float)Math.Sin(gamma);
            float v = Math.Max(Speed, 1f);
            float gammaDot = G / v * (LoadFactor * (float)Math.Cos(bank) - (float)Math.Cos(gamma));
            float cosGamma = Math.Max((float)Math.Cos(gamma), 0.05f);
            float headingDot = G / v * LoadFactor * (float)Math.Sin(bank) / cosGamma;

            Speed = Math.Max(1f, Speed + speedDot * dt);
            gamma = Clamp(gamma + gammaDot * dt, -1.55f, 1.55f);
            heading += headingDot * dt;
            Position += Velocity * dt;
        }

        private float Drag(float qbar, float loadFactor, bool airbrake)
        {
            float cl = qbar > 1f ? loadFactor * p.MassKg * G / (qbar * p.WingAreaM2) : 0f;
            float cd = p.Cd0 + p.InducedK * cl * cl + (airbrake ? p.AirbrakeCd : 0f);
            return qbar * p.WingAreaM2 * cd;
        }

        private float Thrust(float throttle)
        {
            if (throttle <= p.AfterburnerThrottle) return throttle / p.AfterburnerThrottle * p.DryThrustN;
            float ab = (throttle - p.AfterburnerThrottle) / (1f - p.AfterburnerThrottle);
            return p.DryThrustN + ab * (p.AfterburnerThrustN - p.DryThrustN);
        }

        private float ThrottleFor(float thrust)
        {
            if (thrust <= p.DryThrustN) return Clamp(thrust / p.DryThrustN * p.AfterburnerThrottle, 0f, 1f);
            float ab = (thrust - p.DryThrustN) / (p.AfterburnerThrustN - p.DryThrustN);
            return Clamp(p.AfterburnerThrottle + ab * (1f - p.AfterburnerThrottle), 0f, 1f);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static float WrapPi(float a)
        {
            const float twoPi = (float)(2 * Math.PI);
            a %= twoPi;
            if (a > Math.PI) a -= twoPi;
            if (a <= -Math.PI) a += twoPi;
            return a;
        }
    }
}
