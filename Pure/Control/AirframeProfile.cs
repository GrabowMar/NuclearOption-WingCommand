using System;
using System.Collections.Generic;
using System.Reflection;

namespace WingCommand
{
    internal enum AirframeClass { FixedWing, Rotary, Tiltwing }

// Filled by the engine from AircraftParameters and the FBW (M1c) and by tests.
#pragma warning disable CS0649
    /// <summary>Native numbers the engine reads to derive a profile: AircraftParameters, the airframe's
    /// published stall speed and its fly-by-wire fields. Zero means unknown.</summary>
    internal struct ProfileInputs
    {
        public string UnitName;
        public AirframeClass Class;
        public float GLimit, PidReferenceAirspeed, MaxSpeed, CornerSpeed, TakeoffSpeed, LandingSpeed;
        public float PublishedStallKmh, CruiseThrottle, MaxRadius;
        /// <summary>FBW fields: rad/s, g, m/s.</summary>
        public float FbwMaxRollAngularVel, FbwGLimit, FbwCornerSpeed;
        /// <summary>Helicopter FBW rate limits (rad/s) and g limit, and the collective its own autopilot hovers at.</summary>
        public float HeloPitchRate, HeloYawRate, HeloRollRate, HeloGLimit, HoverCollective;
        /// <summary>Landing gear: the steering leg's lock (degrees, signed: its sign reverses the wheel) and slew rate,
        /// the wheelbase (steering leg to braked legs), and the wingspan.</summary>
        public float SteerLockDeg, SteerRateDps, WheelbaseM, SpanM;
    }
#pragma warning restore CS0649

    /// <summary>Per-airframe constants for guidance and control. Derived from native numbers, then
    /// overridden field by field from shipped, calibrated and user JSON.</summary>
    internal sealed class AirframeProfile
    {
        public string Id = "generic";
        public AirframeClass Class = AirframeClass.FixedWing;
        public float StallSpeed = 55f, CornerSpeed = 170f, MaxSpeed = 300f, MilSpeed = 255f, RefAirspeed = 200f;
        public float CruiseThrottle = 0.6f;
        public float GLimit = 9f, NegativeGLimit = 3f;
        /// <summary>Seed for the in-flight roll authority (<see cref="RateAuthority"/>) and its upper bound: the FBW
        /// commands 0.5·maxRollAngularVel, capped at <see cref="RollSeedCapDps"/>. Each pipeline replaces it with
        /// the learned full-stick roll rate.</summary>
        public float RollRateMaxDps = 120f;
        public static float RollSeedCapDps = 120f;
        public float ClimbRateMax = 80f;
        public float BrakeDecel = 4f, AirbrakeDecel = 7f, ThrustAccelMax = 8f;
        public bool HasAfterburner = true;
        public float AfterburnerThrottle = 0.9f;
        public float MaxRadius = 8f;
        public float TauAlong = 6f, TauCross = 4f, TauVert = 5f, TauVel = 1.5f;
        public float RollGain = 2.5f;
        /// <summary>Formation cruise speed: what a member sustains (rotary 0.8·max, fixed-wing the reference airspeed).</summary>
        public float CruiseSpeed = 200f;
        /// <summary>Rotary: disc tilt limit (deg), collective that hovers, vertical acceleration limit (m/s²), and the
        /// FBW rate authorities that seed the learners (deg/s, native maxAngularVel (1, 2, 2) rad/s). 30° of tilt: drag
        /// at 60 m/s already takes ~15°, and a 20° bank turn another 20°.</summary>
        public float MaxTiltDeg = 30f, HoverCollective = 0.5f, VerticalAccelMax = 3f;
        public float PitchRateMaxDps = 57f, YawRateMaxDps = 115f;
        /// <summary>Tiltwing: plane mode above ConversionHigh, rotary below ConversionLow (1.4 / 1.1 × the plane-mode stall).</summary>
        public float ConversionLow = 50f, ConversionHigh = 65f;
        public static float ConversionLowFactor = 1.1f, ConversionHighFactor = 1.4f;
        public static float RotaryClimbRateMax = 8f;
        /// <summary>Ground: nose-wheel lock (deg, signed) and slew rate, wheelbase and span (m), and the speed the takeoff
        /// roll rotates at (the published takeoff speed, else 1.2 × stall).</summary>
        public float SteerLockDeg = 45f, SteerRateDps = 60f, WheelbaseM = 6f, SpanM = 12f, TakeoffSpeed = 66f;
        public static float WheelbaseMin = 2f;
#pragma warning disable CS0649 // set only from airframes JSON through ApplyOverrides (reflection)
        public bool ForceAutoAimFallback;
#pragma warning restore CS0649

        /// <summary>Loaded minimum speed: 1 g stall speed scaled by √n with a 20% margin. A helicopter or a tiltwing can
        /// hover, so it has none and the loaded-minimum logic (leader slow, speed floors) never fires for it (a tiltwing
        /// converts to rotary flight before its plane-mode stall).</summary>
        public float MinimumSpeed(float loadFactor) => Class != AirframeClass.FixedWing
            ? 0f
            : StallSpeed * 1.2f * (float)Math.Sqrt(Math.Max(1f, loadFactor));

        /// <summary>Load factor the wing can generate at this airspeed (n = 1 at StallSpeed).</summary>
        public float LiftLimitedG(float airspeed)
        {
            float r = airspeed / Math.Max(1f, StallSpeed);
            return r * r;
        }

        public AirframeProfile Clone() => (AirframeProfile)MemberwiseClone();

        public static AirframeProfile Derive(in ProfileInputs n)
        {
            var p = new AirframeProfile
            {
                Id = string.IsNullOrEmpty(n.UnitName) ? "generic" : n.UnitName,
                Class = n.Class,
            };
            if (n.PublishedStallKmh > 0f) p.StallSpeed = n.PublishedStallKmh / 3.6f;
            else if (n.LandingSpeed > 0f) p.StallSpeed = n.LandingSpeed / 1.3f;
            else if (n.TakeoffSpeed > 0f) p.StallSpeed = n.TakeoffSpeed / 1.15f;
            float corner = n.FbwCornerSpeed > 0f ? n.FbwCornerSpeed : n.CornerSpeed;
            if (corner > 0f) p.CornerSpeed = corner;
            if (n.MaxSpeed > 0f)
            {
                p.MaxSpeed = n.MaxSpeed;
                p.MilSpeed = n.MaxSpeed * 0.85f;
                p.ClimbRateMax = n.MaxSpeed * 0.3f;
            }
            p.RefAirspeed = n.PidReferenceAirspeed > 0f ? n.PidReferenceAirspeed : p.CornerSpeed;
            if (n.CruiseThrottle > 0f) p.CruiseThrottle = n.CruiseThrottle;
            float g = n.GLimit > 0f ? n.GLimit : p.GLimit;
            if (n.FbwGLimit > 0f) g = Math.Min(g, n.FbwGLimit);
            p.GLimit = g;
            if (n.FbwMaxRollAngularVel > 0f)
                p.RollRateMaxDps = Math.Min(RollSeedCapDps, 0.5f * n.FbwMaxRollAngularVel * Scalar.Rad2Deg);
            if (n.MaxRadius > 0f) p.MaxRadius = n.MaxRadius;
            p.CruiseSpeed = p.RefAirspeed;
            p.TakeoffSpeed = n.TakeoffSpeed > 0f ? n.TakeoffSpeed : 1.2f * p.StallSpeed;
            if (n.SteerLockDeg != 0f) p.SteerLockDeg = n.SteerLockDeg;
            if (n.SteerRateDps > 0f) p.SteerRateDps = n.SteerRateDps;
            if (n.WheelbaseM > 0f) p.WheelbaseM = Math.Max(WheelbaseMin, n.WheelbaseM);
            if (n.SpanM > 0f) p.SpanM = n.SpanM;
            if (p.Class == AirframeClass.Rotary) DeriveRotary(p, n);
            if (p.Class == AirframeClass.Tiltwing)
            {
                p.ConversionLow = ConversionLowFactor * p.StallSpeed;
                p.ConversionHigh = ConversionHighFactor * p.StallSpeed;
            }
            return p;
        }

        /// <summary>Fraction of the published maximum a helicopter cruises at: the native helicopter autopilot reaches its
        /// cruise collective at half the maximum speed (its collective blends by smoothstep(speed / max) × 2), and the
        /// UH-90 could not hold its height at 0.8·max in game.</summary>
        public static float RotaryCruiseFraction = 0.5f;

        /// <summary>Helicopter numbers: cruise at <see cref="RotaryCruiseFraction"/>·max, no afterburner, a gentle climb
        /// rate, quicker position loops than a jet (it can stop), and the helo FBW's default rate authorities.</summary>
        private static void DeriveRotary(AirframeProfile p, in ProfileInputs n)
        {
            p.CruiseSpeed = RotaryCruiseFraction * p.MaxSpeed;
            p.MilSpeed = p.MaxSpeed;
            p.HasAfterburner = false;
            p.ClimbRateMax = RotaryClimbRateMax;
            p.TauAlong = 4f;
            p.TauCross = 3f;
            p.TauVert = 3f;
            p.TauVel = 1.5f;
            p.RollRateMaxDps = 115f;
            if (n.HeloPitchRate > 0f) p.PitchRateMaxDps = n.HeloPitchRate * Scalar.Rad2Deg;
            if (n.HeloYawRate > 0f) p.YawRateMaxDps = n.HeloYawRate * Scalar.Rad2Deg;
            if (n.HeloRollRate > 0f) p.RollRateMaxDps = n.HeloRollRate * Scalar.Rad2Deg;
            if (n.HeloGLimit > 0f) p.GLimit = n.HeloGLimit;
            if (n.HoverCollective > 0f) p.HoverCollective = n.HoverCollective;
        }

        /// <summary>Overwrite public fields named by the keys (case-insensitive). Returns the keys that
        /// name no field or carry a value of the wrong type, in input order, for a load warning.</summary>
        public List<string> ApplyOverrides(Dictionary<string, object> values)
        {
            var rejected = new List<string>();
            foreach (KeyValuePair<string, object> kv in values)
            {
                FieldInfo field = typeof(AirframeProfile).GetField(kv.Key,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null || !TryConvert(kv.Value, field.FieldType, out object value))
                {
                    rejected.Add(kv.Key);
                    continue;
                }
                field.SetValue(this, value);
            }
            return rejected;
        }

        private static bool TryConvert(object raw, Type type, out object value)
        {
            value = null;
            if (type == typeof(float))
            {
                if (raw is double d) value = (float)d;
                else if (raw is long l) value = (float)l;
            }
            else if (type == typeof(bool) && raw is bool b) value = b;
            else if (type == typeof(string) && raw is string s) value = s;
            else if (type.IsEnum && raw is string e && Enum.TryParse(type, e, true, out object parsed)) value = parsed;
            return value != null;
        }
    }
}
