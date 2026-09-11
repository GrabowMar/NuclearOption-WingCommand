using System.Collections.Generic;
using System.Text;

namespace WingCommand
{
    /// <summary>Purchase fit choice or immutable snapshot of fitted stores.</summary>
    internal readonly struct WingLoadoutChoice
    {
        public readonly string TemplateId;
        public readonly IReadOnlyList<string> FittedKeys;

        public WingLoadoutChoice(string templateId = null)
        {
            TemplateId = templateId;
            FittedKeys = null;
        }

        private WingLoadoutChoice(string templateId, IEnumerable<string> fittedKeys)
        {
            TemplateId = templateId;
            FittedKeys = new List<string>(fittedKeys).AsReadOnly();
        }

        public static WingLoadoutChoice Standard => new WingLoadoutChoice(null);
        public bool IsTemplate => !string.IsNullOrEmpty(TemplateId);
        public bool HasSnapshot => FittedKeys != null;

        public WingLoadoutChoice WithTemplate(string templateId) => new WingLoadoutChoice(templateId);

        public WingLoadoutChoice Snapshot(IEnumerable<string> fittedKeys) =>
            fittedKeys == null ? this : new WingLoadoutChoice(TemplateId, fittedKeys);
    }

    /// <summary>Serializable template identity, airframe key, name, and ordered store keys. Empty keys
    /// mean bare pylons. Resolve keys only when building; unavailable stores become empty stations. Prefer
    /// jsonKey, with namespaced asset-name fallback for older mods.</summary>
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

        /// <summary>Pylon key, or null for a bare or missing entry; newly added airframe stations remain
        /// empty.</summary>
        public string KeyAt(int index) =>
            index >= 0 && index < MountKeys.Count ? MountKeys[index] : null;

        public void SetKeyAt(int index, string key)
        {
            if (index < 0) return;
            while (MountKeys.Count <= index) MountKeys.Add(null);
            MountKeys[index] = key;
        }
    }

    /// <summary>Serializes a flat template list as airframe|id|name|key1,key2,,key4 records separated by
    /// semicolons, with percent-escaped delimiters. Malformed records are dropped independently so other
    /// templates remain usable.</summary>
    internal static class LoadoutTemplateCodec
    {
        private const char RecordSeparator = ';';
        private const char FieldSeparator = '|';
        private const char KeySeparator = ',';

        /// <summary>Record fields: airframe key, stable ID, name, and pylon keys.</summary>
        private const int FieldCount = 4;

        public static string EncodeInitializedAirframes(IEnumerable<string> keys)
        {
            var encoded = new StringBuilder();
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (encoded.Length > 0) encoded.Append(RecordSeparator);
                encoded.Append(Escape(key));
            }
            return encoded.ToString();
        }

        public static HashSet<string> DecodeInitializedAirframes(string encoded)
        {
            var keys = new HashSet<string>();
            if (string.IsNullOrEmpty(encoded)) return keys;
            foreach (string chunk in encoded.Split(RecordSeparator))
                if (!string.IsNullOrEmpty(chunk)) keys.Add(Unescape(chunk));
            return keys;
        }

        public static string Encode(IEnumerable<LoadoutTemplateRecord> records)
        {
            if (records == null) return "";

            var sb = new StringBuilder();
            foreach (LoadoutTemplateRecord record in records)
            {
                if (record == null) continue;

                // Skip records lacking identity before writing undecodable data.
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

            // Limit field splitting so raw delimiters cannot shift the expected key-list position.
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

            // An empty key field means zero pylons, not one empty-name pylon.
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

        /// <summary>Escape percent before delimiters so literal percent signs cannot become escape
        /// sequences.</summary>
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

        /// <summary>Decode delimiters before percent to preserve literal escape-like text.</summary>
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
