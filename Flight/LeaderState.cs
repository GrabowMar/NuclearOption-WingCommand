using UnityEngine;

namespace WingCommand
{
    /// <summary>Consistent filtered leader motion from FormationFlyState, shared by both flight models so
    /// geometry and speed control use the same track and sampling interval.</summary>
    internal readonly struct LeaderState
    {
        /// <summary>Filtered travel direction for steering.</summary>
        public readonly Vector3 Track;

        /// <summary>Horizontal travel direction defining the formation frame.</summary>
        public readonly Vector3 FlatTrack;

        /// <summary>Velocity along the filtered slot-frame track.</summary>
        public readonly Vector3 Velocity;

        /// <summary>Filtered heading rate in rad/s, positive right and zero within the deadband.</summary>
        public readonly float TurnRate;

        /// <summary>Signed filtered acceleration in m/s².</summary>
        public readonly float SpeedRate;

        /// <summary>Filtered BankOf angle, negative for right wing down; shared by slot geometry, bank
        /// authority, and roll trim.</summary>
        public readonly float Bank;

        /// <summary>Derivative of filtered bank in rad/s, excluding raw body-rate noise.</summary>
        public readonly float BankRate;

        /// <summary>Smoothed throttle fraction, 0-1; valid only when ThrottleKnown.</summary>
        public readonly float Throttle;

        /// <summary>Whether leader controls were readable. Keep absence separate from idle so failed reads
        /// cannot pull the whole wing's power down.</summary>
        public readonly bool ThrottleKnown;

        /// <summary>Leader body pitch rate in rad/s (positive pitch up).</summary>
        public readonly float PitchRate;

        /// <summary>Leader body roll rate in rad/s (positive right wing down).</summary>
        public readonly float RollRate;

        /// <summary>Leader body yaw rate in rad/s (positive turn right).</summary>
        public readonly float YawRate;

        /// <summary>Leader vertical acceleration in m/s² (positive climb accel).</summary>
        public readonly float VerticalAccel;

        /// <summary>Leader forward pitch angle in degrees relative to horizon.</summary>
        public readonly float Pitch;

        /// <summary>Unfiltered instantaneous leader bank in degrees.</summary>
        public readonly float RawBank;

        /// <summary>Normalized total maneuver intensity 0..1 from angular rates.</summary>
        public readonly float ManeuverIntensity;

        public LeaderState(Vector3 track, Vector3 flatTrack, float turnRate, float speedRate,
                           float bank, float bankRate, float speed, float throttle, bool throttleKnown,
                           float pitchRate = 0f, float rollRate = 0f, float yawRate = 0f,
                           float verticalAccel = 0f, float pitch = 0f, float rawBank = 0f)
        {
            Track = track;
            FlatTrack = flatTrack;
            Velocity = track * speed;
            TurnRate = turnRate;
            SpeedRate = speedRate;
            Bank = bank;
            BankRate = bankRate;
            Throttle = throttle;
            ThrottleKnown = throttleKnown;
            PitchRate = pitchRate;
            RollRate = rollRate;
            YawRate = yawRate;
            VerticalAccel = verticalAccel;
            Pitch = pitch;
            RawBank = rawBank;
            ManeuverIntensity = FormationTracking.ManeuverIntensity(pitchRate, rollRate, turnRate);
        }

        /// <summary>Predict leader speed after leadSeconds.</summary>
        public float PredictedSpeed(float speed, float leadSeconds) =>
            ThrustModel.PredictSpeed(speed, SpeedRate, leadSeconds, WingTuning.MaxCredibleAccel);

        /// <summary>Predict effective climb anticipating vertical acceleration and pitch rate.</summary>
        public float EffectiveClimb(float currentClimb, float horizontalSpeed, float holdBlend, float leadSeconds = 0.55f) =>
            FormationControlRules.EffectiveClimb(currentClimb, VerticalAccel, PitchRate, horizontalSpeed, holdBlend, leadSeconds);

        /// <summary>Horizontal acceleration vector for rotary velocity control.</summary>
        public Vector3 FlatAcceleration =>
            FlatTrack * Mathf.Clamp(SpeedRate, -WingTuning.MaxCredibleAccel,
                                    WingTuning.MaxCredibleAccel);

        public Quaternion Turn(float seconds) => Quaternion.AngleAxis(
            FormationTracking.Sweep(TurnRate, seconds) * Mathf.Rad2Deg, Vector3.up);

    }
}
