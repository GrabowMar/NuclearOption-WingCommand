using System;
using System.Collections.Generic;
using System.IO;

namespace WingCommand
{
    /// <summary>The saved pilots on disk (R7): <c>v1/pilots.user.json</c> (scenarios use <c>pilots.sim.json</c>), read at every mission
    /// start and written whole through <see cref="AtomicFile"/> by the studio, the interop and the mission-end tally. A write that fails
    /// puts the store back as it was, so nothing shows SAVED that is not on disk.</summary>
    internal static class WingSavedPilots
    {
        public const string UserFile = "pilots.user.json", SimFile = "pilots.sim.json", ExportFile = "exported_pilots.json";

        private static SavedPilotStore store;
        private static bool scratch, tallied;

        public static string FilePath => Path.Combine(WingConfig.DataRoot, scratch ? SimFile : UserFile);

        /// <summary>Where EXPORT writes and IMPORT reads first (FOLDER opens it).</summary>
        public static string Folder => Path.Combine(WingConfig.DataRoot, "Pilots");

        public static SavedPilotStore Store => store ?? Load();

        /// <summary>Reads the file (a missing file is an empty squadron; problems are logged, never thrown).</summary>
        public static SavedPilotStore Load()
        {
            var problems = new List<string>();
            string json = null;
            try
            {
                if (File.Exists(FilePath)) json = File.ReadAllText(FilePath);
            }
            catch (Exception e)
            {
                problems.Add(e.Message);
            }
            store = SavedPilotStore.FromJson(json, problems);
            foreach (string p in problems) Plugin.Logger.LogWarning("[Pilots] " + Path.GetFileName(FilePath) + ": " + p);
            WingPilotRoster.SavedTaken = c => store != null && store.Find(c) != null;
            tallied = false;
            return store;
        }

        /// <summary>A saved callsign (the studio's SAVED / NOT SAVED).</summary>
        public static bool IsSaved(string callsign) => Store.Find(callsign) != null;

        /// <summary>Saves a draft (a rename when <paramref name="original"/> names a saved pilot), carries it onto <paramref name="live"/>
        /// (the pilot this mission it is, if any), then writes the file. False with the reason; the store is unchanged then.</summary>
        public static bool Save(CustomPilotRecord draft, string original, WingPilot live, out string why)
        {
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            string target = draft != null ? PilotText.Callsign(draft.Callsign) : null;
            if (!s.Save(draft, original, c => LiveUnsaved(c, live), out why)) return false;
            if (!Write())
            {
                store = SavedPilotStore.FromJson(backup, null);
                why = StudioWords.SaveFailed;
                return false;
            }
            CustomPilotRecord saved = s.Find(target);
            if (live != null && saved != null) WingPilotRoster.UpdateIdentity(live, saved);
            WingPilotRoster.Touch();
            return true;
        }

        public static bool Delete(string callsign, out string why)
        {
            why = null;
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            if (!s.Remove(callsign))
            {
                why = callsign + " is not a saved pilot.";
                return false;
            }
            if (Write())
            {
                WingPilotRoster.Touch();
                return true;
            }
            store = SavedPilotStore.FromJson(backup, null);
            why = StudioWords.SaveFailed;
            return false;
        }

        /// <summary>IMPORT: pilots from <see cref="Folder"/> and 0.9's <c>config/WingCommand/Pilots</c>, only callsigns not saved yet and
        /// not flying this mission unsaved, each with an empty service record. Source files are never touched.</summary>
        public static void Import(out int added, out int skipped, out int files)
        {
            var incoming = new List<CustomPilotRecord>();
            files = 0;
            foreach (string dir in new[] { Folder, WingCustomPilots.LegacyDirectory })
            {
                incoming.AddRange(WingCustomPilots.LoadFolder(dir, out int n));
                files += n;
            }
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            s.Import(incoming, c => LiveUnsaved(c, null), out added, out skipped);
            if (added > 0 && !Write())
            {
                store = SavedPilotStore.FromJson(backup, null);
                skipped += added;
                added = 0;
            }
            if (added > 0) WingPilotRoster.Touch();
        }

        /// <summary>EXPORT: every saved pilot to <c>Pilots/exported_pilots.json</c> (IMPORT and 0.9 both read it).</summary>
        public static bool Export(out int pilots)
        {
            pilots = Store.Records.Count;
            bool ok = AtomicFile.WriteAllText(Path.Combine(Folder, ExportFile), CustomPilotCodec.Encode(Store.Records), out string error);
            if (!ok) Plugin.Logger.LogWarning("[Pilots] could not export: " + error);
            return ok;
        }

        /// <summary>The mission's end (WingService.Deactivate): each saved pilot who sat in an aircraft adds a mission, its sorties and
        /// kills, and keeps its best XP. Once per mission.</summary>
        public static void Tally()
        {
            if (tallied || store == null) return;
            tallied = true;
            var roster = new List<WingPilot>();
            WingPilotRoster.Roster(roster);
            var lines = new List<MissionLine>(roster.Count);
            foreach (WingPilot p in roster) lines.Add(MissionLine.Of(p.Callsign, p.LastAircraft != null, p.Sorties, p.Kills, p.Xp));
            if (store.Tally(lines) && Write()) Plugin.LogVerbose("[Pilots] service records written");
        }

        /// <summary>Scenario hygiene: a fresh <c>pilots.sim.json</c>, so a scenario never touches the player's saved pilots.</summary>
        public static void UseScratch()
        {
            scratch = true;
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Pilots] could not clear " + SimFile + ": " + e.Message);
            }
            Load();
        }

        public static void OpenFolder() => WingCustomPilots.OpenFolder(Folder);

        /// <summary>A callsign flying this mission unsaved (other than <paramref name="self"/>): a save or import must not take it.</summary>
        private static bool LiveUnsaved(string callsign, WingPilot self)
        {
            WingPilot p = WingPilotRoster.FindByCallsign(callsign);
            return p != null && !ReferenceEquals(p, self) && (store == null || store.Find(callsign) == null);
        }

        private static bool Write()
        {
            bool ok = AtomicFile.WriteAllText(FilePath, Store.ToJson(), out string error);
            if (!ok) Plugin.Logger.LogWarning("[Pilots] could not write " + Path.GetFileName(FilePath) + ": " + error);
            return ok;
        }
    }
}
