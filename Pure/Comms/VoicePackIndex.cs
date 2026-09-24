using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Spec M7 §5: a Yappinator-format voice pack's clips by event. A clip serves every event its file name
    /// names: the name split on anything not a letter or digit, digit-only tokens ignored, case-insensitive, Yappinator's
    /// aliases honoured (fox3 → fireFox3, kill → killGeneric…). Only the events a wingman's calls use are indexed.</summary>
    internal sealed class VoicePackIndex
    {
        /// <summary>The Yappinator events a wingman call can use, in their canonical names.</summary>
        public static readonly string[] Events =
        {
            "fireFox2", "fireFox3", "fireMissile", "killAircraft", "killGeneric", "RwrOnFox3", "RwrOn", "RwrOff", "fuelLow",
            "Touchdown", "Spawn",
        };

        private static readonly Dictionary<string, string> Tokens = BuildTokens();
        private static readonly Dictionary<string, string[]> Calls = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "FOX2", new[] { "fireFox2", "fireMissile" } },
            { "FOX3", new[] { "fireFox3", "fireMissile" } },
            { "SPLASH", new[] { "killAircraft", "killGeneric" } },
            { "PANIC", new[] { "RwrOnFox3", "RwrOn" } },
            { "DEFENSIVECLEAR", new[] { "RwrOff" } },
            { "BINGO", new[] { "fuelLow" } },
            { "JOKER", new[] { "fuelLow" } },
            { "DOWN", new[] { "Touchdown" } },
            { "AIRBORNEREJOINING", new[] { "Spawn" } },
        };

        private readonly Dictionary<string, List<int>> clips = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> files = new HashSet<int>();

        /// <summary>Files that serve at least one event.</summary>
        public int Count => files.Count;

        private static Dictionary<string, string> BuildTokens()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string e in Events) d[e] = e;
            d["fox2"] = "fireFox2";
            d["fox3"] = "fireFox3";
            d["missile"] = "fireMissile";
            d["kill"] = "killGeneric";
            d["rwr"] = "RwrOn";
            d["spawn"] = "Spawn";
            return d;
        }

        public void Add(int file, string fileNameWithoutExtension)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExtension)) return;
            int start = -1;
            for (int i = 0; i <= fileNameWithoutExtension.Length; i++)
            {
                bool part = i < fileNameWithoutExtension.Length && char.IsLetterOrDigit(fileNameWithoutExtension[i]);
                if (part)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start < 0) continue;
                string token = fileNameWithoutExtension.Substring(start, i - start);
                start = -1;
                if (AllDigits(token) || !Tokens.TryGetValue(token, out string evt)) continue;
                if (!clips.TryGetValue(evt, out List<int> list)) clips[evt] = list = new List<int>();
                if (!list.Contains(file)) list.Add(file);
                files.Add(file);
            }
        }

        private static bool AllDigits(string s)
        {
            foreach (char c in s)
                if (!char.IsDigit(c)) return false;
            return true;
        }

        /// <summary>The files for an event (empty when none).</summary>
        public IReadOnlyList<int> Clips(string evt) =>
            evt != null && clips.TryGetValue(evt, out List<int> list) ? (IReadOnlyList<int>)list : Array.Empty<int>();

        /// <summary>The events a wingman call may play, first found first (empty: none).</summary>
        public static IReadOnlyList<string> EventsFor(string call) =>
            call != null && Calls.TryGetValue(call, out string[] e) ? e : Array.Empty<string>();

        /// <summary>The pack for wingman #<paramref name="number"/> (#2 the first), round robin; -1 with none.</summary>
        public static int PackFor(int number, int packCount) =>
            packCount <= 0 ? -1 : ((number - 2) % packCount + packCount) % packCount;
    }
}
