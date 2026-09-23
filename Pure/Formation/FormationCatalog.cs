using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Parses, validates and merges formation definitions. Built-ins ship as
    /// <c>Assets/Data/formations.json</c>; user files use the same schema and replace built-ins by id.
    /// Validation is the load-time SelfCheck: same-stack aircraft keep 0.6 spacing at the tightest spacing,
    /// level and in an 85° bank either way, uncompressed and fully compressed.</summary>
    internal static class FormationCatalog
    {
        public const float StackMetres = 10f;
        public const float CompressMin = 0.65f;
        public const float MinSeparation = 0.6f;
        public const int MaxSlots = 7;
        public const float Close = 40f, Standard = 80f, Open = 160f, Spread = 350f;

        private static readonly float[] CheckBanks = { 0f, 85f, -85f };
        private static readonly float[] CheckCompressions = { 1f, CompressMin };

        public static List<FormationDefinition> Parse(string json, List<string> errors)
        {
            var result = new List<FormationDefinition>();
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> top) ||
                !MiniJson.TryGetList(top, "formations", out List<object> list))
            {
                errors.Add("formations: expected a JSON object with a \"formations\" list");
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i] as Dictionary<string, object>;
                string id = entry != null ? MiniJson.GetString(entry, "id") ?? "?" : "?";
                string reason = "not an object";
                if (entry != null && TryRead(entry, out FormationDefinition d, out reason) && Validate(d, out reason))
                    result.Add(d);
                else
                    errors.Add($"formations[{i}] ({id}): {reason}");
            }
            return result;
        }

        public static bool Validate(FormationDefinition d, out string reason)
        {
            int n = d.Slots.Length;
            if (n < 1 || n > MaxSlots)
            {
                reason = $"needs 1 to {MaxSlots} slots, has {n}";
                return false;
            }
            if (!(d.SpacingMin > 0f && d.SpacingMin <= d.SpacingDefault && d.SpacingDefault <= d.SpacingMax))
            {
                reason = "spacing must satisfy 0 < min <= default <= max";
                return false;
            }
            for (int i = 0; i < n; i++)
            {
                SlotDef s = d.Slots[i];
                if (!Scalar.IsFinite(s.Right) || !Scalar.IsFinite(s.Aft) || !Scalar.IsFinite(s.Up) ||
                    Math.Abs(s.Right) > 10f || Math.Abs(s.Aft) > 10f || Math.Abs(s.Up) > 20f || s.RollFollow > 1f)
                {
                    reason = $"slot {i + 1} is out of range (|right|, |aft| <= 10, |up| <= 20, rollFollow <= 1)";
                    return false;
                }
            }
            float spacing = d.SpacingMin, limit = MinSeparation * spacing - 1e-3f;
            var velocity = new Vec3(0f, 0f, 100f);
            foreach (float k in CheckCompressions)
                foreach (float bank in CheckBanks)
                    for (int a = -1; a < n; a++)
                        for (int b = a + 1; b < n; b++)
                        {
                            float upA = a < 0 ? 0f : d.Slots[a].Up, upB = d.Slots[b].Up;
                            if (Math.Abs(upA - upB) >= 1f) continue;
                            Vec3 pa = a < 0 ? Vec3.Zero : Position(d.Slots[a], spacing, k, bank, velocity);
                            Vec3 pb = Position(d.Slots[b], spacing, k, bank, velocity);
                            if ((pa - pb).Length < limit)
                            {
                                reason = $"aircraft {a + 1} and {b + 1} closer than {MinSeparation} spacing " +
                                         $"(compression {k}, bank {bank})";
                                return false;
                            }
                        }
            reason = null;
            return true;
        }

        public static List<FormationDefinition> Merge(List<FormationDefinition> builtIns, List<FormationDefinition> user)
        {
            var result = new List<FormationDefinition>(builtIns);
            foreach (FormationDefinition u in user)
            {
                int i = result.FindIndex(d => string.Equals(d.Id, u.Id, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) result[i] = u;
                else result.Add(u);
            }
            return result;
        }

        public static FormationDefinition Find(List<FormationDefinition> all, string id) =>
            all.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        private static Vec3 Position(SlotDef slot, float spacing, float compression, float bank, Vec3 velocity)
        {
            float right = slot.Right * spacing * compression;
            float w = TurnFrame.RollFollowWeight(TurnFrame.Reach(right, slot.Aft * spacing, slot.Up * StackMetres), slot.RollFollow);
            return TurnFrame.Offset(velocity, Vec3.Forward, bank, right, slot.Aft * spacing, slot.Up * StackMetres, w);
        }

        private static bool TryRead(Dictionary<string, object> e, out FormationDefinition def, out string reason)
        {
            def = null;
            string id = MiniJson.GetString(e, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                reason = "missing id";
                return false;
            }
            if (!MiniJson.TryGetList(e, "slots", out List<object> slots) || slots.Count == 0)
            {
                reason = "missing slots";
                return false;
            }
            var d = new FormationDefinition
            {
                Id = id,
                Family = MiniJson.GetString(e, "family") ?? "classic",
                Name = MiniJson.GetString(e, "name") ?? id,
            };
            if (e.TryGetValue("spacing", out object spacingValue) && spacingValue is Dictionary<string, object> spacing &&
                !(Number(spacing, "min", 40f, out d.SpacingMin) && Number(spacing, "max", 160f, out d.SpacingMax) &&
                  Number(spacing, "default", 80f, out d.SpacingDefault)))
            {
                reason = "spacing values must be numbers";
                return false;
            }
            d.Slots = new SlotDef[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                if (!(slots[i] is Dictionary<string, object> s) ||
                    !Number(s, "right", float.NaN, out float right) || !Number(s, "aft", float.NaN, out float aft) ||
                    !Number(s, "up", 0f, out float up) || !Number(s, "rollFollow", -1f, out float roll) ||
                    float.IsNaN(right) || float.IsNaN(aft))
                {
                    reason = $"slot {i + 1} needs numeric right and aft";
                    return false;
                }
                d.Slots[i] = new SlotDef(right, aft, up, roll);
            }
            if (MiniJson.TryGetList(e, "modifiers", out List<object> modifiers))
                foreach (object m in modifiers)
                    switch (m as string)
                    {
                        case "turnCompress": d.Modifiers |= FormationModifiers.TurnCompress; break;
                        case "crossover": d.Modifiers |= FormationModifiers.Crossover; break;
                        case "terrainFlatten": d.Modifiers |= FormationModifiers.TerrainFlatten; break;
                        default:
                            reason = $"unknown modifier {m}";
                            return false;
                    }
            if (!ReadElements(e, d, out reason)) return false;
            if (!ReadUse(e, d, out reason)) return false;
            def = d;
            return true;
        }

        /// <summary>The shape's <c>"for"</c> list (jet, rotary, escort), or its family's default.</summary>
        private static bool ReadUse(Dictionary<string, object> e, FormationDefinition d, out string reason)
        {
            reason = null;
            if (!MiniJson.TryGetList(e, "for", out List<object> uses))
            {
                d.Use = DefaultUse(d.Family);
                return true;
            }
            d.Use = FormationUse.None;
            foreach (object u in uses)
                switch (u as string)
                {
                    case "jet": d.Use |= FormationUse.Jet; break;
                    case "rotary": d.Use |= FormationUse.Rotary; break;
                    case "escort": d.Use |= FormationUse.Escort; break;
                    default:
                        reason = $"unknown \"for\" value {u} (jet, rotary, escort)";
                        return false;
                }
            return true;
        }

        private static FormationUse DefaultUse(string family)
        {
            switch (family)
            {
                case "classic": return FormationUse.Jet | FormationUse.Rotary;
                case "rotary": return FormationUse.Rotary;
                case "escort": return FormationUse.Escort;
                default: return FormationUse.Jet;
            }
        }

        private static bool ReadElements(Dictionary<string, object> e, FormationDefinition d, out string reason)
        {
            reason = null;
            int n = d.Slots.Length;
            d.Element = new int[n];
            if (!MiniJson.TryGetList(e, "elements", out List<object> elements))
            {
                for (int i = 0; i < n; i++) d.Element[i] = (i + 1) / 2;
                return true;
            }
            for (int i = 0; i < n; i++) d.Element[i] = -1;
            for (int k = 0; k < elements.Count; k++)
            {
                if (!(elements[k] is List<object> members))
                {
                    reason = "elements must be lists of aircraft numbers";
                    return false;
                }
                foreach (object m in members)
                {
                    int aircraft = m is long l ? (int)l : m is double x ? (int)x : -1;
                    if (aircraft == 0) continue;
                    if (aircraft < 1 || aircraft > n || d.Element[aircraft - 1] >= 0)
                    {
                        reason = $"element {k}: aircraft {m} is out of range or repeated";
                        return false;
                    }
                    d.Element[aircraft - 1] = k;
                }
            }
            for (int i = 0; i < n; i++)
                if (d.Element[i] < 0)
                {
                    reason = $"aircraft {i + 1} is in no element";
                    return false;
                }
            return true;
        }

        private static bool Number(Dictionary<string, object> d, string key, float fallback, out float value)
        {
            value = fallback;
            if (!d.TryGetValue(key, out object raw)) return true;
            switch (raw)
            {
                case long l: value = l; return true;
                case double x: value = (float)x; return true;
                default: return false;
            }
        }
    }
}
