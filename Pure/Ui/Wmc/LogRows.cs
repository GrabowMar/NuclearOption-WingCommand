using System.Collections.Generic;
using System.Text;

namespace WingCommand
{
    internal struct LogRow
    {
        public float Time;
        /// <summary>The member's slot when the event was logged, -1 for the wing or a radio line.</summary>
        public int Member;
        /// <summary>The member's aircraft (persistent id), 0 for the wing or a radio line.</summary>
        public uint Id;
        public string Text;
        public bool Radio;
    }

    /// <summary>Which lines LOG shows (spec WMC program §4): everything, one element (its task events and its members'
    /// events, by the element each aircraft flies in now), or one aircraft's events (<see cref="ById"/>; id 0 matches
    /// nothing). Radio lines only when unfiltered.</summary>
    internal struct LogFilter
    {
        public int Element;
        public bool ById;
        public uint Id;
        public SnapshotMember[] Rows;
        public int Count;

        public static LogFilter None => new LogFilter { Element = -1 };

        public bool Any => Element >= 0 || ById;

        public bool Match(in WingEvent e)
        {
            if (ById) return Id != 0u && e.Id == Id;
            if (Element < 0) return true;
            if (e.Member < 0) return e.Element == Element;
            for (int i = 0; i < Count && Rows != null; i++)
                if (e.Id != 0u && Rows[i].Id == e.Id) return Rows[i].Element == Element;
            return false;
        }
    }

    /// <summary>The LOG tab (spec M7b §3): notable wing events and radio lines, newest first. Behaviour flips are
    /// the HUD's business and are left out.</summary>
    internal static class LogRows
    {
        public const int MaxRows = 40;

        public static string Who(int member) => member >= 0 ? WingRows.Number(member) : "WING";

        public static string Describe(in WingEvent e)
        {
            string task = e.Task.ToString().ToUpperInvariant();
            switch (e.Kind)
            {
                case WingEventKind.BehaviourChanged:
                case WingEventKind.FallingBehindCleared:
                case WingEventKind.Converted:
                    return null;
                case WingEventKind.FallingBehind: return "falling behind";
                case WingEventKind.GcasActivated: return "GCAS pull-up";
                case WingEventKind.CollisionEmergency: return "collision avoidance";
                case WingEventKind.AnchorLost: return "lost the leader";
                case WingEventKind.GroundSpawned: return "at the field";
                case WingEventKind.Taxiing: return "taxiing";
                case WingEventKind.HoldingShort: return "holding short";
                case WingEventKind.LiningUp: return "lining up";
                case WingEventKind.Rolling: return "takeoff roll";
                case WingEventKind.Airborne: return "airborne";
                case WingEventKind.Rerouted: return "taxi rerouted";
                case WingEventKind.Relocated: return "moved to the hold point";
                case WingEventKind.DepartureAborted: return "departure aborted";
                case WingEventKind.Parked: return "parked";
                case WingEventKind.PulledAside: return "pulled aside";
                case WingEventKind.Landed: return "landed";
                case WingEventKind.LandingFailed: return "landing failed";
                case WingEventKind.Serviced: return "serviced";
                case WingEventKind.Reserved: return "back in reserve";
                case WingEventKind.Bingo: return "bingo fuel";
                case WingEventKind.Joker: return "joker fuel";
                case WingEventKind.TaskStarted: return task + " started";
                case WingEventKind.TaskCompleted: return task + " complete";
                case WingEventKind.TaskCancelled: return task + " cancelled";
                case WingEventKind.TaskFailed: return task + " failed" + Because(e.Reason);
                case WingEventKind.WaypointReached: return "waypoint reached";
                case WingEventKind.Engaged: return "engaged";
                case WingEventKind.Disengaged: return "disengaged" + Because(e.Reason);
                default: return e.Kind.ToString();
            }
        }

        private static string Because(TransitionReason r) => r == TransitionReason.None ? "" : " (" + Words(r.ToString()) + ")";

        /// <summary>"NoTarget" → "no target".</summary>
        private static string Words(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Newest first, at most <paramref name="max"/> rows; on a tie the event goes first. Returns the count.</summary>
        public static int Fill(WingEventRing events, RadioLog radio, List<LogRow> into, int max = MaxRows) =>
            Fill(events, radio, into, max, LogFilter.None);

        public static int Fill(WingEventRing events, RadioLog radio, List<LogRow> into, int max, in LogFilter filter)
        {
            into.Clear();
            if (filter.Any) radio = null;
            int e = (events?.Count ?? 0) - 1, r = (radio?.Count ?? 0) - 1;
            while (into.Count < max && (e >= 0 || r >= 0))
            {
                // Skip events that are not listed without spending a row.
                if (e >= 0 && (Describe(events[e]) == null || !filter.Match(events[e])))
                {
                    e--;
                    continue;
                }
                bool takeEvent = e >= 0 && (r < 0 || events[e].Time >= radio.TimeAt(r));
                if (takeEvent)
                {
                    WingEvent ev = events[e--];
                    into.Add(new LogRow { Time = ev.Time, Member = ev.Member, Id = ev.Id, Text = Describe(ev) });
                }
                else
                {
                    into.Add(new LogRow { Time = radio.TimeAt(r), Member = -1, Text = radio.TextAt(r), Radio = true });
                    r--;
                }
            }
            return into.Count;
        }
    }
}
