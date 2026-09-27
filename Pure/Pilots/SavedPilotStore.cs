using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One pilot's part in a mission, for the service tally.</summary>
    internal struct MissionLine
    {
        public string Callsign;
        /// <summary>Sat in an aircraft at least once this mission.</summary>
        public bool Seated;
        public int Sorties, Kills, Xp;

        public static MissionLine Of(string callsign, bool seated, int sorties, int kills, int xp) =>
            new MissionLine { Callsign = callsign, Seated = seated, Sorties = sorties, Kills = kills, Xp = xp };
    }

    /// <summary>The saved pilots (research squadron-studio §2.1, lifecycle §6.7; the 2026-09-25 decision): identity, look, radio and bio
    /// persist; XP never does — a saved pilot joins every mission a ROOKIE. The service record (missions, sorties, kills, the best XP of
    /// one mission) grows once per mission and the studio never edits it. At most 24; callsigns unique ignoring case; a rename happens in
    /// place. The store keeps its own copies: a draft saved is a draft copied.</summary>
    internal sealed class SavedPilotStore
    {
        public const int Max = 24;

        private readonly List<CustomPilotRecord> records = new List<CustomPilotRecord>();

        public IReadOnlyList<CustomPilotRecord> Records => records;

        /// <summary>Moves on every change (the studio's list and the roster rebuild on it).</summary>
        public int Version { get; private set; }

        public static SavedPilotStore FromJson(string json, List<string> problems)
        {
            var store = new SavedPilotStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            if (!MiniJson.TryParse(json, out _))
            {
                problems?.Add("not a pilots file (unreadable JSON); nothing loaded");
                return store;
            }
            foreach (CustomPilotRecord raw in CustomPilotCodec.Decode(json).Pilots)
            {
                CustomPilotRecord r = Clean(raw, keepRecord: true);
                if (r.Callsign.Length == 0) problems?.Add("a pilot with no usable callsign was skipped");
                else if (store.Find(r.Callsign) != null) problems?.Add("duplicate callsign " + r.Callsign + " loaded once");
                else if (store.records.Count >= Max)
                {
                    problems?.Add("more than " + Max + " pilots: the rest were not loaded");
                    break;
                }
                else store.records.Add(r);
            }
            return store;
        }

        public string ToJson() => CustomPilotCodec.Encode(records);

        public CustomPilotRecord Find(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign)) return null;
            string c = callsign.Trim();
            foreach (CustomPilotRecord r in records)
                if (string.Equals(r.Callsign, c, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        /// <summary>Saves a draft: a new pilot when <paramref name="original"/> is null (or not saved), else the record saved under
        /// <paramref name="original"/>, renamed in place. Identity and look only; the stored service record stays. Refused: no usable
        /// callsign, one another saved pilot has, one a live unsaved pilot uses (<paramref name="liveTaken"/>), a full store.</summary>
        public bool Save(CustomPilotRecord draft, string original, Func<string, bool> liveTaken, out string why)
        {
            why = null;
            if (draft == null)
            {
                why = "Nothing to save.";
                return false;
            }
            CustomPilotRecord clean = Clean(draft, keepRecord: false);
            CustomPilotRecord stored = Find(original);
            if (clean.Callsign.Length == 0) why = "A callsign needs a letter or a digit.";
            else
            {
                CustomPilotRecord other = Find(clean.Callsign);
                bool renamed = stored == null || !string.Equals(stored.Callsign, clean.Callsign, StringComparison.OrdinalIgnoreCase);
                if (other != null && !ReferenceEquals(other, stored)) why = clean.Callsign + " is already a saved pilot.";
                else if (renamed && liveTaken != null && liveTaken(clean.Callsign)) why = clean.Callsign + " is flying this mission unsaved.";
                else if (stored == null && records.Count >= Max) why = "The squadron keeps " + Max + " saved pilots at most.";
            }
            if (why != null) return false;
            if (stored == null) records.Add(clean);
            else
            {
                clean.Missions = stored.Missions;
                clean.Sorties = stored.Sorties;
                clean.Kills = stored.Kills;
                clean.Xp = stored.Xp;
                records[records.IndexOf(stored)] = clean;
            }
            Version++;
            return true;
        }

        public bool Remove(string callsign)
        {
            CustomPilotRecord r = Find(callsign);
            if (r == null) return false;
            records.Remove(r);
            Version++;
            return true;
        }

        /// <summary>"HATCH-2" (the first free suffix; a long stem is cut to fit 14).</summary>
        public string CloneName(string callsign, Func<string, bool> liveTaken)
        {
            string stem = PilotText.Callsign(callsign);
            for (int n = 2; n < 100; n++)
            {
                string suffix = "-" + n;
                string c = (stem.Length + suffix.Length > PilotText.CallsignChars ? stem.Substring(0, PilotText.CallsignChars - suffix.Length) : stem)
                    .TrimEnd(' ', '-') + suffix;
                if (Find(c) == null && (liveTaken == null || !liveTaken(c))) return c;
            }
            return stem;
        }

        /// <summary>Adds the pilots whose callsigns are not saved yet, up to <see cref="Max"/>, each with an empty service record.</summary>
        public void Import(IEnumerable<CustomPilotRecord> incoming, Func<string, bool> liveTaken, out int added, out int skipped)
        {
            added = skipped = 0;
            if (incoming == null) return;
            foreach (CustomPilotRecord raw in incoming)
            {
                CustomPilotRecord r = raw != null ? Clean(raw, keepRecord: false) : null;
                if (r == null || r.Callsign.Length == 0 || Find(r.Callsign) != null || records.Count >= Max
                    || (liveTaken != null && liveTaken(r.Callsign)))
                {
                    skipped++;
                    continue;
                }
                records.Add(r);
                added++;
            }
            if (added > 0) Version++;
        }

        /// <summary>The mission's end: every saved pilot who sat in an aircraft counts a mission, adds its sorties and kills and keeps its
        /// best XP. True when something changed (the caller writes once).</summary>
        public bool Tally(IReadOnlyList<MissionLine> lines)
        {
            bool changed = false;
            if (lines == null) return false;
            foreach (MissionLine l in lines)
            {
                if (!l.Seated) continue;
                CustomPilotRecord r = Find(l.Callsign);
                if (r == null) continue;
                r.Missions++;
                r.Sorties += Math.Max(0, l.Sorties);
                r.Kills += Math.Max(0, l.Kills);
                r.Xp = Math.Max(r.Xp, l.Xp);
                changed = true;
            }
            if (changed) Version++;
            return changed;
        }

        /// <summary>A clean copy: text through <see cref="PilotText"/>, the look frozen, the service record kept or emptied.</summary>
        private static CustomPilotRecord Clean(CustomPilotRecord raw, bool keepRecord)
        {
            string callsign = PilotText.Callsign(raw.Callsign);
            string name = PilotText.Name(raw.Name);
            var r = new CustomPilotRecord
            {
                Callsign = callsign,
                Name = name.Length > 0 ? name : callsign,
                DialogueTag = PilotText.Tag(raw.DialogueTag),
                Persona = Enum.IsDefined(typeof(ChatterPersona), raw.Persona) ? raw.Persona : ChatterPersona.Professional,
                Background = PilotText.Bio(raw.Background),
                Missions = keepRecord ? Math.Max(0, raw.Missions) : 0,
                Sorties = keepRecord ? Math.Max(0, raw.Sorties) : 0,
                Kills = keepRecord ? Math.Max(0, raw.Kills) : 0,
                Xp = keepRecord ? Math.Max(0, raw.Xp) : 0,
            };
            if (r.DialogueTag.Length == 0) r.DialogueTag = callsign;
            r.ApplySelection(PilotStudio.Frozen(r.Name, r.Callsign, raw.HasCustomPortrait ? raw.Selection : (PortraitSelection?)null));
            return r;
        }
    }
}
