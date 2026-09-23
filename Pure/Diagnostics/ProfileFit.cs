using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>Fits airframe profile numbers from one step test (60 Hz rows, time from the start of the test):
    /// <list type="bullet">
    /// <item><c>RollRateMaxDps</c>: the peak roll rate during a roll step, divided by the stick fraction.</item>
    /// <item><c>ThrustAccelMax</c>: the mean aerodynamic acceleration along the path at military power (16–20 s,
    /// after spool-up).</item>
    /// <item><c>AirbrakeDecel</c>: the mean deceleration at idle with the airbrake out (22–26 s).</item>
    /// </list>
    /// Along-path acceleration adds g·sinγ back, so a climb does not read as drag. <see cref="Merge"/> writes the
    /// result into the calibrated airframe layer, keeping every other entry.</summary>
    internal static class ProfileFit
    {
        public static Dictionary<string, float> Fit(TelemetryRing rows)
        {
            var result = new Dictionary<string, float>();
            float rollPeak = 0f, rollStick = 0f, thrust = 0f, brake = 0f;
            int thrustN = 0, brakeN = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                TelemetryRow r = rows[i];
                if (Math.Abs(r.Roll) > 0.1f && Math.Abs(r.RollRate) > rollPeak)
                {
                    rollPeak = Math.Abs(r.RollRate);
                    rollStick = Math.Abs(r.Roll);
                }
                float speed = r.Vel.Length;
                float along = r.AccelAlong + (speed > 1f ? Scalar.G * r.Vel.Y / speed : 0f);
                if (r.Time >= 16f && r.Time < 20f)
                {
                    thrust += along;
                    thrustN++;
                }
                else if (r.Time >= 22f && r.Time < 26f)
                {
                    brake += along;
                    brakeN++;
                }
            }
            if (rollPeak > 1f && rollStick > 0.1f) result["RollRateMaxDps"] = rollPeak / rollStick;
            if (thrustN > 0 && thrust / thrustN > 0.1f) result["ThrustAccelMax"] = thrust / thrustN;
            if (brakeN > 0 && brake / brakeN < -0.1f) result["AirbrakeDecel"] = -brake / brakeN;
            return result;
        }

        public static string Merge(string existing, string unit, Dictionary<string, float> values)
        {
            var layer = new SortedDictionary<string, SortedDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            if (existing != null && MiniJson.TryParse(existing, out object root) && root is Dictionary<string, object> top)
            {
                foreach (KeyValuePair<string, object> kv in top)
                {
                    if (!(kv.Value is Dictionary<string, object> fields)) continue;
                    var entry = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, object> f in fields) entry[f.Key] = f.Value;
                    layer[kv.Key] = entry;
                }
            }
            if (!layer.TryGetValue(unit, out SortedDictionary<string, object> own))
                layer[unit] = own = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, float> kv in values) own[kv.Key] = kv.Value;

            var sb = new StringBuilder("{\n");
            bool firstUnit = true;
            foreach (KeyValuePair<string, SortedDictionary<string, object>> u in layer)
            {
                if (!firstUnit) sb.Append(",\n");
                firstUnit = false;
                sb.Append("  \"").Append(MiniJson.Escape(u.Key)).Append("\": {");
                bool first = true;
                foreach (KeyValuePair<string, object> f in u.Value)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append('"').Append(MiniJson.Escape(f.Key)).Append("\": ").Append(Value(f.Value));
                }
                sb.Append('}');
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static string Value(object v)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (v)
            {
                case float f: return f.ToString("R", c);
                case double d: return d.ToString("R", c);
                case long l: return l.ToString(c);
                case bool b: return b ? "true" : "false";
                case string s: return "\"" + MiniJson.Escape(s) + "\"";
                default: return "null";
            }
        }
    }
}
