using System;
using System.Numerics;

namespace WingCommand
{
    /// <summary>Finite braking distance, pursuit power, and hysteretic speed-brake authority.</summary>
    internal static class FormationClosure
    {
        internal readonly struct Controls
        {
            public readonly float Throttle;
            public readonly bool Airbrake;
            public Controls(float throttle, bool airbrake) { Throttle = throttle; Airbrake = airbrake; }
        }

        public static float SafeClosure(float distance, float braking, float responseSeconds)
        {
            double decel = Math.Max(0.1f, braking);
            double lag = decel * Math.Max(0f, responseSeconds);
            return (float)(Math.Sqrt(lag * lag + 2d * decel * Math.Max(0f, distance)) - lag);
        }

        public static float LoadedMinimum(float minimumAirspeed, float bankDegrees) =>
            minimumAirspeed / (float)Math.Sqrt(Math.Max(0.25d, Math.Cos(bankDegrees * Math.PI / 180d)));

        public static float PursuitSpeed(Vector2 toSlot, Vector2 ownVelocity, FormationIntercept.Plan intercept,
            float stationSpeed, float maxSpeed, float braking, float responseSeconds, float spacing)
        {
            float distance = toSlot.Length();
            if (distance < 1f || intercept.Gap.LengthSquared() < 1f) return stationSpeed;
            float reserve = Math.Max(30f, spacing * 0.5f);
            // Use the real gap for energy, never the distant predicted aim point.
            float closure = SafeClosure(distance - reserve, braking, responseSeconds);
            Vector2 desired = intercept.ArrivalVelocity + Vector2.Normalize(intercept.Gap) * closure;
            float pursuit = Math.Min(maxSpeed, desired.Length());
            float alignment = ownVelocity.LengthSquared() > 1f
                ? Vector2.Dot(Vector2.Normalize(ownVelocity), Vector2.Normalize(intercept.Gap)) : 0f;
            float aligned = Math.Max(0f, Math.Min(1f, (alignment - 0.1f) / 0.55f));
            aligned *= aligned * (3f - 2f * aligned);
            return stationSpeed + (pursuit - stationSpeed) * FormationIntercept.LongRangeBlend(distance) * aligned;
        }

        public static Controls Resolve(float throttle, float speedError, float airspeed, float minimumAirspeed,
            float distance, float closing, float spacing, float braking, float responseSeconds,
            float alignment, float bankDegrees, float radarAltitude, float verticalSpeed,
            bool terrainWarning, bool allowPursuit, bool allowBraking, bool wasBraking)
        {
            float reserve = Math.Max(30f, spacing * 0.5f);
            float safeClosure = SafeClosure(distance - reserve, braking, responseSeconds);
            bool overspeed = closing > safeClosure + (wasBraking ? -3f : 3f);
            // Brakes release while closure is still positive, allowing engine spool
            // before co-airspeed. Separate on/off thresholds avoid actuator chatter.
            float loadedMinimum = LoadedMinimum(minimumAirspeed, bankDegrees);
            bool canShedEnergy = allowBraking && !terrainWarning && radarAltitude > 250f &&
                Math.Abs(bankDegrees) < 50f && verticalSpeed < 5f &&
                airspeed > loadedMinimum + (wasBraking ? 8f : 15f);
            bool brake = canShedEnergy &&
                closing > (wasBraking ? 6f : 10f) && speedError < (wasBraking ? -3f : -8f) && overspeed;

            if (allowPursuit && distance > 1500f && alignment > 0.65f &&
                speedError > 3f && closing < safeClosure - 5f && !terrainWarning)
                throttle = 1f;
            if (canShedEnergy && overspeed && closing > 6f && speedError < -3f)
                throttle = Math.Min(throttle, 0.1f);
            if (brake) throttle = 0f;
            // Energy protection has the final say over both arrival and braking.
            if (airspeed < loadedMinimum) return new Controls(1f, false);
            // Native Airbrake.Update deploys on EXACT zero throttle. Positive idle
            // retracts fitted brakes; ControlInputs.brake is the wheel brake.
            return new Controls(brake ? 0f : Math.Max(0.01f, Math.Min(1f, throttle)), brake);
        }
    }
}
