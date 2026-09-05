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
        private readonly struct SpeechKey : System.IEquatable<SpeechKey>
        {
            private readonly int slot;
            private readonly Call call;

            public SpeechKey(WingMember member, Call call)
            {
                slot = member != null ? member.Slot : -1;
                this.call = call;
            }

            public bool Equals(SpeechKey other) =>
                slot == other.slot && call == other.call;

            public override bool Equals(object obj) => obj is SpeechKey other && Equals(other);

            public override int GetHashCode() => (slot * 397) ^ (int)call;
        }

        internal enum Call
        {
            Engaging,
            Defending,
            Splash,
            Winchester,
            Bingo,
            Rejoining,
            Covering,
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
            JammingOff,
            Maneuvering,
            ManeuverDone,
            BreakCall,
            SlowLeader,
        }

        /// <summary>
        /// Calls a commander needs to hear even in Performance mode. Everything else -
        /// order acks, engaging/splash/covering colour, rejoining status,
        /// the delivery lifecycle, manoeuvre and jam chatter - is dropped there.
        /// Losses go through <see cref="ReportLoss"/> and are never gated by this.
        /// </summary>
        private static bool Critical(Call call)
        {
            switch (call)
            {
                case Call.Winchester:
                case Call.Bingo:
                case Call.Unable:
                case Call.SlowLeader:
                case Call.Damaged:
                case Call.Critical:
                case Call.Panic:
                case Call.DefensiveClear:
                case Call.NoDropOff:
                case Call.BreakCall:
                    return true;
                default:
                    return false;
            }
        }

        private const float RepeatCooldown = 12f;
        private const float BanterCheckMin = 80f;
        private const float BanterCheckMax = 160f;
        private const float BanterChance = 0.28f;

        /// <summary>
        /// Per slot+kind cooldowns. A value key avoids allocating a callsign string every
        /// time a rapidly repeating combat state asks to speak while still on cooldown, and
        /// slot keys keep the table bounded even through many replacements in a long mission.
        /// </summary>
        private static readonly Dictionary<SpeechKey, float> lastSpoken =
            new Dictionary<SpeechKey, float>();
        private static float nextBanterCheck;

        public static void Say(WingMember member, Call call, string detail = null)
        {
            if (Plugin.Settings.Radio.Value == ChatterLevel.Off || member == null) return;

            // Performance mode keeps only the calls a commander actually needs to hear.
            if (!WingBrain.RichChatter && !Critical(call)) return;

            var key = new SpeechKey(member, call);
            if (lastSpoken.TryGetValue(key, out float last) &&
                Time.timeSinceLevelLoad - last < RepeatCooldown)
                return;

            lastSpoken[key] = Time.timeSinceLevelLoad;
            string tag = DialogueTag(member.Crew);
            if (!WingCustomPilots.TryGetEventLine(tag, call.ToString(), detail, out string phrase))
            {
                phrase = ChatterDialogue.Event(
                    Persona(member), call.ToString(), detail, Random.Range(0, int.MaxValue));
            }
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
            if (Plugin.Settings.Radio.Value == ChatterLevel.Off || members == null) return;

            // Order acknowledgements are flavour, not information - dropped in Performance mode.
            if (!WingBrain.RichChatter) return;

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
            else if (order == WingOrder.JamTarget)
                phrase = ChatterDialogue.Event(Persona(lead), "Jamming",
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
            if (Plugin.Settings.Radio.Value == ChatterLevel.Off || lost == null) return;

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

        public static void Tick(WingRegistry wing)
        {
            WingChatterHud.Tick();

            if (Plugin.Settings.Radio.Value == ChatterLevel.Off || wing == null)
                return;

            CheckLeaderThreats(wing);

            if (!WingBrain.RichChatter)
                return;

            float now = Time.unscaledTime;
            if (nextBanterCheck <= 0f)
            {
                nextBanterCheck = now + Random.Range(BanterCheckMin, BanterCheckMax);
                return;
            }

            // The common path is one timestamp comparison. Only the infrequent check walks
            // the small wing roster, and busy operational radio always wins over a joke.
            if (now < nextBanterCheck) return;
            nextBanterCheck = now + Random.Range(BanterCheckMin, BanterCheckMax);
            if (!WingChatterHud.IsIdle || Random.value > BanterChance) return;

            IReadOnlyList<WingMember> members = wing.Members;
            int eligible = 0;
            for (int i = 0; i < members.Count; i++)
                if (CanBanter(members[i])) eligible++;
            if (eligible == 0) return;

            if (!TryChooseBanter(members, eligible, Random.Range(0, int.MaxValue),
                                 out WingMember first, out WingMember second,
                                 out ChatterExchange exchange))
                return;

            Broadcast(first, exchange.Opening, urgent: false);

            if (exchange.Reply != null)
                Broadcast(second, exchange.Reply, urgent: false);
        }

        private static float nextThreatCheck;

        private static void CheckLeaderThreats(WingRegistry wing)
        {
            if (wing == null || wing.Leader == null) return;
            float now = Time.timeSinceLevelLoad;
            if (now < nextThreatCheck) return;
            nextThreatCheck = now + 1.2f;

            MissileWarning mws = wing.Leader.GetMissileWarningSystem();
            if (mws == null || !mws.IsWarning()) return;
            if (!mws.TryGetNearestIncoming(out Missile incoming) || incoming == null || incoming.disabled) return;

            WingMember bestCaller = null;
            float bestDistSq = float.MaxValue;
            Vector3 leadPos = wing.Leader.transform.position;
            IReadOnlyList<WingMember> members = wing.Members;

            for (int i = 0; i < members.Count; i++)
            {
                WingMember m = members[i];
                if (m == null || !m.Alive || m.Aircraft == null || m.IsPanicking) continue;
                float dSq = (m.Aircraft.transform.position - leadPos).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestCaller = m;
                }
            }

            if (bestCaller != null)
            {
                Say(bestCaller, Call.BreakCall);
                nextThreatCheck = now + 6.5f;
            }
        }

        public static void Reset()
        {
            lastSpoken.Clear();
            nextBanterCheck = 0f;
            nextThreatCheck = 0f;
            WingChatterHud.Reset();
        }

        private static bool CanBanter(WingMember member) =>
            member != null && member.Alive && !member.DeliveryPending && member.Crew != null;

        private static WingMember EligibleAt(IReadOnlyList<WingMember> members, int ordinal)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (!CanBanter(members[i])) continue;
                if (ordinal-- == 0) return members[i];
            }

            return null;
        }

        /// <summary>
        /// Find a valid exchange and its cast without temporary lists. This runs only after
        /// the sparse timer and random gate succeed, so scanning the static dialogue table is
        /// substantially cheaper than maintaining indexes as pilots join and leave.
        /// </summary>
        private static bool TryChooseBanter(IReadOnlyList<WingMember> members, int eligible,
                                            int seed, out WingMember first,
                                            out WingMember second,
                                            out ChatterExchange exchange)
        {
            first = null;
            second = null;
            exchange = default;
            int start = seed == int.MinValue ? 0 : System.Math.Abs(seed);

            for (int offset = 0; offset < ChatterDialogue.AmbientCount; offset++)
            {
                ChatterExchange candidate = ChatterDialogue.AmbientAt(start + offset);
                WingMember opener = candidate.SpeakerTag != null
                    ? EligibleWithTag(members, candidate.SpeakerTag, except: null)
                    : EligibleAt(members, (start + offset) % eligible);
                if (opener == null) continue;

                WingMember responder = null;
                if (candidate.Reply != null)
                {
                    if (eligible < 2) continue;
                    responder = candidate.ReplyTag != null
                        ? EligibleWithTag(members, candidate.ReplyTag, opener)
                        : OtherEligible(members, opener, (start + offset) % (eligible - 1));
                    if (responder == null) continue;
                }

                first = opener;
                second = responder;
                exchange = candidate;
                return true;
            }

            return false;
        }

        private static WingMember EligibleWithTag(IReadOnlyList<WingMember> members,
                                                   string tag, WingMember except)
        {
            for (int i = 0; i < members.Count; i++)
            {
                WingMember member = members[i];
                if (member == except || !CanBanter(member)) continue;
                if (string.Equals(DialogueTag(member.Crew), tag,
                                  System.StringComparison.OrdinalIgnoreCase))
                    return member;
            }

            return null;
        }

        private static WingMember OtherEligible(IReadOnlyList<WingMember> members,
                                                WingMember except, int ordinal)
        {
            for (int i = 0; i < members.Count; i++)
            {
                WingMember member = members[i];
                if (member == except || !CanBanter(member)) continue;
                if (ordinal-- == 0) return member;
            }

            return null;
        }

        private static string DialogueTag(WingPilot pilot) =>
            pilot == null || string.IsNullOrWhiteSpace(pilot.DialogueTag)
                ? pilot?.Callsign
                : pilot.DialogueTag;

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
