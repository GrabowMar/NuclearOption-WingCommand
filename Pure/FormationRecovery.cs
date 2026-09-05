using System;

namespace WingCommand
{
    internal enum FormationRecoveryMode { Station, SlowLeader, Overshoot }

    /// <summary>Per-wingman flight memory. No shared timers or airframe estimates.</summary>
    internal sealed class FormationRecovery
    {
        public FormationRecoveryMode Mode { get; private set; }
        public float Blend { get; private set; }
        public float Braking { get; private set; } = WingTuning.FormationInitialBraking;
        public float ResponseSeconds { get; private set; } = WingTuning.SpeedLeadSeconds;
        private float slowTime, readyTime, lastSpeed, lastThrottle, stableThrottle, responseTime;
        private bool sampled, awaitingResponse;
        private float burstRemaining, burstCooldown, reportElapsed;

        public bool UpdateMode(float leaderSpeed, float minimumSpeed, float gap, float spacing, float dt,
                               bool allowSlowLeader = true)
        {
            dt = Clamp(dt, 0f, 0.5f);
            var previous = Mode;
            slowTime = allowSlowLeader && leaderSpeed < minimumSpeed ? slowTime + dt : 0f;
            readyTime = leaderSpeed > minimumSpeed + WingTuning.FormationSpeedHysteresis ? readyTime + dt : 0f;
            if (slowTime >= WingTuning.FormationSlowEntrySeconds) Mode = FormationRecoveryMode.SlowLeader;
            else if (Mode == FormationRecoveryMode.SlowLeader &&
                     (!allowSlowLeader || readyTime >= WingTuning.FormationSlowExitSeconds))
                Mode = gap < -spacing ? FormationRecoveryMode.Overshoot : FormationRecoveryMode.Station;
            else if (Mode == FormationRecoveryMode.Station && gap < -Math.Max(spacing, WingTuning.FormationOvershootEntry))
                Mode = FormationRecoveryMode.Overshoot;
            else if (Mode == FormationRecoveryMode.Overshoot && gap > spacing * 0.5f)
                Mode = FormationRecoveryMode.Station;
            Blend = Move(Blend, Mode == FormationRecoveryMode.Station ? 0f : 1f,
                dt / WingTuning.FormationRecoveryBlendSeconds);
            return previous != Mode;
        }

        // Only stable, level flight at a settled throttle can identify drag. Reject
        // discontinuities and reset across manoeuvres; never learn from a collision.
        public void Observe(float speed, float throttle, bool stableFlight, float dt)
        {
            if (!sampled || dt <= 0f || dt > 0.5f || !stableFlight)
            {
                sampled = true; lastSpeed = speed; lastThrottle = throttle;
                stableThrottle = 0f; awaitingResponse = false; return;
            }
            float accel = (speed - lastSpeed) / dt;
            float leverChange = throttle - lastThrottle;
            stableThrottle = Math.Abs(leverChange) < 0.02f ? stableThrottle + dt : 0f;
            if (leverChange > 0.25f) { awaitingResponse = true; responseTime = 0f; }
            if (leverChange < -0.1f) awaitingResponse = false;
            if (awaitingResponse)
            {
                responseTime += dt;
                if (accel > 0.5f && accel < WingTuning.MaxCredibleAccel)
                {
                    ResponseSeconds += (Clamp(responseTime, 0.25f, 2f) - ResponseSeconds) * 0.15f;
                    awaitingResponse = false;
                }
                if (responseTime > 2f) awaitingResponse = false;
            }
            if (throttle < 0.1f && stableThrottle > 2f && accel < -0.2f && accel > -8f)
            {
                float observed = Clamp(-accel * 0.8f, 0.5f, 6f);
                // Weak brakes are learned faster than strong ones: underestimating
                // stopping distance is the dangerous side of the model error.
                float tau = observed < Braking ? 1f : 8f;
                Braking += (observed - Braking) * (1f - (float)Math.Exp(-dt / tau));
            }
            lastSpeed = speed; lastThrottle = throttle;
        }

        public bool BurstReport(bool unstable, float dt)
        {
            burstCooldown = Math.Max(0f, burstCooldown - dt);
            if (unstable && burstCooldown <= 0f)
            {
                burstRemaining = WingTuning.FormationBurstSeconds;
                burstCooldown = WingTuning.FormationBurstCooldown;
                reportElapsed = WingTuning.FormationBurstInterval;
            }
            bool active = burstRemaining > 0f;
            burstRemaining = Math.Max(0f, burstRemaining - dt);
            reportElapsed += dt;
            if (!active || reportElapsed < WingTuning.FormationBurstInterval) return false;
            reportElapsed = 0f;
            return true;
        }

        public static float Move(float current, float target, float maximumChange) =>
            current + Clamp(target - current, -Math.Max(0f, maximumChange), Math.Max(0f, maximumChange));

        // Preserve the side already occupied, rather than crossing the leader's nose
        // to reach a parity-assigned lane. Parity only resolves an ambiguous centreline.
        public static float LaneSide(float lateral, float spacing, int slot) =>
            Math.Abs(lateral) > spacing * 0.5f ? Math.Sign(lateral) : (slot % 2 == 0 ? 1f : -1f);

        public static float LaneCorrection(float lateral, float lane, float baseline) =>
            Clamp((lane - lateral) / Math.Max(1f, baseline), -0.5f, 0.5f);
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
