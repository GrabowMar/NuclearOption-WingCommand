using System;

namespace WingCommand
{
    /// <summary>
    /// The relationship between a throttle lever and the speed it eventually produces, and
    /// the predictions the formation builds on it.
    ///
    /// Why a model at all: a wingman that steers by the leader's *current* speed is a
    /// proportional controller fed a ramp, and a proportional controller cannot track a
    /// ramp without a standing error. While the player accelerates, the wingman's speed
    /// sits a fixed amount below the leader's, so the gap grows until the position term is
    /// large enough to make up the difference — and it then holds that gap for as long as
    /// the acceleration lasts. That is the "falls behind whenever I push the throttle up"
    /// report, and its mirror image on the way back down.
    ///
    /// The lever, though, is known the instant the player moves it, seconds before any of
    /// that shows up as a speed difference. So the fix is to fly the leader's *intent*
    /// rather than only its state.
    ///
    /// The model is the standard high-speed one: thrust rises with throttle, drag rises
    /// with the square of speed, so the speed a lever setting can sustain goes as its
    /// square root and the lever needed to hold a speed goes as that speed squared. It is
    /// exact for neither airframe and does not need to be — it is a feed-forward, and the
    /// speed loop it feeds still closes on the residual. What it has to get right is the
    /// direction and rough size of a change the player just commanded, and for that the
    /// square law is much closer than assuming a lever and a speed are the same number.
    /// </summary>
    internal static class ThrustModel
    {
        /// <summary>The lever position that holds <paramref name="speed"/> in level flight.</summary>
        public static float ThrottleToHold(float speed, float maxSpeed)
        {
            if (maxSpeed <= 0f || speed <= 0f || float.IsNaN(speed)) return 0f;

            float ratio = speed / maxSpeed;
            return Clamp01(ratio * ratio);
        }

        /// <summary>The speed <paramref name="throttle"/> eventually settles at in level flight.</summary>
        public static float SpeedAtThrottle(float throttle, float maxSpeed)
        {
            if (maxSpeed <= 0f) return 0f;
            return maxSpeed * (float)Math.Sqrt(Clamp01(throttle));
        }

        /// <summary>
        /// The throttle change the leader has just commanded but not yet flown, as a
        /// fraction of full lever travel: where its lever is now, minus where its lever
        /// would be to hold the speed it currently has.
        ///
        /// This is the whole anticipation, and its two properties are what make it safe to
        /// add to a wingman's throttle outright. It is <b>zero whenever the leader is
        /// settled</b> — lever and speed agree, nothing has been commanded, nothing is fed
        /// forward — so it cannot bias steady formation flight. And it is <b>symmetric</b>:
        /// pushing the lever up returns positive and pulling it back returns negative, so
        /// the wingman comes off the power with the player instead of sailing past, which
        /// the previous <c>Mathf.Max(throttle, leaderThrottle)</c> anticipation could never
        /// do because it could only ever add.
        ///
        /// It is expressed against the <em>leader's</em> own maximum on purpose. A fraction
        /// of lever travel transfers between airframes; a speed does not. Measuring it
        /// against the wingman's maximum instead would make a leader that is simply faster
        /// than the wingman read as a permanent power cut, and pull power from the one
        /// aircraft that needs all of it.
        /// </summary>
        public static float ThrottleAnticipation(float leaderThrottle, float leaderSpeed,
                                                 float leaderMaxSpeed)
        {
            return Clamp01(leaderThrottle) - ThrottleToHold(leaderSpeed, leaderMaxSpeed);
        }

        /// <summary>
        /// Where the leader's speed will be <paramref name="leadSeconds"/> from now, from
        /// its measured rate of change.
        ///
        /// The companion to <see cref="ThrottleAnticipation"/> and the half of the pair that
        /// is ground truth: the lever says what was asked for, this says what is actually
        /// happening — including everything the lever cannot explain, such as a leader
        /// accelerating down a dive or bleeding speed round a hard turn.
        ///
        /// <paramref name="maxRate"/> is a credibility limit, not a tuning knob. The rate is
        /// differentiated from a velocity, so a respawn, a collision or a dropped frame can
        /// present an arbitrarily large step; without the clamp one of those would be
        /// projected forward into a nonsense speed demand.
        /// </summary>
        public static float PredictSpeed(float speed, float rate, float leadSeconds,
                                         float maxRate)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate)) return speed;

            float credible = rate < -maxRate ? -maxRate : (rate > maxRate ? maxRate : rate);
            float predicted = speed + credible * leadSeconds;
            return predicted > 0f ? predicted : 0f;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
