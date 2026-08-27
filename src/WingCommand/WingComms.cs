using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Wingman radio calls, written into the game's own on-screen message feed.
    ///
    /// Until now the only way to tell what the wing was doing was to read the BepInEx
    /// log, which is a poor way to command a flight. <c>MessageUI.GameMessage</c> is public
    /// on a SceneSingleton, so this needs no patching.
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
            Down,
            Unable,
            Copy,
        }

        private const float RepeatCooldown = 12f;

        /// <summary>Per member+kind cooldowns, keyed so one wingman cannot spam the feed.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> lastSpoken =
            new System.Collections.Generic.Dictionary<string, float>();

        public static void Say(WingMember member, Call call, string detail = null)
        {
            if (!Plugin.Config2.RadioChatter.Value || member == null) return;

            string key = member.Slot + ":" + call;
            if (lastSpoken.TryGetValue(key, out float last) &&
                Time.timeSinceLevelLoad - last < RepeatCooldown)
                return;

            lastSpoken[key] = Time.timeSinceLevelLoad;
            Broadcast(Callsign(member) + ", " + Phrase(call, detail));
        }

        /// <summary>A call from the flight as a whole, in response to a player order.</summary>

        public static void Reset() => lastSpoken.Clear();

        private static void Broadcast(string line)
        {
            try
            {
                MessageUI ui = SceneSingleton<MessageUI>.i;
                if (ui != null) ui.GameMessage(line);
            }
            catch
            {
                // The feed is cosmetic; never let a missing UI break the wing.
            }
        }

        /// <summary>
        /// Wingmen are numbered from two, as in a real flight: the player is One.
        /// </summary>
        private static string Callsign(WingMember member)
        {
            switch (member.Slot)
            {
                case 1: return "Two";
                case 2: return "Three";
                case 3: return "Four";
                default: return "Number " + (member.Slot + 1);
            }
        }

        private static string Phrase(Call call, string detail)
        {
            switch (call)
            {
                case Call.Engaging:   return detail != null ? "engaging " + detail : "engaging";
                case Call.Defending:  return "defending";
                case Call.Splash:     return detail != null ? "splash " + detail : "splash one";
                case Call.Winchester: return "Winchester, returning to base";
                case Call.Bingo:      return "bingo fuel, returning to base";
                case Call.Rejoining:  return "rejoining";
                case Call.Unable:     return "unable to keep up, returning to base";
                case Call.Breaking:   return detail != null ? "breaking " + detail : "breaking";
                case Call.FallingBack: return "breaking off, falling back";
                case Call.Holding:    return "holding at standoff";
                case Call.Covering:   return "covering you";
                case Call.Orbiting:   return "on station, orbiting";
                case Call.Delivering: return "running the cargo in";
                case Call.Down:       return "on the deck";
                default:              return "copy";
            }
        }
    }
}
