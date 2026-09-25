using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>The HANGAR store's words (user decision 2026-09-25: the wing's airframe store is the HANGAR, with STORE and a
    /// two-press RETURN; RESERVE stays a doctrine word). What may be stored or returned is <see cref="ShopRules.StoreBlock"/> /
    /// <see cref="ShopRules.ReturnBlock"/>.</summary>
    internal static class HangarWords
    {
        public const int CaptionChars = 22;

        public static string Label(int held, int capacity, bool offline) => "HANGAR " + Value(held, capacity, offline);

        public static string Value(int held, int capacity, bool offline) => offline ? WmcText.Unknown : N(held) + "/" + N(capacity);

        public static float Level(int held, int capacity) =>
            capacity <= 0 || held <= 0 ? 0f : held >= capacity ? 1f : (float)held / capacity;

        public static bool Full(int held, int capacity) => held >= capacity;

        public static string ReturnLabel(bool asking) => asking ? "RETURN?" : "RETURN";

        public static string Ask(string type) => "Return " + type + " to the faction's stock? Press RETURN again";

        public static string Stored(string type, int held, int capacity) => type + " stored in the HANGAR (" + N(held) + "/" + N(capacity) + ")";

        public static string Returned(string type, int held, int capacity) =>
            type + " returned to the faction (" + N(held) + "/" + N(capacity) + ")";

        /// <summary>The HANGAR tile's caption: the stored types ("FS-20 ×2 · A-19") within 22 characters, the rest as "+n".</summary>
        public static string Caption(IReadOnlyList<string> codes, bool host, bool faction)
        {
            if (!host) return "HOST ONLY";
            if (!faction) return "NO FACTION";
            if (codes == null || codes.Count == 0) return "NONE STORED";
            string s = "";
            int shown = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (SeenBefore(codes, i)) continue;
                int n = CountOf(codes, codes[i]);
                string part = n > 1 ? codes[i] + " ×" + N(n) : codes[i];
                string next = s.Length == 0 ? part : s + " · " + part;
                int rest = codes.Count - shown - n;
                if (next.Length + (rest > 0 ? 2 + N(rest).Length : 0) > CaptionChars)
                    return s.Length == 0 ? WmcText.Cut(part, CaptionChars) : s + " +" + N(codes.Count - shown);
                s = next;
                shown += n;
            }
            return s;
        }

        private static bool SeenBefore(IReadOnlyList<string> codes, int i)
        {
            for (int j = 0; j < i; j++)
                if (codes[j] == codes[i]) return true;
            return false;
        }

        private static int CountOf(IReadOnlyList<string> codes, string code)
        {
            int n = 0;
            for (int i = 0; i < codes.Count; i++)
                if (codes[i] == code) n++;
            return n;
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
