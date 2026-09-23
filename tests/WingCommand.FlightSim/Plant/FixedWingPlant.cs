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
        public float CornerSpeed = 170f;
        /// <summary>FBW maxRollAngularVel (rad/s). The FBW commands half of it (native units quirk).</summary>
        public float MaxRollAngularVel = 6f;
        public float RollRateMaxDps => 0.5f * MaxRollAngularVel * 57.29578f;
        /// <summary>FBW rollTightness: surface per rad/s of roll-rate error in the rate loop.</summary>
        public float RollTightness = 1f;
        /// <summary>Steady roll rate (rad/s) a full surface gives at <see cref="RollAuthoritySpeed"/>; it scales
        /// with airspeed (fixed-deflection roll rate ∝ V).</summary>
        public float RollAuthorityRadS = 4f;
        public float RollAuthoritySpeed = 200f;
        public float RollLagS = 0.2f;
        public float LoadLagS = 0.25f;
        public float EngineLagS = 1.6f;

        public static PlantParams GenericFighter => new PlantParams();

        /// <summary>A CI-22-like turboprop fitted to the in-game S2 steps: stall ≈ 39 m/s, corner 110 m/s, 6 g,
        /// no afterburner, low excess thrust, and a weak FBW roll loop (maxRollAngularVel 10, rollTightness 0.2)
        /// on aero-limited ailerons: ≈ 95°/s at 110 m/s, ≈ 57°/s at 55 m/s.</summary>
        public static PlantParams CoinTurboprop => new PlantParams
        {
            MassKg = 5000f,
            WingAreaM2 = 25f,
            Cd0 = 0.025f,
            InducedK = 0.07f,
            ClMax = 2.1f,
            DryThrustN = 10000f,
            AfterburnerThrustN = 10000f,
            AfterburnerThrottle = 1f,
            GLimit = 6f,
            CornerSpeed = 110f,
            MaxRollAngularVel = 10f,
            RollTightness = 0.2f,
            RollAuthorityRadS = 2.5f,
            RollAuthoritySpeed = 110f,
            EngineLagS = 1f,
        };
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
        public Vec3 Acceleration { get; private set; }

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
            float qbar = Isa.DynamicPressure(Position.Y, Speed);
            float qRatio = Isa.Density(Position.Y) * Speed * Speed / (Isa.SeaLevelDensity * p.CornerSpeed * p.CornerSpeed);
            float remap = 1f / Math.Max(qRatio, 1f);

            // Roll, as FlyByWire.Filter: the surface is a P rate loop, rollTightness·(0.5·maxRollAngularVel·stick
            // − ω), blended toward direct stick above corner speed, clamped to ±1. The ailerons then drive the
            // roll rate toward a steady value proportional to deflection and airspeed, through a lag.
            float stickRoll = Clamp(input.Roll, -1f, 1f);
            float target = stickRoll * 0.5f * p.MaxRollAngularVel;
            float surface = Clamp(Lerp(stickRoll, p.RollTightness * (target - rollRate), remap), -1f, 1f);
            float authority = p.RollAuthorityRadS * Clamp(Speed / Math.Max(1f, p.RollAuthoritySpeed), 0.1f, 3f);
            rollRate += (authority * surface - rollRate) * Math.Min(1f, dt / p.RollLagS);
            bank = WrapPi(bank + rollRate * dt);

            // Pitch: a g-command, scaled down below corner speed (flight assist on, as native AI states set
            // it; the pure-rate law is only reached with assist off); limited by structure and lift.
            float liftLimit = qbar * p.WingAreaM2 * p.ClMax / (p.MassKg * G);
            float nMax = Math.Min(p.GLimit, liftLimit);
            float nMin = -Math.Min(p.NegativeGLimit, liftLimit);
            float stickPitch = Clamp(input.Pitch, -1f, 1f);
            float pitchRate = stickPitch * p.GLimit * G / Math.Max(Speed, 0.75f * p.CornerSpeed);
            if (qRatio < 1f) pitchRate *= Clamp(qRatio, 0.3f, 1f);
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

            Vec3 before = Velocity;
            Speed = Math.Max(1f, Speed + speedDot * dt);
            gamma = Clamp(gamma + gammaDot * dt, -1.55f, 1.55f);
            heading += headingDot * dt;
            Acceleration = (Velocity - before) / dt;
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
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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
