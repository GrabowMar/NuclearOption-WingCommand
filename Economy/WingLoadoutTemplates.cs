using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;

namespace WingCommand
{
    /// <summary>Persists per-airframe pylon templates in BepInEx config across missions and restarts. Use
    /// stable definition/store keys, never prefab references. Mission-specific fitted and planned loadouts
    /// belong in WingLoadoutBook.</summary>
    internal static class WingLoadoutTemplates
    {
        /// <summary>All templates in creation order, stored as a small flat list for
        /// serialization.</summary>
        private static readonly List<LoadoutTemplateRecord> records =
            new List<LoadoutTemplateRecord>();

        private static readonly List<LoadoutTemplateRecord> scratch =
            new List<LoadoutTemplateRecord>();

        private static bool loaded;

        /// <summary>Template-name length limit for selector display.</summary>
        public const int MaxNameLength = 28;

        /// <summary>Per-airframe template limit to avoid popup pagination.</summary>
        public const int MaxPerAirframe = 8;

        private static readonly Dictionary<string, int> airframeLiveryIndices =
            new Dictionary<string, int>();

        public sealed class LiveryOption
        {
            public readonly LiveryKey? Key;
            public readonly string Name;

            public LiveryOption(LiveryKey? key, string name)
            {
                Key = key;
                Name = name;
            }
        }

        public static int GetLiveryIndex(AircraftDefinition definition)
        {
            if (definition == null) return 0;
            string key = KeyOf(definition);
            if (key != null && airframeLiveryIndices.TryGetValue(key, out int idx)) return idx;
            return 0;
        }

        public static void SetLiveryIndex(AircraftDefinition definition, int index)
        {
            if (definition == null) return;
            string key = KeyOf(definition);
            if (key != null) airframeLiveryIndices[key] = Math.Max(0, index);
        }

        public static List<LiveryOption> GetLiveries(AircraftDefinition definition, Faction faction = null)
        {
            var list = new List<LiveryOption>();
            list.Add(new LiveryOption(null, "STANDARD (FACTION)"));

            if (definition == null || definition.aircraftParameters == null) return list;

            try
            {
                var nativeList = new List<(LiveryKey key, string label)>();
                LoadoutSelector.GetLiveryOptions(nativeList, definition, faction != null ? faction.factionName : null, true);
                if (nativeList != null && nativeList.Count > 0)
                {
                    for (int i = 0; i < nativeList.Count; i++)
                    {
                        var item = nativeList[i];
                        string label = !string.IsNullOrEmpty(item.label) ? item.label : ("LIVERY " + (i + 1));
                        list.Add(new LiveryOption(item.key, label));
                    }
                    return list;
                }
            }
            catch
            {
                // Try aircraftParameters.liveries next.
            }

            AircraftParameters p = definition.aircraftParameters;
            if (p.liveries != null)
            {
                for (int i = 0; i < p.liveries.Count; i++)
                {
                    AircraftParameters.Livery l = p.liveries[i];
                    string name = l != null && !string.IsNullOrEmpty(l.name) ? l.name : ("LIVERY " + (i + 1));
                    list.Add(new LiveryOption(new LiveryKey(i), name));
                }
            }

            return list;
        }

        // Template lifecycle.

        /// <summary>Load config once, outside mission Reset, so mission changes cannot discard unsaved
        /// template edits.</summary>
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                records.Clear();
                records.AddRange(LoadoutTemplateCodec.Decode(Plugin.Settings.LoadoutTemplates.Value));
            }
            catch (Exception e)
            {
                // On a wholly unreadable value, log once and clear the list; the codec handles
                // individual invalid records.
                records.Clear();
                Plugin.Logger.LogWarning(
                    "[Loadout] saved templates could not be read and have been ignored: " +
                    e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Plugin.Settings.LoadoutTemplates.Value = LoadoutTemplateCodec.Encode(records);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Loadout] templates could not be saved: " + e.Message);
            }
        }

        // Template queries.

        /// <summary>This airframe's templates in creation order.</summary>
        public static IReadOnlyList<LoadoutTemplateRecord> For(AircraftDefinition definition)
        {
            EnsureLoaded();
            scratch.Clear();

            string key = KeyOf(definition);
            if (key == null) return scratch;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].AirframeKey == key) scratch.Add(records[i]);
            }
            return scratch;
        }

        public static int CountFor(AircraftDefinition definition)
        {
            EnsureLoaded();

            string key = KeyOf(definition);
            if (key == null) return 0;

            int count = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].AirframeKey == key) count++;
            }
            return count;
        }

        public static LoadoutTemplateRecord ById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Id == id) return records[i];
            }
            return null;
        }

        /// <summary>Resolve a template name with a fallback for deleted or unavailable IDs; purchases and
        /// recovered fits may outlive templates.</summary>
        public static string NameOf(string id)
        {
            LoadoutTemplateRecord record = ById(id);
            return record != null && !string.IsNullOrEmpty(record.Name)
                ? record.Name
                : "DELETED TEMPLATE";
        }

        public static bool Exists(string id) => ById(id) != null;

        // Template editing.

        /// <summary>Create a template from store keys; return null when the airframe lacks a key or has
        /// reached its template limit.</summary>
        public static LoadoutTemplateRecord Create(AircraftDefinition definition, string name,
                                                   IEnumerable<string> mountKeys)
        {
            EnsureLoaded();

            string key = KeyOf(definition);
            if (key == null) return null;
            if (CountFor(definition) >= MaxPerAirframe) return null;

            var record = new LoadoutTemplateRecord(NewId(), key, Clean(name), mountKeys);
            records.Add(record);
            Save();
            return record;
        }

        public static LoadoutTemplateRecord Duplicate(LoadoutTemplateRecord source)
        {
            EnsureLoaded();
            if (source == null) return null;

            int count = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].AirframeKey == source.AirframeKey) count++;
            }
            if (count >= MaxPerAirframe) return null;

            LoadoutTemplateRecord copy = source.Copy(NewId(), Clean(source.Name + " COPY"));
            records.Add(copy);
            Save();
            return copy;
        }

        public static void Delete(LoadoutTemplateRecord record)
        {
            EnsureLoaded();
            if (record == null) return;
            if (records.Remove(record)) Save();
        }

        public static void Rename(LoadoutTemplateRecord record, string name)
        {
            EnsureLoaded();
            if (record == null) return;

            string cleaned = Clean(name);
            if (record.Name == cleaned) return;

            record.Name = cleaned;
            Save();
        }

        /// <summary>Set the pylon's store key; null clears it.</summary>
        public static void SetMount(LoadoutTemplateRecord record, int pylon, string key)
        {
            EnsureLoaded();
            if (record == null || pylon < 0) return;

            if (record.KeyAt(pylon) == key) return;
            record.SetKeyAt(pylon, key);
            Save();
        }

        // Template naming.

        /// <summary>Generate the next default template name within this airframe's list.</summary>
        public static string NextDefaultName(AircraftDefinition definition)
        {
            EnsureLoaded();

            for (int n = 1; n <= MaxPerAirframe + 1; n++)
            {
                string candidate = "TEMPLATE " + n;
                if (!NameTaken(definition, candidate)) return candidate;
            }
            return "TEMPLATE";
        }

        private static bool NameTaken(AircraftDefinition definition, string name)
        {
            string key = KeyOf(definition);
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].AirframeKey == key && records[i].Name == name) return true;
            }
            return false;
        }

        /// <summary>Trim names to display/storage limits. Leave delimiter escaping to the codec so
        /// characters are not silently substituted.</summary>
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "TEMPLATE";

            string trimmed = name.Trim();
            if (trimmed.Length == 0) return "TEMPLATE";
            return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed;
        }

        private static string KeyOf(AircraftDefinition definition)
        {
            if (definition == null) return null;
            string key = definition.jsonKey;
            return string.IsNullOrEmpty(key) ? null : key;
        }

        /// <summary>Generate a stable unique ID independent of editable names, preserving purchase and
        /// recovered-fit references across renames.</summary>
        private static string NewId()
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                string id = "t" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (ById(id) == null) return id;
            }
            return "t" + Guid.NewGuid().ToString("N");
        }
    }
}
