using System.Collections.Generic;
using System.Text;

namespace WingCommand
{
    /// <summary>
    /// One saved loadout template, in the only form that can be written to a config file:
    /// strings.
    ///
    /// Nothing here is resolved against the game. A template is an airframe key, a name, and
    /// one store key per hardpoint set in the order the airframe declares them — an empty
    /// slot meaning a bare pylon, which is a legal thing for an aircraft to launch with. The
    /// keys stay unresolved until a requisition is actually built, so a template written
    /// against a store this game build no longer ships degrades to an empty pylon rather
    /// than refusing to load.
    ///
    /// This is the same shape the game uses for its own saved loadouts: a flat ordered list
    /// of mount identities. These are normally <c>jsonKey</c>s; older workshop stores that
    /// omit one use a namespaced ScriptableObject asset name.
    /// </summary>
    internal sealed class LoadoutTemplateRecord
    {
        public string Id;
        public string AirframeKey;
        public string Name;
        public readonly List<string> MountKeys = new List<string>();

        public LoadoutTemplateRecord()
        {
        }

        public LoadoutTemplateRecord(string id, string airframeKey, string name,
                                     IEnumerable<string> mountKeys)
        {
            Id = id;
            AirframeKey = airframeKey;
            Name = name;
            if (mountKeys != null) MountKeys.AddRange(mountKeys);
        }

        public LoadoutTemplateRecord Copy(string newId, string newName) =>
            new LoadoutTemplateRecord(newId, AirframeKey, newName, MountKeys);

        /// <summary>The key on one pylon, or null for a bare one and for a pylon this
        /// template is too short to describe — an airframe gaining a station between
        /// versions should leave that station empty, not throw.</summary>
        public string KeyAt(int index) =>
            index >= 0 && index < MountKeys.Count ? MountKeys[index] : null;

        public void SetKeyAt(int index, string key)
        {
            if (index < 0) return;
            while (MountKeys.Count <= index) MountKeys.Add(null);
            MountKeys[index] = key;
        }
    }

    /// <summary>
    /// Reads and writes the whole template list as one config string.
    ///
    /// A hand-rolled encoding rather than JSON, because the mod references no serialiser and
    /// this payload does not justify adding one: it is a list of short strings with no
    /// nesting. The format is <c>airframe|id|name|key1,key2,,key4</c> per record, records
    /// separated by <c>;</c>, with the delimiters percent-escaped inside any field.
    ///
    /// Decoding is deliberately total. Every failure a config file can present — a truncated
    /// string, a record with the wrong field count, a stray delimiter someone typed in by
    /// hand — drops that one record and keeps the rest. The alternative is a panel that will
    /// not open because one line of a text file is wrong.
    /// </summary>
    internal static class LoadoutTemplateCodec
    {
        private const char RecordSeparator = ';';
        private const char FieldSeparator = '|';
        private const char KeySeparator = ',';

        /// <summary>Fields per record: airframe, id, name, keys.</summary>
        private const int FieldCount = 4;

        public static string Encode(IEnumerable<LoadoutTemplateRecord> records)
        {
            if (records == null) return "";

            var sb = new StringBuilder();
            foreach (LoadoutTemplateRecord record in records)
            {
                if (record == null) continue;

                // A record with no identity cannot be read back as anything, so it is
                // dropped here rather than written out to fail decoding later.
                if (string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.AirframeKey))
                    continue;

                if (sb.Length > 0) sb.Append(RecordSeparator);
                sb.Append(Escape(record.AirframeKey)).Append(FieldSeparator);
                sb.Append(Escape(record.Id)).Append(FieldSeparator);
                sb.Append(Escape(record.Name ?? "")).Append(FieldSeparator);

                for (int i = 0; i < record.MountKeys.Count; i++)
                {
                    if (i > 0) sb.Append(KeySeparator);
                    sb.Append(Escape(record.MountKeys[i] ?? ""));
                }
            }
            return sb.ToString();
        }

        public static List<LoadoutTemplateRecord> Decode(string encoded)
        {
            var records = new List<LoadoutTemplateRecord>();
            if (string.IsNullOrEmpty(encoded)) return records;

            string[] chunks = encoded.Split(RecordSeparator);
            for (int i = 0; i < chunks.Length; i++)
            {
                LoadoutTemplateRecord record = DecodeRecord(chunks[i]);
                if (record != null) records.Add(record);
            }
            return records;
        }

        private static LoadoutTemplateRecord DecodeRecord(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return null;

            // Capped split: a name that somehow still holds a raw separator must not be able
            // to shift the key list into the name's place.
            string[] fields = chunk.Split(new[] { FieldSeparator }, FieldCount);
            if (fields.Length != FieldCount) return null;

            string airframe = Unescape(fields[0]);
            string id = Unescape(fields[1]);
            if (string.IsNullOrEmpty(airframe) || string.IsNullOrEmpty(id)) return null;

            var record = new LoadoutTemplateRecord
            {
                AirframeKey = airframe,
                Id = id,
                Name = Unescape(fields[2]),
            };

            // An empty key field is a template with no pylons, not one pylon named "" —
            // which is what a bare Split would produce.
            if (fields[3].Length > 0)
            {
                string[] keys = fields[3].Split(KeySeparator);
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = Unescape(keys[i]);
                    record.MountKeys.Add(key.Length == 0 ? null : key);
                }
            }

            return record;
        }

        /// <summary>
        /// Percent-escape the delimiters, and the escape character itself first, so that
        /// unescaping cannot mistake a literal percent for the start of a sequence.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            if (value.IndexOf('%') < 0 &&
                value.IndexOf(RecordSeparator) < 0 &&
                value.IndexOf(FieldSeparator) < 0 &&
                value.IndexOf(KeySeparator) < 0)
                return value;

            return value
                .Replace("%", "%25")
                .Replace(";", "%3B")
                .Replace("|", "%7C")
                .Replace(",", "%2C");
        }

        /// <summary>Undo <see cref="Escape"/>, percent last for the same reason.</summary>
        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOf('%') < 0) return value;

            return value
                .Replace("%2C", ",")
                .Replace("%7C", "|")
                .Replace("%3B", ";")
                .Replace("%25", "%");
        }
    }
}
