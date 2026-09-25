using System;

namespace WingCommand
{
    /// <summary>TACTICAL › FORMATION's MANEUVER row (spec WMC rebuild R3; design wmc-rebuild/maneuver.md): each member in scope
    /// flying with the wing reacts through its own brain and pipeline, then rejoins its slot on its element's task.</summary>
    internal sealed partial class WingService
    {
        /// <summary>BEAM turns across an air threat tracked within this range.</summary>
        public static float BeamRangeMetres = 60000f;

        private readonly Vec3[] splitSum = new Vec3[ElementRoster.MaxElements];
        private readonly int[] splitCount = new int[ElementRoster.MaxElements];

        /// <summary>Begins <paramref name="kind"/> for the members in <paramref name="who"/> (null: everyone). Returns how many
        /// began; <paramref name="refusal"/> names each member that could not and why (null when none).</summary>
        public int React(ReactionKind kind, Func<WingMember, bool> who, out string refusal)
        {
            refusal = null;
            // First pass: the middle of those that can fly one, per element (SPLIT turns each element's pair apart across its
            // own lead's track; review R3b: one centroid over a split wing turned both of a pair the same way).
            for (int e = 0; e < splitSum.Length; e++)
            {
                splitSum[e] = Vec3.Zero;
                splitCount[e] = 0;
            }
            foreach (WingMember m in Members)
                if (InScope(m, who) && CannotReact(m) == null)
                {
                    int e = ElementOf(m);
                    splitSum[e] += m.Last.Pos;
                    splitCount[e]++;
                }
            int started = 0;
            foreach (WingMember m in Members)
            {
                if (!InScope(m, who)) continue;
                string why = CannotReact(m);
                if (why == null)
                {
                    var o = new ReactionOrder { Kind = kind };
                    if (kind == ReactionKind.Split)
                    {
                        int e = ElementOf(m);
                        LeaderEstimate lead = WingOf(e).Frame.Leader;
                        Vec3 centroid = splitCount[e] > 0 ? splitSum[e] * (1f / splitCount[e]) : m.Last.Pos;
                        o.Side = ReactionManeuver.SplitSide(m.Last.Pos, centroid, splitCount[e] >= 2, lead.Pos, lead.Track, m.Seat);
                    }
                    else if (kind == ReactionKind.Beam && NearestAirThreat(m.Aircraft, out Vec3 threat, out _, out _) &&
                             (threat - m.Last.Pos).Length <= BeamRangeMetres)
                    {
                        o.HasThreat = true;
                        o.Threat = threat;
                    }
                    why = m.Brain.React(o, m.Last, m.Last.RadarAlt, m.Profile, missionTime, Events);
                }
                if (why == null) started++;
                else refusal = (refusal == null ? "" : refusal + ", ") + "#" + m.Number + " " + why;
            }
            if (started == 0 && refusal == null) refusal = "nobody in scope";
            return started;
        }

        private static bool InScope(WingMember m, Func<WingMember, bool> who) => !m.Released && m.Alive && (who == null || who(m));

        /// <summary>Why a member cannot maneuver now (null: it can): only one flying with the wing in our own state does.</summary>
        private static string CannotReact(WingMember m)
        {
            if (m.OnGround) return "on the ground";
            if (m.Settle != null) return "landed";
            if (m.Recovery != null) return "going home";
            if (m.Engaged) return "fighting";
            if (!ReferenceEquals(m.Pilot.currentState, m.State)) return "not in formation";
            return null;
        }

        /// <summary>Maneuvers begun this session (automation).</summary>
        public int Reacts
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Events.Count; i++)
                    if (Events[i].Kind == WingEventKind.BehaviourChanged && Events[i].To == BehaviourId.React) n++;
                return n;
            }
        }
    }
}
