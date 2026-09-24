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

        /// <summary>Launches wingmen from a field (M3): <c>lead</c> (the anchor), <c>field</c> (an airbase name, or
        /// <c>nearest</c> to the lead), <c>count</c>, and optionally <c>type</c>, <c>shape</c>, <c>spacing</c>.</summary>
        public static Dictionary<string, object> Launch(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing?.Selection == null || SpawnService.Instance == null) return Fail("Launch", "the wing is not active (no mission, or no formations loaded)");
            if (!(Arg(args, "leadUnit") is Aircraft lead) || lead.disabled) return Fail("Launch", "'lead' does not name a live aircraft");
            AircraftDefinition type = lead.definition;
            string typeName = Text(args, "type");
            if (typeName != null && (type = FindType(typeName)) == null) return Fail("Launch", $"no aircraft type '{typeName}'");
            int count = Number(args, "count", 2);
            if (count < 1 || count > FormationCatalog.MaxSlots) return Fail("Launch", $"count must be 1 to {FormationCatalog.MaxSlots}");
            string fieldName = Text(args, "field") ?? "nearest";
            List<Airbase> fields = WingService.FriendlyFields(lead);
            Airbase field = fieldName == "nearest" ? wing.FieldFor(lead, type)
                : fields.Find(a => string.Equals(a.name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null) return Fail("Launch", $"no friendly field '{fieldName}'; friendly: {string.Join(", ", fields.ConvertAll(a => a.name))}");
            string shape = Text(args, "shape");
            if (shape != null && FormationCatalog.Find(WingData.Formations, shape) == null) return Fail("Launch", $"no formation '{shape}'");
            wing.SetAnchor(lead);
            if (shape != null) wing.SetShape(shape);
            string spacingName = Text(args, "spacing");
            if (spacingName != null)
            {
                if (!TryPreset(spacingName, out SpacingPreset spacing)) return Fail("Launch", $"no spacing '{spacingName}'");
                wing.SetSpacing(spacing);
            }
            // Scenarios test flight and ground behaviour: calls are free unless the scenario asks otherwise.
            bool sandbox = !args.TryGetValue("sandbox", out object free) || !(free is bool b) || b;
            int launched = SpawnService.Instance.LaunchFromField(field, type, count, sandbox);
            Plugin.Logger.LogInfo($"[Automation] Launch: {launched} × {type.unitName} from {field.name}");
            return launched > 0 ? Ok("launched", launched) : Fail("Launch", "nothing launched; see the log");
        }

        /// <summary>Ground operations so far: members still on the ground and airborne, and the ground events
        /// (relocations, reroutes, native ejections blocked).</summary>
        public static Dictionary<string, object> Ground(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Ground", "the wing is not active");
            // flying: really up (radar altitude above 30 m), not just off our ground phases (a jet left in the grass
            // after its roll once counted as airborne).
            int grounded = 0, airborne = 0, flying = 0;
            foreach (WingMember m in wing.Members)
            {
                if (m.OnGround) grounded++;
                else airborne++;
                if (!m.OnGround && m.Aircraft != null && m.Aircraft.radarAlt > 30f) flying++;
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "grounded", grounded }, { "airborne", airborne }, { "flying", flying },
                { "relocated", wing.Events.CountOf(WingEventKind.Relocated) },
                { "rerouted", wing.Events.CountOf(WingEventKind.Rerouted) },
                { "rolled", wing.Events.CountOf(WingEventKind.Rolling) },
                { "aborted", wing.Events.CountOf(WingEventKind.DepartureAborted) },
                { "landed", wing.Events.CountOf(WingEventKind.Landed) },
                { "landing_failed", wing.Events.CountOf(WingEventKind.LandingFailed) },
                { "parked", wing.Events.CountOf(WingEventKind.Parked) },
                { "reserved", wing.Events.CountOf(WingEventKind.Reserved) },
                { "serviced", wing.Events.CountOf(WingEventKind.Serviced) },
                { "native_switches_redirected", SwitchStateGuard.Redirected },
                { "bounces_held", BounceGuard.Held },
                { "ejections_blocked", EjectGuard.Blocked },
            };
            var members = new List<object>();
            foreach (WingMember m in wing.Members)
                members.Add(new Dictionary<string, object>
                {
                    { "number", m.Number },
                    { "phase", m.Ground != null ? m.Ground.Phase.ToString() : "air" },
                    { "stop", m.Ground != null ? m.Ground.Stop.ToString() : "" },
                    { "recovery", m.Recovery != null ? m.Recovery.Phase.ToString() : "" },
                    { "x", m.Last.Pos.X }, { "z", m.Last.Pos.Z }, { "alt", m.Last.RadarAlt },
                });
            result["members"] = members;
            Plugin.Logger.LogInfo($"[Automation] Ground: {grounded} on the ground, {airborne} airborne, " +
                                  $"{result["relocated"]} relocated, {result["rerouted"]} rerouted, {EjectGuard.Blocked} ejections blocked");
            return result;
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
                { "ai_ms_mean", wing.AiMsMean }, { "ai_ms_max", wing.AiMsMax },
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

        /// <summary>Escorts <c>id</c> (any scenario unit: aircraft, vehicle or ship) in the escort shapes; no id ends the
        /// escort.</summary>
        public static Dictionary<string, object> Escort(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Escort", "the wing is not active");
            if (Text(args, "id") == null)
            {
                wing.SetEscort(null);
                return Ok("escort", "none");
            }
            if (!(Arg(args, "idUnit") is Unit u) || u.disabled) return Fail("Escort", "'id' does not name a live unit");
            wing.SetEscort(u);
            return Ok("escort", u.unitName);
        }

        /// <summary>Writes every field in use as the mod reads it (the nomodkit dump format) to <c>path</c> (default:
        /// <c>%TEMP%\wingcommand-fields.json</c>), for replay in the FlightSim.</summary>
        public static Dictionary<string, object> DumpFields(Dictionary<string, object> args)
        {
            string path = Text(args, "path") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wingcommand-fields.json");
            List<AirbaseSample> samples = FieldRegistry.Samples();
            FieldRegistry.LogStates();
            System.IO.File.WriteAllText(path, AirbaseSample.ToDumpJson(MissionManager.CurrentMission?.Name, samples));
            Plugin.Logger.LogInfo($"[Automation] DumpFields: {samples.Count} fields to {path}");
            return Ok("path", path);
        }

        /// <summary>Spike S6 (carriers): any airbase by name (args.field), read as the ground code would (roads, hangars, runways,
        /// pads) and dumped in the fixture format (args.path, default %TEMP%/wingcommand-airbase-NAME.json), with what a deck
        /// adds: attached or not, the runways' and hangars' velocities, elevator hangars, arrestor and ski-jump runways.</summary>
        public static Dictionary<string, object> DumpAirbase(Dictionary<string, object> args)
        {
            string name = Text(args, "field");
            Airbase airbase = null;
            foreach (Airbase b in UnityEngine.Object.FindObjectsOfType<Airbase>())
                if (b != null && string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase)) airbase = b;
            if (airbase == null) return Fail("DumpAirbase", $"no airbase '{name}'");
            AirbaseSample sample = AirbaseAdapter.Read(airbase);
            string path = Text(args, "path") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wingcommand-airbase-" + airbase.name + ".json");
            System.IO.File.WriteAllText(path, AirbaseSample.ToDumpJson(MissionManager.CurrentMission?.Name, new[] { sample }));
            float runwaySpeed = 0f, hangarSpeed = 0f;
            int arrestor = 0, skiJump = 0, doors = 0;
            if (airbase.runways != null)
                foreach (Airbase.Runway r in airbase.runways)
                {
                    if (r == null) continue;
                    runwaySpeed = Math.Max(runwaySpeed, r.GetVelocity().magnitude);
                    if (r.Arrestor) arrestor++;
                    if (r.SkiJump) skiJump++;
                }
            if (airbase.hangars != null)
                foreach (Hangar h in airbase.hangars)
                    if (h != null) hangarSpeed = Math.Max(hangarSpeed, h.GetVelocity().magnitude);
            foreach (HangarSample h in sample.Hangars)
                if (h.CarrierDoors) doors++;
            string unit = airbase.TryGetAttachedUnit(out Unit attached) && attached != null ? attached.unitName : "";
            Plugin.Logger.LogInfo($"[Automation] DumpAirbase {airbase.name}: attached {sample.Attached} ({unit}), {sample.Roads.Length} roads, " +
                                  $"{sample.Hangars.Length} hangars ({doors} elevator), {sample.Runways.Length} runways ({arrestor} arrestor, " +
                                  $"{skiJump} ski-jump), {sample.ServicePoints.Length} service points, {sample.Pads.Length} pads, " +
                                  $"deck speed {runwaySpeed:0.0}/{hangarSpeed:0.0} m/s → {path}");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "path", path }, { "attached", sample.Attached }, { "roads", sample.Roads.Length },
                { "hangars", sample.Hangars.Length }, { "elevators", doors }, { "runways", sample.Runways.Length },
                { "arrestor", arrestor }, { "ski_jump", skiJump }, { "service_points", sample.ServicePoints.Length },
                { "pads", sample.Pads.Length }, { "deck_speed", runwaySpeed },
            };
        }

        /// <summary>Gives the wing a task (spec M4): kind Move|Route|Patrol|Orbit|Hold|Form; offsets [[forward, right], …]
        /// metres from the lead; alt (world Y), speed, seconds, loop, left optional.</summary>
        public static Dictionary<string, object> Task(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Task", "the wing is not active");
            WingTask task = BuildTask(args, wing, "kind", out string error);
            if (task == null) return Fail("Task", error);
            OrderResult r = wing.Order(task);
            return new Dictionary<string, object> { { "ok", true }, { "accepted", r.Accepted }, { "reason", r.Reason ?? "" } };
        }

        /// <summary>A task from hook arguments: <paramref name="kindKey"/> names the TaskKind; offsets, alt, speed, seconds,
        /// loop and left as <see cref="Task"/> reads them. Null with the error.</summary>
        private static WingTask BuildTask(Dictionary<string, object> args, WingService wing, string kindKey, out string error)
        {
            error = null;
            // The wing's anchor (the harness spawns its lead without a player, review M4a I1), else the player.
            if (!Enum.TryParse(Text(args, kindKey) ?? "", true, out TaskKind kind))
            {
                error = "unknown task kind";
                return null;
            }
            var points = new List<Waypoint>();
            if (args.TryGetValue("offsets", out object raw) && raw is List<object> list)
            {
                // Only points need a lead (a Form order has none; during a task the leader is the virtual anchor).
                Unit lead = Arg(args, "leadUnit") as Unit ?? wing.LeaderUnit ?? wing.Player;
                if (lead == null)
                {
                    error = "no lead to place the points from";
                    return null;
                }
                Vec3 at = lead.GlobalPosition().ToVec3();
                Vec3 f = lead.transform.forward.ToVec3().Horizontal.Normalized;
                foreach (object o in list)
                    if (o is List<object> pair && pair.Count == 2)
                    {
                        float forward = Convert.ToSingle(pair[0], CultureInfo.InvariantCulture);
                        float right = Convert.ToSingle(pair[1], CultureInfo.InvariantCulture);
                        Vec3 p = at + f * forward + new Vec3(f.Z, 0f, -f.X) * right;
                        points.Add(Waypoint.At(p.X, p.Z));
                    }
            }
            var task = new WingTask { Kind = kind, Points = points.ToArray() };
            if (args.TryGetValue("alt", out object alt)) task.Altitude = Convert.ToSingle(alt, CultureInfo.InvariantCulture);
            if (args.TryGetValue("speed", out object speed)) task.Speed = Convert.ToSingle(speed, CultureInfo.InvariantCulture);
            if (args.TryGetValue("seconds", out object seconds)) task.Seconds = Convert.ToSingle(seconds, CultureInfo.InvariantCulture);
            task.Loop = args.TryGetValue("loop", out object loop) && loop is bool l && l;
            task.Left = args.TryGetValue("left", out object left) && left is bool lf && lf;
            return task;
        }

        /// <summary>Spec WMC program §3.2: one order through the executor. kind: an OrderKind name (Task takes the Task hook's
        /// arguments with its kind under "task"); scope: element (a letter) or members (their #numbers); text, number and
        /// flag as the order carries them.</summary>
        public static Dictionary<string, object> Order(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Order", "the wing is not active");
            if (!Enum.TryParse(Text(args, "kind") ?? "", true, out OrderKind kind)) return Fail("Order", "unknown kind");
            var o = new WingOrder
            {
                Kind = kind, Text = Text(args, "text"), Number = Number(args, "number", 0),
                Flag = Arg(args, "flag") is bool flag && flag,
            };
            string letter = Text(args, "element");
            if (!string.IsNullOrEmpty(letter)) o.Scope = WingScope.OfElement(char.ToUpperInvariant(letter[0]) - 'A');
            else if (Arg(args, "members") is List<object> numbers)
            {
                var ids = new List<uint>();
                foreach (object n in numbers)
                {
                    int number = (int)Math.Round(Convert.ToDouble(n, CultureInfo.InvariantCulture));
                    foreach (WingMember m in wing.Members)
                        if (m.Number == number && (object)m.Aircraft != null) ids.Add(m.Aircraft.persistentID.Id);
                }
                o.Scope = WingScope.OfMembers(ids.ToArray());
            }
            if (kind == OrderKind.Task)
            {
                o.Task = BuildTask(args, wing, "task", out string error);
                if (o.Task == null) return Fail("Order", error);
            }
            OrderResult r = OrderExecutor.Execute(o);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "accepted", r.Accepted }, { "ack", r.Ack ?? "" }, { "reason", r.Reason ?? "" }, { "element", r.Element },
            };
        }

        /// <summary>Spec WMC program §3.3: elements in use and their member counts (a, b, c, d).</summary>
        public static Dictionary<string, object> Elements(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Elements", "the wing is not active");
            var result = new Dictionary<string, object> { { "ok", true } };
            int inUse = 0;
            for (int e = 0; e < ElementRoster.MaxElements; e++)
            {
                bool used = wing.Roster.InUse(e);
                if (used) inUse++;
                string key = ElementRoster.Letter(e).ToLowerInvariant();
                result[key] = used ? wing.Roster.Count(e) : 0;
                WingPlanner planner = wing.PlannerOf(e);
                result[key + "_task"] = used && planner.Active ? planner.Current.Kind.ToString() : "Form";
            }
            result["in_use"] = inUse;
            return result;
        }

        /// <summary>The wing's task now and how many task events it logged.</summary>
        public static Dictionary<string, object> TaskState(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("TaskState", "the wing is not active");
            WingPlanner p = wing.Planner;
            return new Dictionary<string, object>
            {
                { "ok", true }, { "active", p.Active }, { "kind", p.Active ? (int)p.Current.Kind : 0 }, { "leg", p.Leg },
                { "started", wing.Events.CountOf(WingEventKind.TaskStarted) },
                { "completed", wing.Events.CountOf(WingEventKind.TaskCompleted) },
                { "cancelled", wing.Events.CountOf(WingEventKind.TaskCancelled) },
                { "failed", wing.Events.CountOf(WingEventKind.TaskFailed) },
            };
        }

        /// <summary>Test fixture for economy scenarios (Free Flight gives no allocation): adds <c>allocation</c> to the
        /// player's allocation and <c>stock</c> airframes of <c>type</c> to the lead's faction.</summary>
        public static Dictionary<string, object> Grant(Dictionary<string, object> args)
        {
            GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player);
            float add = args.TryGetValue("allocation", out object raw) ? Convert.ToSingle(raw, CultureInfo.InvariantCulture) : 0f;
            if (player != null && add != 0f) player.AddAllocation(add);
            Aircraft lead = Arg(args, "leadUnit") as Aircraft ?? WingService.Instance?.Player;
            string typeName = Text(args, "type");
            AircraftDefinition type = typeName != null ? FindType(typeName) : null;
            int stock = Number(args, "stock", 0);
            if (type != null && stock != 0 && lead != null && lead.NetworkHQ != null) lead.NetworkHQ.ModifyUnitSupply(type, stock);
            Plugin.Logger.LogInfo($"[Automation] Grant: allocation +{add:0}, {stock} × {typeName ?? "-"}");
            return Ok("allocation", player != null ? player.Allocation : -1f);
        }

        /// <summary>Every member flying with the wing engages (args.target: a registered unit id to attack).</summary>
        public static Dictionary<string, object> Engage(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Engage", "the wing is not active");
            // target, target2, target3...: registered unit ids (the harness passes each as <key>Unit), split across the wing.
            var targets = new List<Unit>();
            for (int i = 1; i <= TargetAllocator.MaxTargets; i++)
                if (Arg(args, i == 1 ? "targetUnit" : "target" + i + "Unit") is Unit u) targets.Add(u);
            int n = targets.Count > 0 ? wing.Attack(targets) : wing.Engage();
            Plugin.Logger.LogInfo($"[Automation] Engage: {n} engaged on {targets.Count} target(s)");
            return n > 0 ? Ok("engaged", n) : Fail("Engage", "nobody could engage");
        }

        /// <summary>The radial Engage: refused while outnumbered unless pressed again within the window (spec M5 §6.3).</summary>
        public static Dictionary<string, object> EngageCommand(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("EngageCommand", "the wing is not active");
            bool allowed = wing.MayEngage(out int hostiles, out int members);
            int n = allowed ? wing.Engage() : 0;
            Plugin.Logger.LogInfo($"[Automation] EngageCommand: {(allowed ? n + " engaged" : "refused")} ({hostiles} hostiles, {members} members)");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "refused", allowed ? 0 : 1 }, { "hostiles", hostiles }, { "members", members }, { "engaged", n },
            };
        }

        /// <summary>The transport's handshake counts (spec M6 §7; spike S1 in single-player: the host greets its own client).</summary>
        public static Dictionary<string, object> NetState(Dictionary<string, object> args) => new Dictionary<string, object>
        {
            { "ok", true }, { "disabled", WingNet.Disabled ? 1 : 0 }, { "greeted", WingNet.Greeted }, { "replies", WingNet.Replies },
            { "host_greeted", WingNet.HostGreeted ? 1 : 0 }, { "in", WingNet.In }, { "out", WingNet.Out },
            { "decode_failures", WingNet.DecodeFailures }, { "snapshots_in", WingNet.SnapshotsIn },
            { "mirror_members", WingNet.Mirror != null ? WingNet.Mirror.Count : -1 },
            { "mirror_stale", WingNet.Mirror == null || WingNet.Mirror.Stale(UnityEngine.Time.unscaledTime) ? 1 : 0 },
            { "wing_members", WingService.Instance != null ? WingService.Instance.Members.Count : -1 },
        };

        /// <summary>The wing's helicopters land around the anchor (spec M4 §5).</summary>
        public static Dictionary<string, object> LandHere(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("LandHere", "the wing is not active");
            int n = wing.LandHere(out string refusal);
            return new Dictionary<string, object> { { "ok", true }, { "landing", n }, { "refusal", refusal ?? "" } };
        }

        /// <summary>Spec M4 §7.2: Deliver Cargo as the radial entry does.</summary>
        public static Dictionary<string, object> DeliverCargo(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("DeliverCargo", "the wing is not active");
            int n = wing.DeliverCargo(out string refusal);
            Plugin.Logger.LogInfo($"[Automation] DeliverCargo: {n} landing{(refusal != null ? " (" + refusal + ")" : "")}");
            return new Dictionary<string, object> { { "ok", true }, { "landing", n }, { "refusal", refusal ?? "" } };
        }

        /// <summary>Spec M4 §7.1: Rescue as the radial entry does.</summary>
        public static Dictionary<string, object> Rescue(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Rescue", "the wing is not active");
            WingMember sent = wing.Rescue(out string result);
            Plugin.Logger.LogInfo($"[Automation] Rescue: {result}");
            return new Dictionary<string, object> { { "ok", true }, { "sent", sent != null ? 1 : 0 }, { "result", result } };
        }

        public static Dictionary<string, object> TakeOff(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("TakeOff", "the wing is not active");
            return Ok("lifting", wing.TakeOff());
        }

        /// <summary>Settled members per phase, and landings/lift-offs logged.</summary>
        public static Dictionary<string, object> Settled(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Settled", "the wing is not active");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "approach", wing.Settled(SettlePhase.Approach) }, { "descend", wing.Settled(SettlePhase.Descend) },
                { "down", wing.Settled(SettlePhase.Down) }, { "lifting", wing.Settled(SettlePhase.LiftOff) },
                { "landed", wing.Events.CountOf(WingEventKind.Landed) }, { "airborne", wing.Events.CountOf(WingEventKind.Airborne) },
                { "failed", wing.Events.CountOf(WingEventKind.LandingFailed) }, { "members", wing.Members.Count },
                { "cargo_deployed", wing.CargoDeployed },
            };
        }

        /// <summary>Sets the wing's doctrine by name (Reserve, Escort, Sweep or a custom line).</summary>
        public static Dictionary<string, object> Doctrine(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Doctrine", "the wing is not active");
            if (!WingDoctrine.TryParse(Text(args, "name") ?? "", out WingDoctrine d)) return Fail("Doctrine", "unknown doctrine");
            wing.SetDoctrine(d);
            return Ok("doctrine", d.PatternName);
        }

        /// <summary>Spec WMC rebuild: open the panel (<c>open</c>: maximize the map and select WMC), latch a tab (<c>tab</c> 0
        /// TACTICAL … 3 WING) and a TACTICAL sub-page (<c>sub</c> 0 ORDERS, 1 FORMATION, 2 ROUTE), and/or press a control by id
        /// (<c>press</c>; a TACTICAL id shows its tab and sub-page first), then report the panel's state — including
        /// <c>overflow</c> (labels that would spill out of their box: the no-"…" check), the alerts shown and the order-grid
        /// cells that cannot be pressed now.</summary>
        public static Dictionary<string, object> Wmc(Dictionary<string, object> args)
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return Fail("Wmc", "no WMC panel");
            if (args != null && args.TryGetValue("open", out object open) && open is bool o && o) panel.Open();
            if (args != null && args.TryGetValue("tab", out object tab) && tab != null)
                panel.Show(Convert.ToInt32(tab, CultureInfo.InvariantCulture));
            if (Arg(args, "sub") != null && panel.Tactical != null)
                panel.Tactical.ShowSubFor(new[] { "tac.orders.", "tac.form.", "tac.route." }[Math.Max(0, Math.Min(2, (int)Number(args, "sub", 0)))]);
            // Spec WMC program §4: the scope — clear, an element by letter, or wingmen by their #numbers.
            WmcSelection selection = panel.Context.Selection;
            if (Arg(args, "clear") is bool clear && clear) selection.Clear();
            string letter = Text(args, "element");
            if (!string.IsNullOrEmpty(letter) && WingService.Instance != null)
            {
                int e = char.ToUpperInvariant(letter[0]) - 'A';
                var ids = new List<uint>();
                foreach (WingMember m in WingService.Instance.Members)
                    if (WingService.Instance.ElementOf(m) == e && (object)m.Aircraft != null) ids.Add(m.Aircraft.persistentID.Id);
                if (ids.Count > 0) selection.SelectElement(e, ids);
            }
            if (Arg(args, "select") is List<object> numbers && WingService.Instance != null)
                foreach (object n in numbers)
                {
                    int number = (int)Math.Round(Convert.ToDouble(n, CultureInfo.InvariantCulture));
                    foreach (WingMember m in WingService.Instance.Members)
                        if (m.Number == number && (object)m.Aircraft != null) selection.Toggle(m.Aircraft.persistentID.Id);
                }
            // Refresh now so a press in the same call acts on this scope.
            panel.Refresh();
            string press = Text(args, "press");
            bool pressed = !string.IsNullOrEmpty(press) && panel.Press(press);
            return new Dictionary<string, object>
            {
                { "ok", string.IsNullOrEmpty(press) || pressed }, { "visible", panel.Visible }, { "tab", panel.Page },
                { "sub", panel.Sub }, { "pressed", pressed }, { "members", panel.Context.Count }, { "controls", panel.Controls.Count },
                { "scope", panel.Context.Selection.Label(panel.Context.Rows, panel.Context.Count) },
                { "overflow", panel.Overflow }, { "alerts", panel.Tactical?.AlertsShown ?? 0 },
                { "armed", panel.Context.Map.Mode != MapMode.Off ? 1 : 0 },
                { "disabled", panel.Tactical != null ? string.Join(",", panel.Tactical.DisabledOrders()) : "" },
            };
        }

        /// <summary>Spec WMC program §5: the map layer as a player drives it — arm a <c>mode</c> (move, route, orbit, hold,
        /// attack, cargo, off); right-click <c>ahead_km</c>/<c>right_km</c> from the player (or <c>x</c>/<c>z</c>, or the nearest
        /// known enemy with <c>enemy</c>, <c>shift</c> to queue); press route controls (<c>loop</c>, <c>send</c>, <c>skip</c>,
        /// <c>save</c>, <c>clear</c>, <c>undo</c>) on ORDERS — then report the mode, the draft, the overlay and the scope's
        /// task.</summary>
        public static Dictionary<string, object> WmcMap(Dictionary<string, object> args)
        {
            WmcPanel panel = WmcPanel.Instance;
            WingService wing = WingService.Instance;
            if (panel == null || wing == null) return Fail("WmcMap", "no WMC panel or wing");
            WmcContext c = panel.Context;
            panel.Refresh();
            string mode = Text(args, "mode");
            if (!string.IsNullOrEmpty(mode))
            {
                if (!Enum.TryParse(mode, true, out MapMode m)) return Fail("WmcMap", "unknown mode " + mode);
                c.Map.Arm(c, m);
            }
            bool shift = Arg(args, "shift") is bool sh && sh;
            Aircraft player = wing.Player;
            if (Arg(args, "enemy") is bool enemy && enemy)
            {
                if (player == null || !wing.NearestAirThreatUnit(player, out Unit target)) return Fail("WmcMap", "no enemy known");
                c.Map.Place(c, target.GlobalPosition(), target, shift);
            }
            else if (Arg(args, "ahead_km") != null || Arg(args, "right_km") != null)
            {
                if (player == null) return Fail("WmcMap", "not flying");
                float ahead = Kilometres(args, "ahead_km"), right = Kilometres(args, "right_km");
                GlobalPosition at = player.GlobalPosition();
                UnityEngine.Vector3 f = player.transform.forward, r = player.transform.right;
                f.y = r.y = 0f;
                f.Normalize();
                r.Normalize();
                c.Map.Place(c, new GlobalPosition(at.x + f.x * ahead + r.x * right, 0f, at.z + f.z * ahead + r.z * right), null, shift);
            }
            else if (Arg(args, "x") != null && Arg(args, "z") != null)
                c.Map.Place(c, new GlobalPosition(Number(args, "x", 0), 0f, Number(args, "z", 0)), null, shift);
            // TACTICAL › ROUTE's controls (built in R2; until then nothing is pressed).
            int pressed = 0;
            foreach (string control in new[] { "loop", "send", "skip", "save", "clear", "undo" })
            {
                if (!(Arg(args, control) is bool on) || !on) continue;
                if (panel.Press("tac.route." + control)) pressed++;
            }
            panel.Refresh();
            WingPlanner p = wing.Roster.InUse(c.ScopeElement) ? wing.PlannerOf(c.ScopeElement) : null;
            return new Dictionary<string, object>
            {
                { "ok", true }, { "mode", c.Map.Mode.ToString() }, { "armed", c.Map.Mode != MapMode.Off ? 1 : 0 },
                { "draft", c.Draft.Count }, { "loop", (int)c.Draft.Loop }, { "pressed", pressed },
                { "legs", panel.Overlay.LegCount }, { "rings", panel.Overlay.RingCount }, { "routes", WmcRoutes.Store.Routes.Count },
                { "scope", c.ScopeLabel }, { "scope_element", c.ScopeElement },
                { "patrolling", p != null && p.Active && p.Current.Kind == TaskKind.Patrol ? 1 : 0 },
                { "moving", p != null && p.Active && p.Current.Kind == TaskKind.Move ? 1 : 0 },
                { "orbiting", p != null && p.Active && p.Current.Kind == TaskKind.Orbit ? 1 : 0 },
                { "last", WingToast.Last ?? "" },
            };
        }

        /// <summary>Spec WMC program §6: the room as a player drives it — <c>open</c>, <c>page</c>, <c>fit</c>, <c>zoom</c> (factor
        /// at the view centre), <c>click_ahead_km</c>/<c>click_right_km</c> with <c>button</c> "left" or "right" through TACTICAL's
        /// own click path, <c>press</c> a room control, <c>close</c> — then report it.</summary>
        public static Dictionary<string, object> WmcRoom(Dictionary<string, object> args)
        {
            WmcRoom room = global::WingCommand.WmcRoom.Instance;
            if (room == null) return Fail("WmcRoom", "no room");
            if (Arg(args, "open") is bool open && open) room.Open(Number(args, "page", -1));
            RoomTactical t = room.Tactical;
            if (Arg(args, "fit") is bool fit && fit) t?.FitNow();
            if (Arg(args, "log") is bool log) t?.SetLog(log);
            float mppBefore = t != null ? t.MetresPerPixel : 0f;
            if (Arg(args, "zoom") != null) t?.ZoomCentre((float)Convert.ToDouble(Arg(args, "zoom"), CultureInfo.InvariantCulture));
            float zoomRatio = t != null && mppBefore > 0f ? t.MetresPerPixel / mppBefore : 1f;
            if ((Arg(args, "click_ahead_km") != null || Arg(args, "click_right_km") != null) && t != null)
            {
                Aircraft player = WingService.Instance?.Player;
                if (player == null) return Fail("WmcRoom", "not flying");
                float ahead = Kilometres(args, "click_ahead_km"), right = Kilometres(args, "click_right_km");
                GlobalPosition at = player.GlobalPosition();
                UnityEngine.Vector3 f = player.transform.forward, r = player.transform.right;
                f.y = r.y = 0f;
                f.Normalize();
                r.Normalize();
                t.ClickWorld(at.x + f.x * ahead + r.x * right, at.z + f.z * ahead + r.z * right, Text(args, "button") != "left", Arg(args, "shift") is bool sh && sh);
            }
            bool pressed = false;
            string press = Text(args, "press");
            if (!string.IsNullOrEmpty(press) && t != null && t.Controls.TryGetValue(press, out NOAvionics.Ui.AvButton b) && b != null && b.gameObject.activeInHierarchy)
            {
                b.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
                pressed = true;
            }
            room.RefreshNow();
            if (Arg(args, "close") is bool close && close) room.Close();
            return new Dictionary<string, object>
            {
                { "ok", true }, { "open", room.IsOpen ? 1 : 0 }, { "page", room.Page }, { "keyboard_held", room.KeyboardHeld ? 1 : 0 },
                { "pressed", pressed ? 1 : 0 }, { "markers", t?.Markers ?? 0 }, { "legs", t?.LegCount ?? 0 }, { "contacts", t?.ContactCount ?? 0 },
                { "fields", t?.FieldCount ?? 0 }, { "mpp", t?.MetresPerPixel ?? 0f }, { "cards", t?.Cards ?? 0 },
                { "card_member", t != null && t.CardMember != 0u ? 1 : 0 }, { "zoom_ratio", zoomRatio }, { "last", WingToast.Last ?? "" },
                { "log_open", t != null && t.LogOpen ? 1 : 0 }, { "log_rows", t?.LogRowsShown ?? 0 },
            };
        }

        private static float Kilometres(Dictionary<string, object> args, string key) =>
            Arg(args, key) is object v ? (float)Convert.ToDouble(v, CultureInfo.InvariantCulture) * 1000f : 0f;

        public static Dictionary<string, object> Disengage(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Disengage", "the wing is not active");
            return Ok("disengaged", wing.Disengage());
        }

        /// <summary>Engaged members now, engage/disengage events so far, and native switches the guard redirected.</summary>
        /// <summary>Spec M7 §1.6: the radio's counters.</summary>
        public static Dictionary<string, object> RadioState(Dictionary<string, object> args)
        {
            RadioQueue q = RadioDirector.Instance?.Queue;
            if (q == null) return Fail("RadioState", "no radio");
            int sent = q.SentOf(RadioClass.Chatter) + q.SentOf(RadioClass.Status) + q.SentOf(RadioClass.Tactical) + q.SentOf(RadioClass.Emergency);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "radio_sent", sent }, { "radio_emergency", q.SentOf(RadioClass.Emergency) },
                { "radio_tactical", q.SentOf(RadioClass.Tactical) }, { "radio_status", q.SentOf(RadioClass.Status) },
                { "radio_chatter", q.SentOf(RadioClass.Chatter) }, { "radio_stale", q.DroppedStale },
                { "radio_repeat", q.DroppedRepeat }, { "radio_full", q.DroppedFull },
                { "radio_contacts", RadioDirector.Instance.ContactsCalled },
                // +inf (no speaker spoke twice) is reported as a large number so JSON stays valid.
                { "radio_min_gap", float.IsInfinity(q.MinSpeakerGap) ? 999f : q.MinSpeakerGap },
            };
        }

        /// <summary>Splash at the nearest known enemy aircraft to the wing's player or anchor (spec M5 §9.2).</summary>
        public static Dictionary<string, object> Splash(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Splash", "the wing is not active");
            Aircraft from = wing.Player;
            if (from == null)
                foreach (WingMember m in wing.Members)
                    if (!m.Released && m.Alive)
                    {
                        from = m.Aircraft;
                        break;
                    }
            if (from == null || !wing.NearestAirThreatUnit(from, out Unit target)) return Fail("Splash", "no target");
            int fired = wing.Splash(target, out int capable);
            Plugin.Logger.LogInfo($"[Automation] Splash on {target.unitName}: {fired} fired, {capable} in envelope");
            return new Dictionary<string, object> { { "ok", true }, { "fired", fired }, { "capable", capable }, { "shots", wing.SplashShots } };
        }

        /// <summary>Spec M5 §10.1: set the stack (args: metres).</summary>
        public static Dictionary<string, object> Stack(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Stack", "the wing is not active");
            float metres = args != null && args.TryGetValue("metres", out object v) ? System.Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture) : 0f;
            wing.SetStack(metres);
            return new Dictionary<string, object> { { "ok", true }, { "stack", wing.Stack } };
        }

        /// <summary>Asks for Bogey Dope as the radial entry does.</summary>
        public static Dictionary<string, object> BogeyDope(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null || wing.Player == null) return Fail("BogeyDope", "no wing or player");
            bool asked = WingCommands.BogeyDopeCall(wing, out string answer);
            Plugin.Logger.LogInfo($"[Automation] BogeyDope: {answer}");
            return new Dictionary<string, object> { { "ok", asked }, { "clean", answer == "Picture clean." ? 1 : 0 } };
        }

        public static Dictionary<string, object> CombatState(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("CombatState", "the wing is not active");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "engaged", wing.EngagedCount }, { "assigned", wing.AssignedCount }, { "members", wing.Members.Count },
                { "engage_events", wing.Events.CountOf(WingEventKind.Engaged) },
                { "disengage_events", wing.Events.CountOf(WingEventKind.Disengaged) },
                { "redirected", SwitchStateGuard.Redirected },
                { "hostiles", wing.LastHostiles }, { "fallbacks", wing.FallBacks },
                { "jokers", wing.Events.CountOf(WingEventKind.Joker) },
                { "defends", wing.Events.CountOf(TransitionReason.MissileInbound) },
                { "standing_shots", wing.StandingShots },
                { "radars_on", wing.RadarsOn },
                { "reacts", wing.Reacts },
            };
        }

        /// <summary>Dev only (spec WMC rebuild R3 alerts): member <c>member</c> (its #n) has its parts' hit points set to
        /// <c>hp</c> (0-100), or with <c>kill</c> its aircraft is disabled, as a shoot-down would.</summary>
        public static Dictionary<string, object> Damage(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Damage", "the wing is not active");
            int number = (int)Number(args, "member", 2);
            WingMember m = null;
            foreach (WingMember x in wing.Members)
                if (x.Number == number) m = x;
            if (m == null || m.Aircraft == null) return Fail("Damage", "no such member");
            if (Arg(args, "kill") is bool kill && kill)
            {
                m.Aircraft.DisableUnit();
                return Ok("killed", number);
            }
            float hp = Number(args, "hp", 40);
            int parts = 0;
            if (m.Aircraft.partLookup != null)
                foreach (UnitPart p in m.Aircraft.partLookup)
                    if (p != null)
                    {
                        p.hitPoints = hp;
                        parts++;
                    }
            return Ok("parts", parts);
        }

        /// <summary>What calls have cost this mission: charged, refunded, and the player's allocation now; the squadron's
        /// pilots flying, free and lost; with args.type, the faction's stock of that airframe (−1: unknown).</summary>
        public static Dictionary<string, object> Economy(Dictionary<string, object> args)
        {
            GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player);
            float allocation = player != null ? player.Allocation : -1f;
            int flying = 0, free = 0, lost = 0;
            foreach (WingPilot p in WingPilotRoster.DisplayRoster())
            {
                if (p.Lost) lost++;
                else if (WingPilotRoster.IsFlying(p)) flying++;
                else if (WingPilotRoster.IsFree(p)) free++;
            }
            string typeName = Text(args, "type");
            AircraftDefinition type = typeName != null ? FindType(typeName) : null;
            Aircraft lead = Arg(args, "leadUnit") as Aircraft ?? WingService.Instance?.Player;
            FactionHQ hq = lead != null ? lead.NetworkHQ : null;
            int stock = type != null && hq != null ? hq.GetUnitSupply(type) : -1;
            Plugin.Logger.LogInfo($"[Automation] Economy: charged {WingLedger.Charged:0}, refunded {WingLedger.Refunded:0}, " +
                                  $"allocation {allocation:0}, pilots {flying} flying / {free} free / {lost} lost, stock {stock}");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "charged", WingLedger.Charged }, { "refunded", WingLedger.Refunded }, { "allocation", allocation },
                { "flying", flying }, { "free", free }, { "lost", lost }, { "stock", stock },
            };
        }

        /// <summary>Sends every member home to the reserve (spec M3 §4).</summary>
        public static Dictionary<string, object> Rtb(Dictionary<string, object> args) => Recover(RecoveryIntent.Rtb, "Rtb");

        /// <summary>Sends every member home to refuel and rearm, then back out.</summary>
        public static Dictionary<string, object> Refit(Dictionary<string, object> args) => Recover(RecoveryIntent.Refit, "Refit");

        private static Dictionary<string, object> Recover(RecoveryIntent intent, string hook)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail(hook, "the wing is not active");
            int n = wing.RecoverAll(intent);
            Plugin.Logger.LogInfo($"[Automation] {hook}: {n} of {wing.Members.Count} wingmen going");
            return n > 0 ? Ok("recovering", n) : Fail(hook, "no wingman could go (no friendly field, or already going)");
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
