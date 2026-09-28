using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>A sortie for the DEBRIEF (spec WMC rebuild §PLAN LOG and DEBRIEF), folded from the wing's events as they come — the ring
    /// keeps only the last 256 — with kill credit by shooter, said in a few lines (safe glyphs only).</summary>
    internal sealed class SortieLog
    {
        public const int MaxShooters = 8;
        public string Theatre = "";
        public float Start, End;
        public int Launched, Airborne, Landed, Lost, Ejected, Kills, TasksDone, TasksFailed, TargetsDown, Relocated, Aborted, Gcas, Collisions;
        private readonly string[] shooters = new string[MaxShooters];
        private readonly int[] shooterKills = new int[MaxShooters];
        private int shooterCount;

        public void Begin(float time)
        {
            Start = End = time;
            Launched = Airborne = Landed = Lost = Ejected = Kills = TasksDone = TasksFailed = TargetsDown = Relocated = Aborted = Gcas = Collisions = 0;
            shooterCount = 0;
        }

        public void Finish(float time) => End = Math.Max(Start, time);

        public void Add(in WingEvent e)
        {
            End = Math.Max(End, e.Time);
            switch (e.Kind)
            {
                case WingEventKind.GroundSpawned: Launched++; break;
                case WingEventKind.Airborne: Airborne++; break;
                case WingEventKind.Landed: Landed++; break;
                case WingEventKind.TaskCompleted: TasksDone++; break;
                case WingEventKind.TaskFailed: TasksFailed++; break;
                case WingEventKind.TargetDestroyed: TargetsDown++; break;
                case WingEventKind.Relocated: Relocated++; break;
                case WingEventKind.DepartureAborted: Aborted++; break;
                case WingEventKind.GcasActivated: Gcas++; break;
                case WingEventKind.CollisionEmergency: Collisions++; break;
                case WingEventKind.MemberLost:
                    if (e.Reason == TransitionReason.Killed || e.Reason == TransitionReason.Ejected) Lost++;
                    if (e.Reason == TransitionReason.Ejected) Ejected++;
                    break;
            }
        }

        /// <summary>A kill by the wingman <paramref name="shooter"/> (its callsign).</summary>
        public void Kill(string shooter, string victimType)
        {
            Kills++;
            shooter = string.IsNullOrEmpty(shooter) ? "?" : shooter;
            for (int i = 0; i < shooterCount; i++)
                if (shooters[i] == shooter)
                {
                    shooterKills[i]++;
                    return;
                }
            if (shooterCount >= MaxShooters) return;
            shooters[shooterCount] = shooter;
            shooterKills[shooterCount++] = 1;
        }

        public List<string> Lines()
        {
            var lines = new List<string>();
            string head = "SORTIE " + WmcText.Clock(End - Start) + " · ";
            if (Launched == 0 && Airborne == 0 && Lost == 0 && Kills == 0)
            {
                lines.Add(head + "NOTHING FLOWN");
                return lines;
            }
            lines.Add(head + N(Launched) + " LAUNCHED · " + N(Airborne) + " AIRBORNE · " + N(Landed) + " LANDED · " + N(Lost) + " LOST");
            if (Kills > 0)
            {
                var sb = new StringBuilder("KILLS ").Append(N(Kills));
                // Most kills first (a stable pick: the order they first scored breaks ties).
                var order = new List<int>(shooterCount);
                for (int i = 0; i < shooterCount; i++) order.Add(i);
                order.Sort((a, b) => shooterKills[b] != shooterKills[a] ? shooterKills[b].CompareTo(shooterKills[a]) : a.CompareTo(b));
                for (int i = 0; i < order.Count && i < 4; i++) sb.Append(" · ").Append(shooters[order[i]]).Append(' ').Append(N(shooterKills[order[i]]));
                lines.Add(sb.ToString());
            }
            if (TasksDone + TasksFailed + TargetsDown > 0)
                lines.Add("TASKS " + N(TasksDone) + " DONE · " + N(TasksFailed) + " FAILED · " + N(TargetsDown) + (TargetsDown == 1 ? " TARGET DOWN" : " TARGETS DOWN"));
            if (Relocated + Aborted + Gcas + Collisions > 0)
                lines.Add("GROUND " + N(Relocated) + " MOVED · " + N(Aborted) + " ABORTED · SAFETY " + N(Gcas) + " GCAS" +
                          (Collisions > 0 ? " · " + N(Collisions) + " COLLISION" : ""));
            if (Ejected > 0) lines.Add(N(Ejected) + (Ejected == 1 ? " PILOT EJECTED" : " PILOTS EJECTED"));
            return lines;
        }

        private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The last <see cref="Max"/> debriefs, newest first, as <c>debrief.user.json</c>.</summary>
    internal sealed class DebriefStore
    {
        public const int Max = 10, MaxLines = 8;
        private readonly List<string[]> lines = new List<string[]>();
        private readonly List<string> theatres = new List<string>();

        public int Count => lines.Count;
        public string[] Lines(int i) => lines[i];
        public string Theatre(int i) => theatres[i];

        public void Add(List<string> sortie, string theatre)
        {
            if (sortie == null || sortie.Count == 0) return;
            lines.Insert(0, sortie.GetRange(0, Math.Min(sortie.Count, MaxLines)).ToArray());
            theatres.Insert(0, theatre ?? "");
            if (lines.Count > Max)
            {
                lines.RemoveAt(Max);
                theatres.RemoveAt(Max);
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder("{\"debriefs\":[");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"theatre\":\"").Append(MiniJson.Escape(theatres[i])).Append("\",\"lines\":[");
                for (int k = 0; k < lines[i].Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('"').Append(MiniJson.Escape(lines[i][k])).Append('"');
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        public static DebriefStore FromJson(string json, List<string> errors)
        {
            var store = new DebriefStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> top) || !MiniJson.TryGetList(top, "debriefs", out List<object> list))
            {
                errors.Add("not a debrief file; nothing loaded");
                return store;
            }
            foreach (object o in list)
            {
                if (store.lines.Count >= Max) break;
                if (!(o is Dictionary<string, object> d) || !MiniJson.TryGetList(d, "lines", out List<object> ls)) continue;
                var text = new List<string>();
                foreach (object l in ls)
                    if (l is string s && text.Count < MaxLines) text.Add(s);
                if (text.Count == 0) continue;
                store.lines.Add(text.ToArray());
                store.theatres.Add(MiniJson.GetString(d, "theatre") ?? "");
            }
            return store;
        }
    }
}
