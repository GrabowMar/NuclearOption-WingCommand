using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Everything the formation knows about how the leader is moving, filtered once by
    /// <see cref="FormationFlyState"/> and handed to whichever flight model is flying.
    ///
    /// It is one type rather than a handful of loose parameters for the reason the turn
    /// rate had to be filtered in the first place: these signals are only correct
    /// *together*. The track, the heading rate and the speed rate are all differentiated
    /// from the same smoothed leader velocity over the same tick, and deriving any one of
    /// them separately - from the nose direction, from the rigidbody's angular velocity, or
    /// from a raw velocity delta - is what previously let them disagree about which way and
    /// how fast the leader was actually going.
    /// </summary>
    internal readonly struct LeaderState
    {
        /// <summary>Smoothed direction of travel. The steering reference.</summary>
        public readonly Vector3 Track;

        /// <summary>The same track flattened into the horizontal plane; the formation's frame.</summary>
        public readonly Vector3 FlatTrack;

        /// <summary>Filtered heading rate, rad/s, positive to the right, zero inside the noise band.</summary>
        public readonly float TurnRate;

        /// <summary>Filtered rate of change of the leader's speed, m/s². Signed.</summary>
        public readonly float SpeedRate;

        /// <summary>Smoothed lever position, 0-1. Meaningless unless <see cref="ThrottleKnown"/>.</summary>
        public readonly float Throttle;

        /// <summary>
        /// Whether the leader's control inputs could be read at all. It matters that this is
        /// a separate flag: a missing <c>ControlInputs</c> read as a throttle of zero would
        /// be indistinguishable from a leader that has genuinely chopped to idle, and the
        /// anticipation would answer it by pulling the whole wing's power to nothing.
        /// </summary>
        public readonly bool ThrottleKnown;

        public LeaderState(Vector3 track, Vector3 flatTrack, float turnRate, float speedRate,
                           float throttle, bool throttleKnown)
        {
            Track = track;
            FlatTrack = flatTrack;
            TurnRate = turnRate;
            SpeedRate = speedRate;
            Throttle = throttle;
            ThrottleKnown = throttleKnown;
        }

        /// <summary>The leader's speed <paramref name="leadSeconds"/> from now.</summary>
        public float PredictedSpeed(float speed, float leadSeconds) =>
            ThrustModel.PredictSpeed(speed, SpeedRate, leadSeconds, WingTuning.MaxCredibleAccel);

        /// <summary>
        /// The leader's acceleration as a horizontal vector, for the rotary model - which
        /// commands a velocity rather than a speed and so needs the direction with it.
        /// </summary>
        public Vector3 FlatAcceleration =>
            FlatTrack * Mathf.Clamp(SpeedRate, -WingTuning.MaxCredibleAccel,
                                    WingTuning.MaxCredibleAccel);
    }
}
