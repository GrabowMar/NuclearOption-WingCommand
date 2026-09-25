using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>LOADOUT's saved templates and liveries (spec WMC rebuild §LOADOUT), kept in the BepInEx config across missions: templates
    /// per airframe by stable id (names through <see cref="TemplateNames"/>, five per airframe), and one livery per airframe saved as a
    /// token (<see cref="LiveryChoice"/>) and resolved per faction at each spawn — the saved one while the faction still offers it, else
    /// the faction's own random livery, as the game's AI wears. <see cref="Revision"/> moves on every change.</summary>
    internal static class WingLoadoutTemplates
    {
        private static readonly List<LoadoutTemplateRecord> records = new List<LoadoutTemplateRecord>();
        private static readonly List<LoadoutTemplateRecord> scratch = new List<LoadoutTemplateRecord>();
        private static readonly List<string> names = new List<string>();
        private static readonly List<(LiveryKey key, string label)> nativeScratch = new List<(LiveryKey, string)>();
        private static readonly List<LiveryOption> spawnScratch = new List<LiveryOption>();
        private static Dictionary<string, string> liveries = new Dictionary<string, string>();
        private static bool loaded;

        public static int MaxPerAirframe => TemplateNames.PerAirframe;

        /// <summary>Moves on every saved change (templates and liveries): pages rebuild only when it does.</summary>
        public static int Revision { get; private set; }

        /// <summary>One livery the faction may wear: STANDARD (no token) first.</summary>
        public struct LiveryOption
        {
            public LiveryKey Key;
            public string Label, Token;
        }

        // ---- liveries

        /// <summary>STANDARD, then the liveries <paramref name="faction"/> may wear (the game's own list). Without a faction only STANDARD:
        /// the game would list every faction's paint. The first call scans the skin folders; callers cache per airframe.</summary>
        public static void Liveries(AircraftDefinition definition, Faction faction, List<LiveryOption> into)
        {
            into.Clear();
            into.Add(new LiveryOption { Label = LoadoutWords.Standard });
            if (definition == null || definition.aircraftParameters == null || faction == null) return;
            try
            {
                nativeScratch.Clear();
                LoadoutSelector.GetLiveryOptions(nativeScratch, definition, faction.factionName, true);
                foreach ((LiveryKey key, string label) in nativeScratch)
                    into.Add(new LiveryOption
                    {
                        Key = key, Token = TokenOf(key), Label = LoadoutWords.Livery(string.IsNullOrEmpty(label) ? "LIVERY " + into.Count : label),
                    });
            }
            catch (Exception e)
            {
                Plugin.LogVerbose("[Loadout] liveries for " + definition.unitName + " could not be listed: " + e.Message);
            }
        }

        public static string TokenOf(LiveryKey key)
        {
            key.Save(out LiveryKey.KeyType type, out int index, out string name);
            return LiveryChoice.Token((LiveryKind)(byte)type, index, name);
        }

        public static string LiveryTokenOf(AircraftDefinition definition)
        {
            EnsureLoaded();
            string key = KeyOf(definition);
            return key != null && liveries.TryGetValue(key, out string token) ? token : null;
        }

        /// <summary>Saves the airframe's livery; null is STANDARD.</summary>
        public static void SetLivery(AircraftDefinition definition, string token)
        {
            EnsureLoaded();
            string key = KeyOf(definition);
            if (key == null || LiveryTokenOf(definition) == token) return;
            if (token == null) liveries.Remove(key);
            else liveries[key] = token;
            try
            {
                Plugin.Settings.LoadoutLiveries.Value = LiveryChoice.Encode(liveries);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Loadout] liveries could not be saved: " + e.Message);
            }
            Revision++;
        }

        /// <summary>What a launch of <paramref name="definition"/> for <paramref name="hq"/> wears: the saved livery while the faction still
        /// offers it, else the faction's random one (a fresh pick per aircraft, as the game's AI).</summary>
        public static LiveryKey LiveryFor(AircraftDefinition definition, FactionHQ hq)
        {
            Faction faction = hq != null ? hq.faction : null;
            string token = LiveryTokenOf(definition);
            if (token != null && faction != null)
            {
                Liveries(definition, faction, spawnScratch);
                foreach (LiveryOption o in spawnScratch)
                    if (o.Token == token) return o.Key;
            }
            AircraftParameters p = definition != null ? definition.aircraftParameters : null;
            return p != null && faction != null ? new LiveryKey(p.GetRandomLiveryForFaction(faction)) : default;
        }

        // ---- lifecycle

        /// <summary>Loads the config once (outside the mission reset, so a mission change never loses an edit).</summary>
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
                records.Clear();
                Plugin.Logger.LogWarning("[Loadout] saved templates could not be read and have been ignored: " + e.Message);
            }
            try
            {
                liveries = LiveryChoice.Decode(Plugin.Settings.LoadoutLiveries.Value);
            }
            catch (Exception e)
            {
                liveries = new Dictionary<string, string>();
                Plugin.Logger.LogWarning("[Loadout] saved liveries could not be read and have been ignored: " + e.Message);
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
            Revision++;
        }

        // ---- queries

        /// <summary>This airframe's templates in creation order (a shared list: read it at once, never keep it).</summary>
        public static IReadOnlyList<LoadoutTemplateRecord> For(AircraftDefinition definition)
        {
            EnsureLoaded();
            scratch.Clear();
            string key = KeyOf(definition);
            if (key == null) return scratch;
            foreach (LoadoutTemplateRecord r in records)
                if (r.AirframeKey == key) scratch.Add(r);
            return scratch;
        }

        public static int CountFor(AircraftDefinition definition)
        {
            EnsureLoaded();
            string key = KeyOf(definition);
            int count = 0;
            if (key == null) return 0;
            foreach (LoadoutTemplateRecord r in records)
                if (r.AirframeKey == key) count++;
            return count;
        }

        public static LoadoutTemplateRecord ById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (LoadoutTemplateRecord r in records)
                if (r.Id == id) return r;
            return null;
        }

        /// <summary>A template's name, or "DELETED TEMPLATE" for an id that outlived it.</summary>
        public static string NameOf(string id)
        {
            LoadoutTemplateRecord record = ById(id);
            return record != null && !string.IsNullOrEmpty(record.Name) ? record.Name : "DELETED TEMPLATE";
        }

        public static bool Exists(string id) => ById(id) != null;

        // ---- editing

        /// <summary>A new template (null at the limit or for an airframe without a key); a blank name takes the first free
        /// TEMPLATE n, a taken one the next free number.</summary>
        public static LoadoutTemplateRecord Create(AircraftDefinition definition, string name, IEnumerable<string> mountKeys)
        {
            EnsureLoaded();
            string key = KeyOf(definition);
            if (key == null || CountFor(definition) >= MaxPerAirframe) return null;
            NamesOf(key, null);
            string clean = TemplateNames.Clean(name) ?? TemplateNames.NextDefault(names);
            var record = new LoadoutTemplateRecord(NewId(), key, TemplateNames.Unique(clean, names), mountKeys);
            records.Add(record);
            Save();
            return record;
        }

        /// <summary>A copy under a new id and the next free "NAME n" (null at the limit).</summary>
        public static LoadoutTemplateRecord Duplicate(LoadoutTemplateRecord source)
        {
            EnsureLoaded();
            if (source == null) return null;
            NamesOf(source.AirframeKey, null);
            if (names.Count >= MaxPerAirframe) return null;
            LoadoutTemplateRecord copy = source.Copy(NewId(), TemplateNames.Unique(source.Name ?? "TEMPLATE", names));
            records.Add(copy);
            Save();
            return copy;
        }

        public static void Delete(LoadoutTemplateRecord record)
        {
            EnsureLoaded();
            if (record != null && records.Remove(record)) Save();
        }

        /// <summary>Renames by what was typed (cleaned; a blank keeps the name; a taken one is numbered); false when nothing changed.</summary>
        public static bool Rename(LoadoutTemplateRecord record, string typed)
        {
            EnsureLoaded();
            string clean = TemplateNames.Clean(typed);
            if (record == null || clean == null) return false;
            NamesOf(record.AirframeKey, record);
            string name = TemplateNames.Unique(clean, names);
            if (name == record.Name) return false;
            record.Name = name;
            Save();
            return true;
        }

        /// <summary>Writes every set's store at once (a station pick writes its pair and empties the stations it blocks).</summary>
        public static void SetMounts(LoadoutTemplateRecord record, IReadOnlyList<string> keys)
        {
            EnsureLoaded();
            if (record == null || keys == null) return;
            bool changed = record.MountKeys.Count != keys.Count;
            for (int i = 0; i < keys.Count && !changed; i++) changed = record.KeyAt(i) != keys[i];
            if (!changed) return;
            record.MountKeys.Clear();
            record.MountKeys.AddRange(keys);
            Save();
        }

        private static void NamesOf(string airframe, LoadoutTemplateRecord except)
        {
            names.Clear();
            foreach (LoadoutTemplateRecord r in records)
                if (r.AirframeKey == airframe && !ReferenceEquals(r, except) && r.Name != null) names.Add(r.Name);
        }

        private static string KeyOf(AircraftDefinition definition) =>
            definition != null && !string.IsNullOrEmpty(definition.jsonKey) ? definition.jsonKey : null;

        /// <summary>A stable id independent of the editable name, so SUPPLY's fit survives a rename.</summary>
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
