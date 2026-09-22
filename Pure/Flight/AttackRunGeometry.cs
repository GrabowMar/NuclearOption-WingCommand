using System;

namespace WingCommand
{
    /// <summary>Energy geometry for an ordered attack run. Aim offsets are metres in the
    /// attacker-to-target frame: along the line of sight, right, and up.</summary>
    internal enum AttackRunShape
    {
        RunIn = 0,
        Overshoot = 1,
        HighClosure = 2,
        LowEnergy = 3,
        Lag = 4,
    }

    internal readonly struct AttackRunAdvice
    {
        public readonly AttackRunShape Shape;
        public readonly float Along;
        public readonly float Right;
        public readonly float Up;
        public readonly float Throttle;
        public readonly bool HoldFire;
        public readonly bool FollowTerrain;

        public AttackRunAdvice(AttackRunShape shape, float along, float right, float up,
            float throttle, bool holdFire, bool followTerrain)
        {
            Shape = shape;
            Along = along;
            Right = right;
            Up = up;
            Throttle = throttle;
            HoldFire = holdFire;
            FollowTerrain = followTerrain;
        }
    }

    internal static class AttackRunGeometry
    {
        public const float OvershootRange = 800f;
        public const float OvershootWideRange = 2500f;
        public const float HighClosureMin = 400f;
        public const float HighClosureMax = 2800f;
        public const float LowEnergyRange = 2200f;
        public const float LagMin = 300f;
        public const float LagMax = 3500f;

        public static AttackRunAdvice Evaluate(
            float distance,
            float forwardDot,
            float aspectDot,
            float mySpeed,
            float targetSpeed,
            float cornerSpeed,
            float radarAlt,
            bool surface,
            bool leadPursuit,
            bool energyFighter)
        {
            if ((distance < OvershootRange && forwardDot < 0.2f) ||
                (forwardDot < -0.1f && distance < OvershootWideRange))
            {
                return new AttackRunAdvice(AttackRunShape.Overshoot, 0f, 0f, 600f, 1f, true, true);
            }

            if (distance > HighClosureMin && distance < HighClosureMax &&
                targetSpeed > 1f && mySpeed > targetSpeed * 1.08f &&
                forwardDot < 0.91f && radarAlt > 350f)
            {
                float throttle = energyFighter ? 1f : 0.7f;
                return new AttackRunAdvice(AttackRunShape.HighClosure, distance * 0.35f, 0f, 400f,
                    throttle, false, true);
            }

            if (!energyFighter && distance < LowEnergyRange && cornerSpeed > 1f &&
                mySpeed < cornerSpeed * 0.92f && forwardDot < 0.87f && radarAlt > 280f)
            {
                return new AttackRunAdvice(AttackRunShape.LowEnergy, 0f, 0f, -150f, 1f, false, true);
            }

            if (aspectDot < -0.34f && forwardDot < 0.95f &&
                distance > LagMin && distance < LagMax)
            {
                return new AttackRunAdvice(AttackRunShape.Lag, -400f, 0f, 0f, 1f, false, true);
            }

            float along = 0f;
            if (leadPursuit)
                along = Math.Max(150f, distance * 0.12f);

            bool diving = surface && forwardDot > 0.85f && distance < OvershootWideRange;
            return new AttackRunAdvice(AttackRunShape.RunIn, along, 0f, 0f, 1f, false, !diving);
        }
    }
}
