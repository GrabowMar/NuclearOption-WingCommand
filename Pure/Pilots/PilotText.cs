using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>What a saved pilot's text may hold (research squadron-studio §2.1; critic §5 R7 1): the MFD font shows printable ASCII,
    /// so accented letters lose their marks (Polish ł and ß-like letters by table), whitespace collapses in single-line fields, and
    /// everything else is dropped at the source.</summary>
    internal static class PilotText
    {
        public const int CallsignChars = 14, NameChars = 24, TagChars = 24, BioChars = 280;

        /// <summary>Trimmed, upper case, letters, digits, hyphens and single spaces, at most 14; never ends in a space or hyphen.</summary>
        public static string Callsign(string s)
        {
            string ascii = Ascii(s, false).ToUpperInvariant();
            var sb = new StringBuilder(ascii.Length);
            foreach (char c in ascii)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-') sb.Append(c);
                else if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return Cap(sb.ToString(), CallsignChars).Trim(' ', '-');
        }

        public static string Name(string s) => Cap(Collapse(Ascii(s, false)), NameChars).Trim();

        public static string Tag(string s) => Cap(Collapse(Ascii(s, false)).ToUpperInvariant(), TagChars).Trim();

        /// <summary>Lines kept, other control characters as spaces, at most 280.</summary>
        public static string Bio(string s) => Cap(Ascii(s, true), BioChars).TrimEnd();

        /// <summary>Printable ASCII (plus newlines in a bio): marks stripped, "…" as "...", tabs and other controls as spaces.</summary>
        private static string Ascii(string s, bool lines)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (c == '\n') sb.Append(lines ? '\n' : ' ');
                else if (c >= ' ' && c <= '~') sb.Append(c);
                else if (c < ' ' || c == '\u007f') sb.Append(' ');
                else if (c == '…') sb.Append("...");
                else if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                else
                {
                    string t = Table(c);
                    if (t != null) sb.Append(t);
                }
            }
            return sb.ToString().Trim(' ');
        }

        /// <summary>Letters with no decomposition: Polish ł, and a few common neighbours.</summary>
        private static string Table(char c)
        {
            switch (c)
            {
                case 'ł': return "l";
                case 'Ł': return "L";
                case 'ß': return "ss";
                case 'ø': return "o";
                case 'Ø': return "O";
                case 'đ': return "d";
                case 'Đ': return "D";
                case 'æ': return "ae";
                case 'Æ': return "AE";
                default: return null;
            }
        }

        private static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string Cap(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
