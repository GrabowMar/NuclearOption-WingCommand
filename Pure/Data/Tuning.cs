using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace WingCommand
{
    /// <summary>Makes every AI parameter tunable from data (<c>.claude/rules/ai-code.md</c>). A parameter is a
    /// writable <c>public static</c> float, int or bool field of one of <see cref="Types"/>, named
    /// <c>"Type.Field"</c> (case-insensitive) in a flat JSON object. The engine applies
    /// <c>tuning.user.json</c> at startup and writes <c>tuning.defaults.json</c> from <see cref="Export"/>.</summary>
    internal static class Tuning
    {
        public static readonly Type[] Types =
        {
            typeof(TrackingGuidance), typeof(AccelMapping), typeof(HoldGuidance), typeof(AutopilotSession),
            typeof(FixedWingController), typeof(AircraftSensorCore), typeof(ConstraintChain), typeof(CollisionBias),
            typeof(TerrainFloor), typeof(LeaderEstimator), typeof(TurnFrame), typeof(SlotSolver),
            typeof(RejoinPlanner), typeof(FormationWing), typeof(HoldOrbit), typeof(PilotMind), typeof(RolePolicy), typeof(AirStart), typeof(RateAuthority), typeof(AirframeProfile),
            typeof(RotaryGuidance), typeof(RotaryController), typeof(RotaryPipeline), typeof(TiltwingPipeline), typeof(AnchorTrail),
            typeof(FaultGuard), typeof(StepSequence), typeof(EscortPick),
            typeof(TaxiGraph), typeof(GroundGuidance), typeof(GroundController), typeof(LineupPlanner), typeof(DepartureSequencer),
            typeof(StuckWatchdog), typeof(GroundPilot), typeof(FieldTraffic), typeof(ServiceSpots), typeof(RecoveryPilot), typeof(PilotSkill), typeof(RefitTimer), typeof(BingoMonitor), typeof(TaskLead),
        };

        /// <summary>Returns the keys it could not apply, in input order.</summary>
        public static List<string> Apply(string json, Type[] types)
        {
            var rejected = new List<string>();
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> values))
            {
                rejected.Add("(not a JSON object)");
                return rejected;
            }
            foreach (KeyValuePair<string, object> kv in values)
            {
                FieldInfo field = Find(kv.Key, types);
                if (field == null || !TryConvert(kv.Value, field.FieldType, out object value))
                {
                    rejected.Add(kv.Key);
                    continue;
                }
                field.SetValue(null, value);
            }
            return rejected;
        }

        public static string Export(Type[] types)
        {
            var lines = new List<string>();
            foreach (Type t in types)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (Tunable(f)) lines.Add($"  \"{t.Name}.{f.Name}\": {Format(f.GetValue(null))}");
            lines.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("{\n");
            sb.Append(string.Join(",\n", lines));
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static FieldInfo Find(string key, Type[] types)
        {
            int dot = key.LastIndexOf('.');
            if (dot <= 0 || dot == key.Length - 1) return null;
            string typeName = key.Substring(0, dot), fieldName = key.Substring(dot + 1);
            foreach (Type t in types)
            {
                if (!string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)) continue;
                FieldInfo f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                return f != null && Tunable(f) ? f : null;
            }
            return null;
        }

        private static bool Tunable(FieldInfo f) =>
            !f.IsLiteral && !f.IsInitOnly &&
            (f.FieldType == typeof(float) || f.FieldType == typeof(int) || f.FieldType == typeof(bool));

        private static bool TryConvert(object raw, Type type, out object value)
        {
            value = null;
            if (type == typeof(float))
            {
                if (raw is double d) value = (float)d;
                else if (raw is long l) value = (float)l;
            }
            else if (type == typeof(int) && raw is long i && i >= int.MinValue && i <= int.MaxValue) value = (int)i;
            else if (type == typeof(bool) && raw is bool b) value = b;
            return value != null;
        }

        private static string Format(object v)
        {
            if (v is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (v is int i) return i.ToString(CultureInfo.InvariantCulture);
            return (bool)v ? "true" : "false";
        }
    }
}
