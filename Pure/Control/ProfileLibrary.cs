using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Airframe profile overrides in layers: shipped (<c>airframes.json</c>), then calibrated, then
    /// user. Each layer maps a unit name (case-insensitive, or <c>"*"</c> for every airframe) to
    /// <see cref="AirframeProfile"/> field overrides. <see cref="Build"/> derives from native numbers, then
    /// applies each layer's <c>"*"</c> entry and its exact entry, in layer order: later layers win.</summary>
    internal sealed class ProfileLibrary
    {
        private readonly List<string> sources = new List<string>();
        private readonly List<Dictionary<string, Dictionary<string, object>>> layers =
            new List<Dictionary<string, Dictionary<string, object>>>();

        /// <summary>Returns load errors; a malformed layer or entry is skipped.</summary>
        public List<string> AddLayer(string source, string json)
        {
            var errors = new List<string>();
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> top))
            {
                errors.Add($"{source}: expected a JSON object of airframes");
                return errors;
            }
            var layer = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kv in top)
            {
                if (kv.Value is Dictionary<string, object> fields) layer[kv.Key] = fields;
                else errors.Add($"{source}: {kv.Key} is not an object");
            }
            sources.Add(source);
            layers.Add(layer);
            return errors;
        }

        public AirframeProfile Build(in ProfileInputs inputs, List<string> rejected)
        {
            AirframeProfile p = AirframeProfile.Derive(inputs);
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].TryGetValue("*", out Dictionary<string, object> all)) Apply(p, all, sources[i], "*", rejected);
                if (!string.IsNullOrEmpty(inputs.UnitName) && inputs.UnitName != "*" &&
                    layers[i].TryGetValue(inputs.UnitName, out Dictionary<string, object> own))
                    Apply(p, own, sources[i], inputs.UnitName, rejected);
            }
            return p;
        }

        private static void Apply(AirframeProfile p, Dictionary<string, object> values, string source, string unit,
            List<string> rejected)
        {
            foreach (string key in p.ApplyOverrides(values)) rejected.Add($"{source}:{unit}.{key}");
        }
    }
}
