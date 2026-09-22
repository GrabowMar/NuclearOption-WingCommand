using System;

namespace WingCommand
{
    /// <summary>Inner loops through the game's fly-by-wire:
    /// <list type="bullet">
    /// <item>bank → roll-rate command → roll stick (a rate loop at or below corner speed, blending toward
    /// direct stick above it; the PI trim covers both);</item>
    /// <item>load factor → pitch stick, with a feedforward that inverts the FBW g-command
    /// <c>q = stick·gLimit·g / max(V, 0.75·Vc)</c>;</item>
    /// <item>sideslip → yaw;</item>
    /// <item>energy rate → throttle, with a hysteretic airbrake at exactly zero throttle.</item>
    /// </list>
    /// One instance per aircraft for its lifetime. <see cref="Track"/> keeps it bumpless while another
    /// owner flies.</summary>
    internal sealed class FixedWingController
    {
        public const float RollStickSlew = 4f, PitchStickSlew = 3f, ThrottleSlew = 1f;
        public const float AirbrakeEngageError = -3f, AirbrakeReleaseError = -1.5f, AirbrakeEngageSeconds = 0.5f;
        public const float ThrottleFloor = 0.01f, DryThrottleMax = 0.89f;

        private readonly Pidf roll = new Pidf { MaxRate = RollStickSlew };
        private readonly Pidf pitch = new Pidf { MaxRate = PitchStickSlew };
        private readonly Pidf yaw = new Pidf { Kp = 0.02f, Ki = 0.02f, OutMin = -0.5f, OutMax = 0.5f };
        private readonly Pidf energy = new Pidf { Kp = 0.01f, Ki = 0.004f, MaxRate = ThrottleSlew };
        private FirstOrder bankRate;
        private float previousBankCmd;
        private bool bankPrimed;
        private Persistence airbrakeTimer;
        private bool airbrake;

        public bool AirbrakeLatched => airbrake;

        public ControlOutput Step(in AttitudeCommand cmd, in AircraftState s, AirframeProfile p, float dt)
        {
            if (dt <= 0f) dt = 1f / 60f;
            float schedule = GainSchedule(s, p);

            // Roll: bank error → roll-rate command (+ command-rate feedforward) → stick.
            float rateMax = 0.8f * p.RollRateMaxDps;
            float commandRate = bankPrimed ? Scalar.Wrap180(cmd.BankDeg - previousBankCmd) / dt : 0f;
            previousBankCmd = cmd.BankDeg;
            bankPrimed = true;
            float rateFeedforward = bankRate.Update(Scalar.Clamp(commandRate, -rateMax, rateMax), 0.1f, dt);
            float rateCmd = Scalar.Clamp(p.RollGain * Scalar.Wrap180(cmd.BankDeg - s.BankDeg) + rateFeedforward,
                -rateMax, rateMax);
            roll.Kp = 0.3f / p.RollRateMaxDps * schedule;
            roll.Ki = 0.5f / p.RollRateMaxDps * schedule;
            float rollOut = roll.Update(rateCmd, s.P, dt, rateCmd / p.RollRateMaxDps);

            // Pitch: load factor with the FBW g-command inverted as feedforward.
            pitch.Kp = 0.08f * schedule;
            pitch.Ki = 0.15f * schedule;
            float pitchOut = pitch.Update(cmd.Nz, s.Nz, dt, PitchFeedforward(cmd.Nz, s, p));

            // Yaw toward the air-relative velocity (coordination).
            float yawOut = yaw.Update(s.SideslipDeg, 0f, dt);

            // Energy rate → throttle around a drag-model trim.
            float trim = TrimThrottle(s, p, cmd.Nz);
            float ceiling = cmd.AfterburnerAllowed && p.HasAfterburner ? 1f : DryThrottleMax;
            energy.OutMin = ThrottleFloor - trim;
            energy.OutMax = ceiling - trim;
            float energyError = cmd.EnergyRate - s.EnergyRate;
            float throttle = Scalar.Clamp(trim + energy.Update(cmd.EnergyRate, s.EnergyRate, dt), ThrottleFloor, ceiling);

            bool atFloor = throttle <= ThrottleFloor + 1e-4f;
            if (!airbrake)
                airbrake = cmd.AirbrakeAllowed && !cmd.Gcas &&
                           airbrakeTimer.Update(atFloor && energyError < AirbrakeEngageError, AirbrakeEngageSeconds, dt);
            else if (!cmd.AirbrakeAllowed || cmd.Gcas || energyError > AirbrakeReleaseError)
            {
                airbrake = false;
                airbrakeTimer = default;
            }

            return new ControlOutput
            {
                Roll = rollOut,
                Pitch = pitchOut,
                Yaw = yawOut,
                Throttle = airbrake ? 0f : throttle,
                Airbrake = airbrake,
            };
        }

        /// <summary>Align every loop with the output another owner applied (native AI, the player's stick,
        /// a handover) so the next Step continues without a bump.</summary>
        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p)
        {
            float schedule = GainSchedule(s, p);
            roll.Kp = 0.3f / p.RollRateMaxDps * schedule;
            roll.Ki = 0.5f / p.RollRateMaxDps * schedule;
            roll.Track(s.P, s.P, applied.Roll, s.P / p.RollRateMaxDps);
            pitch.Kp = 0.08f * schedule;
            pitch.Ki = 0.15f * schedule;
            pitch.Track(s.Nz, s.Nz, applied.Pitch, PitchFeedforward(s.Nz, s, p));
            yaw.Track(s.SideslipDeg, 0f, applied.Yaw);
            energy.Track(s.EnergyRate, s.EnergyRate, applied.Throttle - TrimThrottle(s, p, s.Nz));
            previousBankCmd = s.BankDeg;
            bankPrimed = true;
            bankRate.Reset(0f);
            airbrake = applied.Airbrake;
            airbrakeTimer = default;
        }

        /// <summary>Gain scale (RefQ/q)^0.3, clamped to [0.3, 3].</summary>
        public static float GainSchedule(in AircraftState s, AirframeProfile p)
        {
            float refQ = 0.5f * Isa.SeaLevelDensity * p.RefAirspeed * p.RefAirspeed;
            float q = Math.Max(100f, s.Qbar);
            return Scalar.Clamp((float)Math.Pow(refQ / q, 0.3), 0.3f, 3f);
        }

        /// <summary>Throttle that holds speed in steady flight at load factor <paramref name="nz"/>:
        /// parasitic drag ∝ V², induced ∝ n²/V², split 70/30 at the reference speed.</summary>
        public static float TrimThrottle(in AircraftState s, AirframeProfile p, float nz)
        {
            float ratio = Math.Max(0.3f, s.Tas / Math.Max(1f, p.RefAirspeed));
            float trim = p.CruiseThrottle * (0.7f * ratio * ratio + 0.3f * nz * nz / (ratio * ratio));
            return Scalar.Clamp(trim, ThrottleFloor, 1f);
        }

        private static float PitchFeedforward(float nz, in AircraftState s, AirframeProfile p)
        {
            float v = Math.Max(30f, s.Tas);
            float gamma = s.GammaDeg * Scalar.Deg2Rad, bank = s.BankDeg * Scalar.Deg2Rad;
            float gravity = (float)(Math.Cos(gamma) * Math.Cos(bank));
            return (nz - gravity) * Math.Max(v, 0.75f * p.CornerSpeed) / (v * p.GLimit);
        }
    }
}
