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

        public LeaderState(Vector3 track, Vector3 flatTrack, float turnRate, float speedRate,
                           float bank, float bankRate, float speed, float throttle, bool throttleKnown)
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
        }

     /// <summary>Predict leader speed after leadSeconds.</summary>
        public float PredictedSpeed(float speed, float leadSeconds) =>
            ThrustModel.PredictSpeed(speed, SpeedRate, leadSeconds, WingTuning.MaxCredibleAccel);

     /// <summary>Horizontal acceleration vector for rotary velocity control.</summary>
        public Vector3 FlatAcceleration =>
            FlatTrack * Mathf.Clamp(SpeedRate, -WingTuning.MaxCredibleAccel,
                                    WingTuning.MaxCredibleAccel);

        public Quaternion Turn(float seconds) => Quaternion.AngleAxis(
            FormationTracking.Sweep(TurnRate, seconds) * Mathf.Rad2Deg, Vector3.up);

    }
}
