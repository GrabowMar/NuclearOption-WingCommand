using System;

namespace WingCommand
{
    /// <summary>Two-degree-of-freedom PID with filtered derivative, output clamp and slew, and
    /// back-calculation anti-windup (after NOAutopilot's controller). Parallel gains in output units per
    /// error unit. <see cref="Track"/> aligns the state with an externally applied output so control can
    /// be handed back without a bump.</summary>
    internal sealed class Pidf
    {
        public float Kp, Ki, Kd;
        /// <summary>Setpoint weight on the proportional term (1 = standard).</summary>
        public float B = 1f;
        /// <summary>Setpoint weight on the derivative term (0 = no kick on setpoint steps).</summary>
        public float C;
        public float DerivativeTau = 0.05f;
        public float OutMin = -1f, OutMax = 1f;
        /// <summary>Output rate limit in output units per second; infinity disables it.</summary>
        public float MaxRate = float.PositiveInfinity;

        private float integral, derivative, previousDerivativeInput;
        private bool primed;

        public float Output { get; private set; }
        public float Integral => integral;

        public float Update(float setpoint, float measurement, float dt, float feedForward = 0f)
        {
            if (dt <= 0f) return Output;
            float error = setpoint - measurement;
            float p = Kp * (B * setpoint - measurement);
            float derivativeInput = C * setpoint - measurement;
            if (primed && Kd != 0f)
            {
                float raw = (derivativeInput - previousDerivativeInput) / dt;
                float alpha = DerivativeTau > 0f ? 1f - (float)Math.Exp(-dt / DerivativeTau) : 1f;
                derivative += (raw - derivative) * alpha;
            }
            previousDerivativeInput = derivativeInput;

            float unsaturated = p + integral + Kd * derivative + feedForward;
            float output = Scalar.Clamp(unsaturated, OutMin, OutMax);
            if (primed && !float.IsPositiveInfinity(MaxRate)) output = Slew.Step(Output, output, MaxRate, dt);
            if (Ki != 0f)
                integral += Ki * error * dt + Math.Min(1f, dt / TrackingTime()) * (output - unsaturated);

            primed = true;
            Output = output;
            return output;
        }

        /// <summary>Another source applied <paramref name="applied"/>. Set the integral so the next Update
        /// continues from it; the derivative restarts. A loop without integral action keeps none.</summary>
        public void Track(float setpoint, float measurement, float applied, float feedForward = 0f)
        {
            float p = Kp * (B * setpoint - measurement);
            integral = Ki != 0f ? applied - p - feedForward : 0f;
            derivative = 0f;
            previousDerivativeInput = C * setpoint - measurement;
            Output = applied;
            primed = true;
        }

        public void Reset()
        {
            integral = derivative = previousDerivativeInput = 0f;
            Output = 0f;
            primed = false;
        }

        private float TrackingTime()
        {
            float ti = Kp > 0f ? Kp / Math.Abs(Ki) : 1f / Math.Abs(Ki);
            float td = Kp > 0f ? Kd / Kp : 0f;
            float tt = td > 0f ? (float)Math.Sqrt(ti * td) : ti;
            return Math.Max(1e-3f, tt);
        }
    }
}
