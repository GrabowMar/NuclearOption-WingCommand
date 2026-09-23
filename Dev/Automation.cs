using System;
using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>Hooks for unattended in-game tests: nomodkit's <c>nomod sim run</c> calls them by name
    /// (<c>{"op": "call", "method": "WingCommand.Automation.FormOn", "args": {...}}</c>).
    /// <list type="bullet">
    /// <item>Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Numbers may arrive as doubles; a
    /// string arg naming a scenario unit also arrives as the unit itself under <c>&lt;key&gt;Unit</c>.</item>
    /// <item>A failure returns <c>{"ok": false, "error": ...}</c> and changes nothing; a returned <c>ids</c> map
    /// registers the aircraft with the harness for recording.</item>
    /// <item>Dev tooling: nothing here runs unless called, and every call is logged.</item>
    /// </list></summary>
    public static class Automation
    {
        /// <summary>Forms the wing on <c>lead</c> and air-starts <c>count</c> (default 3) wingmen of <c>type</c>
        /// (default: the lead's type) in <c>shape</c> at <c>spacing</c> (Close, Standard, Open or Spread; both default
        /// to the current selection). The wingmen join over the next ticks; read them with <see cref="Members"/>.</summary>
        public static Dictionary<string, object> FormOn(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing?.Selection == null || SpawnService.Instance == null) return Fail("FormOn", "the wing is not active (no mission, or no formations loaded)");
            if (!(Arg(args, "leadUnit") is Aircraft lead) || lead.disabled) return Fail("FormOn", "'lead' does not name a live aircraft");
            string shape = Text(args, "shape");
            if (shape != null && FormationCatalog.Find(WingData.Formations, shape) == null) return Fail("FormOn", $"no formation '{shape}'");
            SpacingPreset spacing = wing.Selection.Spacing;
            string spacingName = Text(args, "spacing");
            if (spacingName != null && !TryPreset(spacingName, out spacing)) return Fail("FormOn", $"no spacing '{spacingName}' (Close, Standard, Open, Spread)");
            AircraftDefinition type = lead.definition;
            string typeName = Text(args, "type");
            if (typeName != null && (type = FindType(typeName)) == null) return Fail("FormOn", $"no aircraft type '{typeName}'");
            int count = Number(args, "count", 3);
            if (count < 1 || count > FormationCatalog.MaxSlots) return Fail("FormOn", $"count must be 1 to {FormationCatalog.MaxSlots}");

            wing.SetAnchor(lead);
            if (shape != null) wing.SetShape(shape);
            wing.SetSpacing(spacing);
            int spawned = SpawnService.Instance.Call(count, type);
            Plugin.Logger.LogInfo($"[Automation] FormOn: {spawned} × {type.unitName} on '{lead.unitName}' in {wing.Selection.Current.Id} {spacing}");
            return spawned > 0 ? Ok("spawned", spawned) : Fail("FormOn", "no wingmen spawned; see the log");
        }

        /// <summary>The wing's members as <c>{"ids": {"w2": aircraft, ...}}</c> (wingman numbers, #2 up), plus how many
        /// air-starts are still waiting to join.</summary>
        public static Dictionary<string, object> Members(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Members", "the wing is not active");
            var ids = new Dictionary<string, object>();
            foreach (WingMember m in wing.Members) ids["w" + m.Number] = m.Aircraft;
            Plugin.Logger.LogInfo($"[Automation] Members: {ids.Count} ({string.Join(", ", ids.Keys)})");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "count", ids.Count }, { "pending", SpawnService.Instance?.Pending ?? 0 }, { "ids", ids },
            };
        }

        /// <summary>Starts a fresh metrics window now.</summary>
        public static Dictionary<string, object> ResetMetrics(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("ResetMetrics", "the wing is not active");
            wing.Metrics.Reset(wing.MissionTime);
            Plugin.Logger.LogInfo($"[Automation] metrics window started at t={wing.MissionTime:0.0}");
            return Ok("time", wing.MissionTime);
        }

        /// <summary>Formation quality since the last <see cref="ResetMetrics"/>. A value never observed (no member
        /// captured, no pair, no sample) is NaN, which the harness writes as null, so a check on it fails as missing
        /// instead of passing on a sentinel.</summary>
        public static Dictionary<string, object> Metrics(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Metrics", "the wing is not active");
            MetricsSnapshot s = wing.Metrics.Snapshot(wing.MissionTime);
            float memberMinutes = Math.Max(1, s.Members) * Math.Max(1f, s.WindowSeconds) / 60f;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "window_s", s.WindowSeconds },
                { "members", s.Members },
                { "captured", s.CapturedMembers },
                { "left", s.Left },
                { "mean_capture_s", Observed(s.MeanCaptureSeconds) },
                { "slot_rms_m", Observed(s.SlotRmsM) },
                { "slot_max_m", Observed(s.SlotMaxM) },
                { "station_fraction", Observed(s.StationFraction) },
                { "min_separation_m", Observed(s.MinSeparationM) },
                { "min_speed_mps", Observed(s.MinSpeedMps) },
                { "gcas", s.Gcas },
                { "collision", s.Collision },
                { "falling_behind", s.FallingBehind },
                { "transitions", s.Transitions },
                { "transitions_per_member_min", s.Transitions / memberMinutes },
            };
            Plugin.Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "[Automation] metrics over {0:0} s: {1}/{2} captured, slot RMS {3:0.0} m (max {4:0}), min separation {5:0.0} m, " +
                "gcas {6}, collision {7}, transitions {8}", s.WindowSeconds, s.CapturedMembers, s.Members, s.SlotRmsM, s.SlotMaxM,
                s.MinSeparationM, s.Gcas, s.Collision, s.Transitions));
            return result;
        }

        /// <summary>Forms the wing on <c>id</c> (a scenario unit), or on the player when no id is given.</summary>
        public static Dictionary<string, object> Anchor(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Anchor", "the wing is not active");
            if (Text(args, "id") == null)
            {
                wing.SetAnchor(null);
                return Ok("anchor", "player");
            }
            if (!(Arg(args, "idUnit") is Aircraft a) || a.disabled) return Fail("Anchor", "'id' does not name a live aircraft");
            wing.SetAnchor(a);
            return Ok("anchor", a.unitName);
        }

        /// <summary>Hands every member to the game's AI and forms on the player again.</summary>
        public static Dictionary<string, object> Dismiss(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Dismiss", "the wing is not active");
            int n = wing.Members.Count;
            wing.Dismiss();
            wing.SetAnchor(null);
            Plugin.Logger.LogInfo($"[Automation] dismissed {n} wingmen");
            return Ok("released", n);
        }

        private static float Observed(float value) => value < 0f ? float.NaN : value;

        private static AircraftDefinition FindType(string name)
        {
            foreach (AircraftDefinition d in Encyclopedia.i.aircraft)
                if (d != null && string.Equals(d.unitName, name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private static bool TryPreset(string name, out SpacingPreset preset)
        {
            foreach (SpacingPreset p in (SpacingPreset[])Enum.GetValues(typeof(SpacingPreset)))
                if (string.Equals(p.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    preset = p;
                    return true;
                }
            preset = SpacingPreset.Standard;
            return false;
        }

        private static object Arg(Dictionary<string, object> args, string key) =>
            args != null && args.TryGetValue(key, out object v) ? v : null;

        private static string Text(Dictionary<string, object> args, string key) =>
            Arg(args, key) is object v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static int Number(Dictionary<string, object> args, string key, int fallback) =>
            Arg(args, key) is object v ? (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)) : fallback;

        private static Dictionary<string, object> Ok(string key, object value) =>
            new Dictionary<string, object> { { "ok", true }, { key, value } };

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            Plugin.Logger.LogWarning($"[Automation] {hook}: {error}");
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
