using System;

namespace WingCommand
{
    /// <summary>Converts a commanded acceleration into bank, load factor and an energy-rate demand. Lift
    /// must supply the part of (a + g·up) normal to the velocity; its magnitude is the load factor and
    /// its angle about the velocity axis is the bank. One law for lateral and vertical manoeuvres.</summary>
    internal static class AccelMapping
    {
        /// <summary>Below this lift (in g) bank direction is meaningless; the previous bank is held.</summary>
        public const float MinLiftForBank = 0.2f;

        public static AttitudeCommand Map(in GuidanceCommand cmd, Vec3 velocity, float previousBankDeg)
        {
            var result = new AttitudeCommand
            {
                BankDeg = previousBankDeg,
                Nz = 1f,
                AfterburnerAllowed = cmd.AfterburnerAllowed,
                AirbrakeAllowed = cmd.AirbrakeAllowed,
            };
            float speed = velocity.Length;
            if (speed < 1f) return result;

            Vec3 v = velocity / speed;
            Vec3 total = cmd.Accel + Vec3.Up * Scalar.G;
            Vec3 lift = total - v * Vec3.Dot(total, v);
            result.Nz = lift.Length / Scalar.G;

            Vec3 right = Vec3.Cross(Vec3.Up, v);
            if (right.SqrLength > 1e-6f && result.Nz >= MinLiftForBank)
            {
                right = right.Normalized;
                Vec3 up = Vec3.Cross(v, right);
                result.BankDeg = (float)Math.Atan2(Vec3.Dot(lift, right), Vec3.Dot(lift, up)) * Scalar.Rad2Deg;
            }
            result.EnergyRate = speed * Vec3.Dot(cmd.Accel, v) / Scalar.G + cmd.VelCmd.Y;
            return result;
        }
    }
}
