using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>
    /// Pure data record representing one custom pilot loaded from a file.
    /// </summary>
    internal sealed class CustomPilotRecord
    {
        public string Name { get; set; } = "UNKNOWN";
        public string Callsign { get; set; } = "PILOT";
        public string DialogueTag { get; set; }
        public ChatterPersona Persona { get; set; } = ChatterPersona.Professional;
        public string Background { get; set; } = "";
        public int Xp { get; set; }
        public int Kills { get; set; }
        public int Sorties { get; set; }

        public string ResolvedDialogueTag =>
            !string.IsNullOrWhiteSpace(DialogueTag) ? DialogueTag.Trim().ToUpperInvariant() : Callsign.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Pure data record representing a custom radio chatter line or exchange.
    /// </summary>
    internal sealed class CustomChatterRecord
    {
        public string Opening { get; set; }
        public string Reply { get; set; }
        public string SpeakerTag { get; set; }
        public string ReplyTag { get; set; }
        public string Event { get; set; }
        public string Text { get; set; }

        public bool IsAmbientExchange => !string.IsNullOrWhiteSpace(Opening);
        public bool IsEventLine => !string.IsNullOrWhiteSpace(Event) && !string.IsNullOrWhiteSpace(Text);
    }

    /// <summary>
    /// The combined payload parsed from a custom pilot file.
    /// </summary>
    internal sealed class CustomPilotPayload
    {
        public List<CustomPilotRecord> Pilots { get; } = new List<CustomPilotRecord>();
        public List<CustomChatterRecord> Chatters { get; } = new List<CustomChatterRecord>();
    }

    /// <summary>
    /// Resilient, zero-dependency JSON decoder for custom pilot and chatter files.
    /// Supports comments, missing properties, casing differences, and syntax errors.
    /// </summary>
    internal static class CustomPilotCodec
    {
        public static CustomPilotPayload Decode(string json)
        {
            var payload = new CustomPilotPayload();
            if (string.IsNullOrWhiteSpace(json)) return payload;

            object root;
            try
            {
                root = ParseJsonValue(new JsonScanner(json));
            }
            catch
            {
                return payload;
            }

            if (root is Dictionary<string, object> dict)
            {
                if (TryGetList(dict, "pilots", out List<object> pilotList))
                {
                    foreach (object item in pilotList)
                    {
                        if (item is Dictionary<string, object> pilotDict)
                        {
                            CustomPilotRecord record = ParsePilot(pilotDict);
                            if (record != null) payload.Pilots.Add(record);
                        }
                    }
                }
                else if (dict.ContainsKey("callsign") || dict.ContainsKey("name"))
                {
                    CustomPilotRecord record = ParsePilot(dict);
                    if (record != null) payload.Pilots.Add(record);
                }

                if (TryGetList(dict, "chatters", out List<object> chatterList))
                {
                    foreach (object item in chatterList)
                    {
                        if (item is Dictionary<string, object> chatterDict)
                        {
                            CustomChatterRecord chatter = ParseChatter(chatterDict);
                            if (chatter != null) payload.Chatters.Add(chatter);
                        }
                    }
                }
            }
            else if (root is List<object> list)
            {
                foreach (object item in list)
                {
                    if (item is Dictionary<string, object> pilotDict)
                    {
                        CustomPilotRecord record = ParsePilot(pilotDict);
                        if (record != null) payload.Pilots.Add(record);
                    }
                }
            }

            return payload;
        }

        public static string SampleJson()
        {
            return @"{
  ""pilots"": [
    {
      ""name"": ""Alex Mercer"",
      ""callsign"": ""GHOST"",
      ""dialogueTag"": ""GHOST"",
      ""persona"": ""Calm"",
      ""background"": ""Former high-altitude interceptor pilot with hundreds of hours in supersonic patrol. Unflappable under heavy AA fire."",
      ""xp"": 140,
      ""kills"": 3,
      ""sorties"": 5
    },
    {
      ""name"": ""Sarah Connor"",
      ""callsign"": ""VALKYRIE"",
      ""dialogueTag"": ""VALKYRIE"",
      ""persona"": ""Aggressive"",
      ""background"": ""Aggressive close air support specialist. Prefers low-level gun passes and high-G turn fights."",
      ""xp"": 260,
      ""kills"": 7,
      ""sorties"": 12
    },
    {
      ""name"": ""Marcus Vance"",
      ""callsign"": ""SPECTRE"",
      ""dialogueTag"": ""SPECTRE"",
      ""persona"": ""Dry"",
      ""background"": ""Electronic warfare technician turned frontline combat pilot. Masters ECM radar masking and terrain masking."",
      ""xp"": 50,
      ""kills"": 1,
      ""sorties"": 2
    }
  ],
  ""chatters"": [
    {
      ""speakerTag"": ""GHOST"",
      ""opening"": ""Ghost on station. Radar picture is clean."",
      ""reply"": ""Copy Ghost. Settle into the formation."",
      ""replyTag"": ""VALKYRIE""
    },
    {
      ""speakerTag"": ""VALKYRIE"",
      ""opening"": ""Bandits on scope. Let's make this quick."",
      ""reply"": ""Check your spacing, Valkyrie. We engage together."",
      ""replyTag"": ""GHOST""
    },
    {
      ""speakerTag"": ""SPECTRE"",
      ""opening"": ""Radar warning receiver is quiet. Suspiciously quiet."",
      ""reply"": ""Enjoy the silence while it lasts, Spectre."",
      ""replyTag"": ""VALKYRIE""
    },
    {
      ""event"": ""Splash"",
      ""speakerTag"": ""VALKYRIE"",
      ""text"": ""Splash one! Target eliminated, who's next?""
    },
    {
      ""event"": ""Splash"",
      ""speakerTag"": ""GHOST"",
      ""text"": ""Target confirmed destroyed. Clean shot.""
    },
    {
      ""event"": ""Bingo"",
      ""speakerTag"": ""GHOST"",
      ""text"": ""Ghost is at bingo fuel. Egressing for RTB.""
    },
    {
      ""event"": ""BreakCall"",
      ""speakerTag"": ""VALKYRIE"",
      ""text"": ""Lead, missile break break! Hard right now!""
    }
  ]
}";
        }

        private static CustomPilotRecord ParsePilot(Dictionary<string, object> dict)
        {
            string callsign = GetString(dict, "callsign");
            if (string.IsNullOrWhiteSpace(callsign)) return null;

            string name = GetString(dict, "name");
            if (string.IsNullOrWhiteSpace(name)) name = callsign;

            string tag = GetString(dict, "dialoguetag");
            if (string.IsNullOrWhiteSpace(tag)) tag = callsign.ToUpperInvariant();

            string personaStr = GetString(dict, "persona");
            ChatterPersona persona = ChatterPersona.Professional;
            if (!string.IsNullOrWhiteSpace(personaStr))
            {
                if (Enum.TryParse(personaStr, ignoreCase: true, out ChatterPersona parsed))
                    persona = parsed;
            }

            string background = GetString(dict, "background") ?? "";
            int xp = GetInt(dict, "xp", 0);
            int kills = GetInt(dict, "kills", 0);
            int sorties = GetInt(dict, "sorties", 0);

            return new CustomPilotRecord
            {
                Name = name.Trim(),
                Callsign = callsign.Trim().ToUpperInvariant(),
                DialogueTag = tag.Trim().ToUpperInvariant(),
                Persona = persona,
                Background = background.Trim(),
                Xp = Math.Max(0, xp),
                Kills = Math.Max(0, kills),
                Sorties = Math.Max(0, sorties),
            };
        }

        private static CustomChatterRecord ParseChatter(Dictionary<string, object> dict)
        {
            string opening = GetString(dict, "opening");
            string reply = GetString(dict, "reply");
            string speakerTag = GetString(dict, "speakertag");
            string replyTag = GetString(dict, "replytag");
            string eventName = GetString(dict, "event");
            string text = GetString(dict, "text");

            if (string.IsNullOrWhiteSpace(opening) && (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(text)))
                return null;

            return new CustomChatterRecord
            {
                Opening = opening?.Trim(),
                Reply = reply?.Trim(),
                SpeakerTag = speakerTag?.Trim().ToUpperInvariant(),
                ReplyTag = replyTag?.Trim().ToUpperInvariant(),
                Event = eventName?.Trim(),
                Text = text?.Trim(),
            };
        }

        private static bool TryGetList(Dictionary<string, object> dict, string key, out List<object> list)
        {
            foreach (KeyValuePair<string, object> pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (pair.Value is List<object> found)
                    {
                        list = found;
                        return true;
                    }
                }
            }
            list = null;
            return false;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            foreach (KeyValuePair<string, object> pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value?.ToString();
            }
            return null;
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int defaultValue)
        {
            foreach (KeyValuePair<string, object> pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (pair.Value is long l) return (int)l;
                    if (pair.Value is int i) return i;
                    if (pair.Value is double d) return (int)d;
                    if (int.TryParse(pair.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        return parsed;
                }
            }
            return defaultValue;
        }

        // ------------------------------------------------------------------ scanner & parser

        private sealed class JsonScanner
        {
            private readonly string source;
            private int pos;

            public JsonScanner(string source)
            {
                this.source = source ?? "";
                pos = 0;
            }

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

                    // Line comment //
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '/')
                    {
                        pos += 2;
                        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
                            pos++;
                        continue;
                    }

                    // Block comment /* ... */
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '*')
                    {
                        pos += 2;
                        while (pos + 1 < source.Length && !(source[pos] == '*' && source[pos + 1] == '/'))
                            pos++;
                        if (pos + 1 < source.Length) pos += 2;
                        continue;
                    }

                    break;
                }
            }

            public string ReadString()
            {
                SkipWhitespaceAndComments();
                if (pos >= source.Length || source[pos] != '"') return "";
                pos++; // skip opening quote

                var sb = new StringBuilder();
                while (pos < source.Length)
                {
                    char c = source[pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && pos < source.Length)
                    {
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
                                if (pos + 4 <= source.Length &&
                                    int.TryParse(source.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    sb.Append((char)code);
                                    pos += 4;
                                }
                                break;
                            default:
                                sb.Append(esc);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
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

                string token = source.Substring(start, pos - start).Trim();
                if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase)) return null;

                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;

                return token;
            }
        }

        private static object ParseJsonValue(JsonScanner s)
        {
            char c = s.Peek();
            if (c == '{') return ParseJsonObject(s);
            if (c == '[') return ParseJsonArray(s);
            if (c == '"') return s.ReadString();
            if (c == '\0') return null;
            return s.ReadNumberOrKeyword();
        }

        private static Dictionary<string, object> ParseJsonObject(JsonScanner s)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            s.Next(); // skip '{'

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
                char colon = s.Peek();
                if (colon == ':') s.Next();

                object val = ParseJsonValue(s);
                if (!string.IsNullOrEmpty(key))
                {
                    dict[key] = val;
                }
            }

            return dict;
        }

        private static List<object> ParseJsonArray(JsonScanner s)
        {
            var list = new List<object>();
            s.Next(); // skip '['

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

                object val = ParseJsonValue(s);
                list.Add(val);
            }

            return list;
        }
    }
}
