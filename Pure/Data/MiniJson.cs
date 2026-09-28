using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>Engine-free JSON reader for mod data files. Produces Dictionary (case-insensitive keys),
    /// List, string, long, double, bool and null; tolerates // and /* */ comments; rejects nesting deeper
    /// than 64 and trailing input.</summary>
    internal static class MiniJson
    {
        public static bool TryParse(string json, out object root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var scanner = new Scanner(json);
                root = ParseValue(scanner, 0);
                if (scanner.Peek() != '\0') throw new FormatException("Unexpected trailing input.");
                return true;
            }
            catch (FormatException)
            {
                root = null;
                return false;
            }
        }

        public static string Escape(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        public static bool TryGetList(Dictionary<string, object> dict, string key, out List<object> list)
        {
            list = dict.TryGetValue(key, out object value) ? value as List<object> : null;
            return list != null;
        }

        public static string GetString(Dictionary<string, object> dict, string key) =>
            dict.TryGetValue(key, out object value) ? value?.ToString() : null;

        public static int GetInt(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (!dict.TryGetValue(key, out object value)) return defaultValue;
            switch (value)
            {
                case long l: return l >= int.MinValue && l <= int.MaxValue ? (int)l : defaultValue;
                case int i: return i;
                case double d: return d >= int.MinValue && d <= int.MaxValue ? (int)d : defaultValue;
                default:
                    return int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int parsed) ? parsed : defaultValue;
            }
        }

        private static object ParseValue(Scanner s, int depth)
        {
            if (depth >= 64) throw new FormatException("JSON nesting is too deep.");
            char c = s.Peek();
            if (c == '{') return ParseObject(s, depth + 1);
            if (c == '[') return ParseArray(s, depth + 1);
            if (c == '"') return s.ReadString();
            if (c == '\0') throw new FormatException("Unexpected end of input.");
            return s.ReadNumberOrKeyword();
        }

        private static Dictionary<string, object> ParseObject(Scanner s, int depth)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            s.Next(); // Consume the object opener.
            while (!s.IsEnd)
            {
                char c = s.Peek();
                if (c == '}')
                {
                    s.Next();
                    return dict;
                }
                if (c == ',')
                {
                    s.Next();
                    continue;
                }

                string key = s.ReadString();
                if (s.Next() != ':') throw new FormatException("Expected a colon.");
                object val = ParseValue(s, depth);
                if (!string.IsNullOrEmpty(key)) dict[key] = val;
            }
            throw new FormatException("Unterminated object.");
        }

        private static List<object> ParseArray(Scanner s, int depth)
        {
            var list = new List<object>();
            s.Next(); // Consume the array opener.
            while (!s.IsEnd)
            {
                char c = s.Peek();
                if (c == ']')
                {
                    s.Next();
                    return list;
                }
                if (c == ',')
                {
                    s.Next();
                    continue;
                }
                list.Add(ParseValue(s, depth));
            }
            throw new FormatException("Unterminated array.");
        }

        private sealed class Scanner
        {
            private readonly string source;
            private int pos;

            public Scanner(string source) => this.source = source ?? "";

            public bool IsEnd => pos >= source.Length;

            public char Peek()
            {
                SkipWhitespaceAndComments();
                return pos < source.Length ? source[pos] : '\0';
            }

            public char Next()
            {
                SkipWhitespaceAndComments();
                return pos < source.Length ? source[pos++] : '\0';
            }

            private void SkipWhitespaceAndComments()
            {
                while (pos < source.Length)
                {
                    char c = source[pos];
                    if (char.IsWhiteSpace(c))
                    {
                        pos++;
                        continue;
                    }
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '/')
                    {
                        pos += 2;
                        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r') pos++;
                        continue;
                    }
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '*')
                    {
                        pos += 2;
                        while (pos + 1 < source.Length && !(source[pos] == '*' && source[pos + 1] == '/')) pos++;
                        if (pos + 1 >= source.Length) throw new FormatException("Unterminated comment.");
                        pos += 2;
                        continue;
                    }
                    break;
                }
            }

            public string ReadString()
            {
                SkipWhitespaceAndComments();
                if (pos >= source.Length || source[pos] != '"')
                    throw new FormatException("Expected a quoted string.");
                pos++; // Consume the opening quote.

                var sb = new StringBuilder();
                while (pos < source.Length)
                {
                    char c = source[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\' || pos >= source.Length)
                    {
                        sb.Append(c);
                        continue;
                    }
                    char esc = source[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 <= source.Length && int.TryParse(source.Substring(pos, 4),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                throw new FormatException("Unterminated string.");
            }

            public object ReadNumberOrKeyword()
            {
                SkipWhitespaceAndComments();
                int start = pos;
                while (pos < source.Length && !char.IsWhiteSpace(source[pos]) &&
                       source[pos] != ',' && source[pos] != ']' && source[pos] != '}' && source[pos] != '/')
                {
                    pos++;
                }
                if (pos == start) throw new FormatException("Expected a value.");
                string token = source.Substring(start, pos - start).Trim();
                if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase)) return null;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                return token;
            }
        }
    }
}
