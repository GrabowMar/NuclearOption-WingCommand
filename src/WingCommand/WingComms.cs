using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Wingman radio calls, written into the dedicated squadron subtitle surface.
    ///
    /// Until now the only way to tell what the wing was doing was to read the BepInEx
    /// log, which is a poor way to command a flight. Operational notices continue to use
    /// the game's feed; spoken radio gets a speaker identity and a separately styled line.
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
            Copy,
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
            Broadcast(member, phrase, call == Call.Panic || call == Call.Breaking);
        }

        /// <summary>
        /// A short roll-call response from exactly the aircraft that accepted an order.
        /// The HUD queue staggers the lines, so a group command reads as a flight answering
        /// in turn instead of one anonymous "Wing: copy" notification.
        /// </summary>
        public static void Acknowledge(IReadOnlyList<WingMember> members, WingOrder order)
        {
            if (!Plugin.Config2.RadioChatter.Value || members == null) return;

            // AttackTarget and FireForEffect already produce contextual "in hot" calls at
            // the moment each target is assigned. Adding a second copy here would make the
            // same pilot answer twice to one command.
            if (order == WingOrder.Attack || order == WingOrder.FireForEffect) return;

            for (int i = 0; i < members.Count; i++)
            {
                WingMember member = members[i];
                if (member == null) continue;
                string phrase = ChatterDialogue.Acknowledge(
                    Persona(member), order.ToString(), Random.Range(0, int.MaxValue));
                Broadcast(member, phrase, urgent: false);
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
            WingChatterHud.Enqueue(Identity(member), line, urgent);
        }

        private static string Identity(WingMember member)
        {
            WingPilot pilot = member.Crew;
            if (pilot != null) return ChatterDialogue.Identity(pilot.Name, pilot.Callsign);
            return "WING " + (member.Slot + 1) + " \"NO CALLSIGN\"";
        }

        private static ChatterPersona Persona(WingMember member) =>
            member?.Crew != null ? member.Crew.Persona : ChatterPersona.Professional;
    }
}
