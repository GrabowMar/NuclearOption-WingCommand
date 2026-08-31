using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;

namespace WingCommand
{
    /// <summary>
    /// The player's saved loadout templates: which stores go on which pylons, per airframe,
    /// kept across missions and across restarts.
    ///
    /// This is the one piece of wing state that is deliberately not per-mission. A template
    /// is a decision about how the player likes to fly a VT-7, not a fact about the sortie
    /// they are flying now, so it lives in the BepInEx config alongside every other
    /// preference rather than in the tables <see cref="WingShop.Reset"/> clears. Everything
    /// else about a loadout — what a specific airframe is carrying, what the next
    /// requisition will carry — stays in <see cref="WingLoadoutBook"/> and still dies with
    /// the mission.
    ///
    /// Templates are keyed by <c>AircraftDefinition.jsonKey</c> and hold stable store
    /// identities, never prefab references: a config file outlives any number of game
    /// updates, and the whole store has to survive an identity it no longer recognises.
    /// </summary>
    internal static class WingLoadoutTemplates
    {
        /// <summary>
        /// Every template, in creation order, across all airframes.
        ///
        /// One flat list rather than a dictionary of lists: it is written out as one string,
        /// read back as one string, and never grows past what a person will type names for.
        /// </summary>
        private static readonly List<LoadoutTemplateRecord> records =
            new List<LoadoutTemplateRecord>();

        private static readonly List<LoadoutTemplateRecord> scratch =
            new List<LoadoutTemplateRecord>();

        private static bool loaded;

        /// <summary>Longest a template name may be, so the selector can always draw it.</summary>
        public const int MaxNameLength = 28;

        /// <summary>How many templates one airframe may have, so the popup never pages.</summary>
        public const int MaxPerAirframe = 8;

        // ------------------------------------------------------------------- lifecycle

        /// <summary>
        /// Read the config value once.
        ///
        /// Not called from <c>Reset</c>: templates outlive the mission, and re-reading on
        /// every mission end would quietly discard a template made during the last one if
        /// the write had not landed yet.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                records.Clear();
                records.AddRange(LoadoutTemplateCodec.Decode(Plugin.Config2.LoadoutTemplates.Value));
            }
            catch (Exception e)
            {
                // A config value that cannot be read at all is worth one line and an empty
                // list. The codec already drops individual bad records on its own.
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
                Plugin.Config2.LoadoutTemplates.Value = LoadoutTemplateCodec.Encode(records);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Loadout] templates could not be saved: " + e.Message);
            }
        }

        // ---------------------------------------------------------------------- queries

        /// <summary>The templates saved for one airframe, oldest first.</summary>
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

        /// <summary>
        /// The name to print for a template id.
        ///
        /// A template the player has since deleted, or one saved for a different install,
        /// still has to render as something: a purchase order or a recovered airframe can
        /// outlive the template it was fitted from.
        /// </summary>
        public static string NameOf(string id)
        {
            LoadoutTemplateRecord record = ById(id);
            return record != null && !string.IsNullOrEmpty(record.Name)
                ? record.Name
                : "DELETED TEMPLATE";
        }

        public static bool Exists(string id) => ById(id) != null;

        // ----------------------------------------------------------------- mutation

        /// <summary>
        /// Add a template for an airframe, initialized with the given store keys.
        ///
        /// Returns null when the airframe cannot be keyed or the per-airframe cap is
        /// reached, so the caller can say why rather than silently doing nothing.
        /// </summary>
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

        /// <summary>Set one pylon's store, or clear it with a null key.</summary>
        public static void SetMount(LoadoutTemplateRecord record, int pylon, string key)
        {
            EnsureLoaded();
            if (record == null || pylon < 0) return;

            if (record.KeyAt(pylon) == key) return;
            record.SetKeyAt(pylon, key);
            Save();
        }

        // --------------------------------------------------------------------- naming

        /// <summary>
        /// A name for a template the player has not named yet.
        ///
        /// Numbered per airframe rather than globally, because the selector only ever shows
        /// one airframe's templates and "TEMPLATE 7" among three of them reads as a bug.
        /// </summary>
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

        /// <summary>
        /// Trim a name to something the panel can draw and the config can hold.
        ///
        /// The delimiters are escaped by the codec rather than stripped here, so a name is
        /// only ever shortened, never silently rewritten into different characters.
        /// </summary>
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

        /// <summary>
        /// A short unique id.
        ///
        /// Not the name: names are edited, and a purchase order or a recovered airframe
        /// holding a template id must not follow a rename into a different template or lose
        /// track of the one it was fitted from.
        /// </summary>
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
