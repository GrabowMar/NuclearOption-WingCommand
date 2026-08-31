using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Wingman radio calls, written into the dedicated squadron subtitle surface.
    ///
    /// Until now the only way to tell what the wing was doing was to read the BepInEx
    /// log, which is a poor way to command a flight. Spoken radio gets a speaker identity
    /// and a separately styled line; debug actions alone retain the legacy message feed.
    ///
    /// Calls are rate-limited per member per kind: a wingman that is repeatedly defending
    /// should say so once, not once per engagement tick.
    /// </summary>
    internal static class WingComms
    {
        internal enum Call
        {
            Engaging,
            Defending,
            Splash,
            Winchester,
            Bingo,
            Rejoining,
            Breaking,
            FallingBack,
            Holding,
            Covering,
            Orbiting,
            Delivering,
            Delivered,
            NoDropOff,
            FireForEffect,
            Expended,
            Down,
            Unable,
            Damaged,
            Critical,
            Panic,
            DefensiveClear,
            Recovered,
            Detached,
        }

        private const float RepeatCooldown = 12f;

        /// <summary>Per member+kind cooldowns, keyed so one wingman cannot spam the feed.</summary>
        private static readonly Dictionary<string, float> lastSpoken =
            new Dictionary<string, float>();

        public static void Say(WingMember member, Call call, string detail = null)
        {
            if (!Plugin.Config2.RadioChatter.Value || member == null) return;

            WingPilot pilot = member.Crew;
            string speakerKey = pilot != null && !string.IsNullOrWhiteSpace(pilot.Callsign)
                ? pilot.Callsign
                : member.Slot.ToString();
            string key = speakerKey + ":" + call;
            if (lastSpoken.TryGetValue(key, out float last) &&
                Time.timeSinceLevelLoad - last < RepeatCooldown)
                return;

            lastSpoken[key] = Time.timeSinceLevelLoad;
            string phrase = ChatterDialogue.Event(
                Persona(member), call.ToString(), detail, Random.Range(0, int.MaxValue));
            Broadcast(member, phrase,
                call == Call.Panic || call == Call.Critical);
        }

        /// <summary>
        /// Confirm an order from exactly the aircraft that accepted it. A single member
        /// answers for itself; a group uses its lowest-numbered member as element lead and
        /// names the other responders in one line instead of filling the queue with roll call.
        /// </summary>
        public static void Acknowledge(IReadOnlyList<WingMember> members, WingOrder order)
        {
            if (!Plugin.Config2.RadioChatter.Value || members == null) return;

            var ordered = new List<WingMember>();
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null) ordered.Add(members[i]);
            ordered.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            if (ordered.Count == 0) return;

            WingMember lead = ordered[0];
            if (ordered.Count > 1)
            {
                string groupPhrase = ChatterDialogue.GroupAcknowledge(
                    Persona(lead), order.ToString(), OtherNumbers(ordered),
                    Random.Range(0, int.MaxValue));
                Broadcast(lead, groupPhrase, urgent: false);
                return;
            }

            string phrase;
            if (order == WingOrder.Attack)
                phrase = ChatterDialogue.Event(Persona(lead), "Engaging",
                    lead.AssignedTarget != null ? lead.AssignedTarget.unitName : null,
                    Random.Range(0, int.MaxValue));
            else if (order == WingOrder.FireForEffect)
                phrase = ChatterDialogue.Event(Persona(lead), "FireForEffect",
                    lead.AssignedTarget != null ? lead.AssignedTarget.unitName : null,
                    Random.Range(0, int.MaxValue));
            else
                phrase = ChatterDialogue.Acknowledge(
                    Persona(lead), order.ToString(), Random.Range(0, int.MaxValue));

            Broadcast(lead, phrase, urgent: false);
        }

        /// <summary>Have a surviving pilot call a loss; the dead pilot never reports itself.</summary>
        public static void ReportLoss(WingMember lost, IReadOnlyList<WingMember> flight)
        {
            if (!Plugin.Config2.RadioChatter.Value || lost == null) return;

            WingMember reporter = null;
            if (flight != null)
            {
                for (int i = 0; i < flight.Count; i++)
                {
                    WingMember candidate = flight[i];
                    if (candidate == null || candidate == lost || !candidate.Alive) continue;
                    if (reporter == null || candidate.Slot < reporter.Slot) reporter = candidate;
                }
            }

            string eventName = lost.Pilot != null && lost.Pilot.ejected
                ? "Ejected"
                : lost.Pilot != null && lost.Pilot.dead
                    ? "PilotKilled"
                    : "AirframeLost";
            string lostName = lost.Crew != null && !string.IsNullOrWhiteSpace(lost.Crew.Callsign)
                ? lost.Crew.Callsign.ToUpperInvariant()
                : TacticalNumber(lost);

            if (reporter != null)
            {
                string phrase = ChatterDialogue.Event(
                    Persona(reporter), eventName, lostName, Random.Range(0, int.MaxValue));
                Broadcast(reporter, phrase, urgent: true);
            }
            else
            {
                Broadcast(lost, "Mayday! I'm going down!", urgent: true);
            }
        }

        public static void Tick() => WingChatterHud.Tick();

        public static void Reset()
        {
            lastSpoken.Clear();
            WingChatterHud.Reset();
        }

        private static void Broadcast(WingMember member, string line, bool urgent)
        {
            if (member == null || string.IsNullOrWhiteSpace(line)) return;
            WingChatterHud.Enqueue(Identity(member), Context(member), line, urgent);
        }

        private static string Identity(WingMember member)
        {
            WingPilot pilot = member.Crew;
            if (pilot != null) return ChatterDialogue.Identity(pilot.Name, pilot.Callsign);
            return "WING " + (member.Slot + 1) + " \"NO CALLSIGN\"";
        }

        private static string Context(WingMember member)
        {
            string aircraft = member != null && !string.IsNullOrWhiteSpace(member.Name)
                ? member.Name.ToUpperInvariant()
                : "AIRCRAFT UNKNOWN";
            return "▲ WING " + (member.Slot + 1) + "  //  " + aircraft;
        }

        private static string OtherNumbers(List<WingMember> ordered)
        {
            var labels = new List<string>();
            for (int i = 1; i < ordered.Count; i++) labels.Add(TacticalNumber(ordered[i]));
            if (labels.Count == 0) return string.Empty;
            if (labels.Count == 1) return labels[0];
            if (labels.Count == 2) return labels[0] + " and " + labels[1];
            return string.Join(", ", labels.GetRange(0, labels.Count - 1).ToArray()) +
                   " and " + labels[labels.Count - 1];
        }

        private static string TacticalNumber(WingMember member)
        {
            int number = member != null ? member.Slot + 1 : 0;
            switch (number)
            {
                case 2: return "Two";
                case 3: return "Three";
                case 4: return "Four";
                case 5: return "Five";
                case 6: return "Six";
                case 7: return "Seven";
                case 8: return "Eight";
                case 9: return "Nine";
                default: return "Wing " + number;
            }
        }

        private static ChatterPersona Persona(WingMember member) =>
            member?.Crew != null ? member.Crew.Persona : ChatterPersona.Professional;
    }
}
