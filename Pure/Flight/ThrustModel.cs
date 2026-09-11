namespace WingCommand
{
    /// <summary>Approximate throttle/speed feed-forward using thrust proportional to throttle and drag
    /// proportional to speed squared. Anticipating leader lever changes reduces acceleration lag;
    /// closed-loop speed correction handles model error.</summary>
    internal static class ThrustModel
    {
        /// <summary>Estimated level-flight throttle for the requested speed.</summary>
        public static float ThrottleToHold(float speed, float maxSpeed)
        {
            if (maxSpeed <= 0f || speed <= 0f || float.IsNaN(speed)) return 0f;

            float ratio = speed / maxSpeed;
            return Clamp01(ratio * ratio);
        }

        /// <summary>Signed leader throttle minus its estimated steady-speed throttle, representing demand
        /// not yet reflected in speed. Zero at model equilibrium; use the leader's maximum speed so
        /// different airframes do not create a false permanent correction.</summary>
        public static float ThrottleAnticipation(float leaderThrottle, float leaderSpeed,
                                                 float leaderMaxSpeed)
        {
            return Clamp01(leaderThrottle) - ThrottleToHold(leaderSpeed, leaderMaxSpeed);
        }

        /// <summary>Predict speed from measured acceleration over leadSeconds, including dives and turns.
        /// Clamp rate by maxRate to reject respawn, collision, and sampling discontinuities.</summary>
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
