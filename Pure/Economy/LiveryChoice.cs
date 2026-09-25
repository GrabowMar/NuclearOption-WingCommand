using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>The game's livery kinds (LiveryKey.KeyType): built in, an app-data skin folder, a workshop item.</summary>
    internal enum LiveryKind : byte { Builtin, AppData, Workshop }

    /// <summary>LOADOUT's per-airframe livery (spec WMC rebuild §LOADOUT: LIVERY is pinned and labelled per airframe), saved by what it is
    /// — "B3", "A&lt;folder&gt;", "W&lt;id&gt;" — never by its place in a list that changes with the faction and the installed skins. A token
    /// the faction no longer offers reads STANDARD (the faction's own livery). Tokens that could climb folders or make the game's parser
    /// throw are refused.</summary>
    internal static class LiveryChoice
    {
        public static string Token(LiveryKind kind, int index, string name)
        {
            switch (kind)
            {
                case LiveryKind.AppData: return "A" + name;
                case LiveryKind.Workshop: return "W" + name;
                default: return "B" + index.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static bool TryParse(string token, out LiveryKind kind, out int index, out string name)
        {
            kind = LiveryKind.Builtin;
            index = 0;
            name = null;
            if (string.IsNullOrEmpty(token) || token.Length < 2) return false;
            string rest = token.Substring(1);
            switch (token[0])
            {
                case 'B':
                    return int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out index);
                case 'A':
                    if (rest.IndexOf('/') >= 0 || rest.IndexOf('\\') >= 0 || rest.Contains("..")) return false;
                    kind = LiveryKind.AppData;
                    name = rest;
                    return true;
                case 'W':
                    if (!ulong.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
                    kind = LiveryKind.Workshop;
                    name = rest;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Where <paramref name="saved"/> sits among the offered tokens (entry 0 is STANDARD, null); 0 when it is not offered.</summary>
        public static int IndexOf(IReadOnlyList<string> offered, string saved)
        {
            if (string.IsNullOrEmpty(saved) || offered == null) return 0;
            for (int i = 1; i < offered.Count; i++)
                if (offered[i] == saved) return i;
            return 0;
        }

        public static int Step(int i, int n, int dir) => n <= 0 ? 0 : ((i + dir) % n + n) % n;

        /// <summary>The airframe → token map as "airframe|token;…" with the template codec's escaping.</summary>
        public static string Encode(IReadOnlyDictionary<string, string> map)
        {
            var sb = new StringBuilder();
            if (map == null) return "";
            foreach (KeyValuePair<string, string> kv in map)
            {
                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(LoadoutTemplateCodec.Escape(kv.Key)).Append('|').Append(LoadoutTemplateCodec.Escape(kv.Value));
            }
            return sb.ToString();
        }

        public static Dictionary<string, string> Decode(string encoded)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(encoded)) return map;
            foreach (string record in encoded.Split(';'))
            {
                int bar = record.IndexOf('|');
                if (bar <= 0 || bar == record.Length - 1) continue;
                map[LoadoutTemplateCodec.Unescape(record.Substring(0, bar))] = LoadoutTemplateCodec.Unescape(record.Substring(bar + 1));
            }
            return map;
        }
    }
}
