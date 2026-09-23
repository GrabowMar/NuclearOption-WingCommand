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
#pragma warning disable CS0649 // set only from airframes JSON through ApplyOverrides (reflection)
        public bool ForceAutoAimFallback;
#pragma warning restore CS0649

        /// <summary>Loaded minimum speed: 1 g stall speed scaled by √n with a 20% margin. A helicopter has none, so
        /// the loaded-minimum logic (leader slow, speed floors) never fires for it.</summary>
        public float MinimumSpeed(float loadFactor) => Class == AirframeClass.Rotary
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
            return p;
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
