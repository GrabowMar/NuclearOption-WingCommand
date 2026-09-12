using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>Decoded custom-pilot data.</summary>
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
        public int PortraitVersion { get; set; } = 2;
        public PortraitBody Body { get; set; } = PortraitBody.Male;
        public int Face { get; set; } = -1;
        public int Hair { get; set; }
        public int Uniform { get; set; }
        public int Accessory { get; set; }
        public int Backdrop { get; set; }

        public bool HasCustomPortrait => Face >= 0;

        /// <summary>Canonical v2 selection. This is safe to persist and pass through live roster state.</summary>
        public PortraitSelection Selection => PilotPortraitGenerator.Normalize(
            new PortraitSelection(Body, Face, Hair, Uniform, Accessory, Backdrop));

        public void ApplySelection(PortraitSelection selection)
        {
            selection = PilotPortraitGenerator.Normalize(selection);
            PortraitVersion = 2;
            Body = selection.Body;
            Face = selection.Face;
            Hair = selection.Hair;
            Uniform = selection.Uniform;
            Accessory = selection.Accessory;
            Backdrop = selection.Backdrop;
        }

        public string ResolvedDialogueTag =>
            !string.IsNullOrWhiteSpace(DialogueTag) ? DialogueTag.Trim().ToUpperInvariant() : Callsign.Trim().ToUpperInvariant();

        public CustomPilotRecord Clone(string newCallsign = null)
        {
            return new CustomPilotRecord
            {
                Name = Name,
                Callsign = newCallsign ?? Callsign,
                DialogueTag = DialogueTag,
                Persona = Persona,
                Background = Background,
                Xp = Xp,
                Kills = Kills,
                Sorties = Sorties,
                PortraitVersion = PortraitVersion,
                Body = Body,
                Face = Face,
                Hair = Hair,
                Uniform = Uniform,
                Accessory = Accessory,
                Backdrop = Backdrop,
            };
        }
    }

    /// <summary>Decoded custom radio line or exchange.</summary>
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

    /// <summary>Pilots and chatter decoded from one file.</summary>
    internal sealed class CustomPilotPayload
    {
        public List<CustomPilotRecord> Pilots { get; } = new List<CustomPilotRecord>();
        public List<CustomChatterRecord> Chatters { get; } = new List<CustomChatterRecord>();
    }

    /// <summary>Dependency-free custom-pilot JSON decoder accepting comments, optional fields, and case
    /// variations; malformed input is ignored.</summary>
    internal static class CustomPilotCodec
    {
        public static CustomPilotPayload Decode(string json)
        {
            var payload = new CustomPilotPayload();
            if (string.IsNullOrWhiteSpace(json)) return payload;

            object root;
            try
            {
                var scanner = new JsonScanner(json);
                root = ParseJsonValue(scanner);
                if (scanner.Peek() != '\0') throw new FormatException("Unexpected trailing input.");
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
    },
    {
      ""event"": ""Damaged"",
      ""speakerTag"": ""SPECTRE"",
      ""text"": ""Damage to port avionics. ECM remains operational.""
    },
    {
      ""event"": ""Maneuvering"",
      ""speakerTag"": ""VALKYRIE"",
      ""text"": ""Executing {0}. Keep your eyes open, Lead!""
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

            int face = GetInt(dict, "face", -1);
            int hair = GetInt(dict, "hair", -1);
            int uniform = GetInt(dict, "uniform", -1);
            int accessory = GetInt(dict, "accessory", 0);
            int backdrop = GetInt(dict, "backdrop", -1);
            int portraitVersion = GetInt(dict, "portraitVersion", 0);
            PortraitBody body = string.Equals(GetString(dict, "body"), "female", StringComparison.OrdinalIgnoreCase)
                ? PortraitBody.Female
                : PortraitBody.Male;

            var record = new CustomPilotRecord
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

            if (face >= 0)
            {
                PortraitSelection selection = portraitVersion >= 2
                    ? new PortraitSelection(body, face, hair, uniform, accessory, backdrop)
                    : PilotPortraitGenerator.FromLegacySelection(face, hair, uniform, backdrop);
                record.ApplySelection(selection);
            }
            return record;
        }

        public static string Encode(IEnumerable<CustomPilotRecord> pilots, IEnumerable<CustomChatterRecord> chatters = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"pilots\": [");

            bool firstPilot = true;
            if (pilots != null)
            {
                foreach (CustomPilotRecord p in pilots)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Callsign)) continue;
                    if (!firstPilot) sb.AppendLine(",");
                    firstPilot = false;

                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": \"{EscapeJson(p.Name)}\",");
                    sb.AppendLine($"      \"callsign\": \"{EscapeJson(p.Callsign)}\",");
                    sb.AppendLine($"      \"dialogueTag\": \"{EscapeJson(p.ResolvedDialogueTag)}\",");
                    sb.AppendLine($"      \"persona\": \"{p.Persona}\",");
                    sb.AppendLine($"      \"background\": \"{EscapeJson(p.Background)}\",");
                    sb.AppendLine($"      \"xp\": {p.Xp},");
                    sb.AppendLine($"      \"kills\": {p.Kills},");
                    sb.Append($"      \"sorties\": {p.Sorties}");

                    if (p.HasCustomPortrait)
                    {
                        PortraitSelection selection = p.Selection;
                        sb.AppendLine(",");
                        sb.AppendLine("      \"portraitVersion\": 2,");
                        sb.AppendLine($"      \"body\": \"{PilotPortraitGenerator.BodyLabel(selection.Body).ToLowerInvariant()}\",");
                        sb.AppendLine($"      \"face\": {selection.Face},");
                        sb.AppendLine($"      \"hair\": {selection.Hair},");
                        sb.AppendLine($"      \"uniform\": {selection.Uniform},");
                        sb.Append($"      \"backdrop\": {selection.Backdrop}");
                    }
                    sb.AppendLine();
                    sb.Append("    }");
                }
            }
            sb.AppendLine();
            sb.Append("  ]");

            if (chatters != null)
            {
                bool anyChatter = false;
                var chatterSb = new StringBuilder();
                foreach (CustomChatterRecord c in chatters)
                {
                    if (c == null) continue;
                    if (anyChatter) chatterSb.AppendLine(",");
                    anyChatter = true;

                    chatterSb.AppendLine("    {");
                    if (c.IsAmbientExchange)
                    {
                        chatterSb.AppendLine($"      \"speakerTag\": \"{EscapeJson(c.SpeakerTag)}\",");
                        chatterSb.AppendLine($"      \"opening\": \"{EscapeJson(c.Opening)}\",");
                        chatterSb.AppendLine($"      \"reply\": \"{EscapeJson(c.Reply)}\",");
                        chatterSb.Append($"      \"replyTag\": \"{EscapeJson(c.ReplyTag)}\"");
                    }
                    else if (c.IsEventLine)
                    {
                        chatterSb.AppendLine($"      \"event\": \"{EscapeJson(c.Event)}\",");
                        chatterSb.AppendLine($"      \"speakerTag\": \"{EscapeJson(c.SpeakerTag)}\",");
                        chatterSb.Append($"      \"text\": \"{EscapeJson(c.Text)}\"");
                    }
                    chatterSb.AppendLine();
                    chatterSb.Append("    }");
                }

                if (anyChatter)
                {
                    sb.AppendLine(",");
                    sb.AppendLine("  \"chatters\": [");
                    sb.Append(chatterSb.ToString());
                    sb.AppendLine();
                    sb.Append("  ]");
                }
            }

            sb.AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string EncodeSingle(CustomPilotRecord pilot)
        {
            if (pilot == null) return "";
            return Encode(new[] { pilot });
        }

        public static string RemovePilot(string json, string callsign, out bool removed)
        {
            CustomPilotPayload payload = Decode(json);
            removed = payload.Pilots.RemoveAll(p =>
                string.Equals(p.Callsign, callsign, StringComparison.OrdinalIgnoreCase)) > 0;
            return removed ? Encode(payload.Pilots, payload.Chatters) : json;
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
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
                    if (pair.Value is long l)
                        return l >= int.MinValue && l <= int.MaxValue ? (int)l : defaultValue;
                    if (pair.Value is int i) return i;
                    if (pair.Value is double d)
                        return d >= int.MinValue && d <= int.MaxValue ? (int)d : defaultValue;
                    if (int.TryParse(pair.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        return parsed;
                }
            }
            return defaultValue;
        }

        // JSON scanning and parsing.

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

                    // Consume a line comment.
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '/')
                    {
                        pos += 2;
                        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
                            pos++;
                        continue;
                    }

                    // Consume a block comment.
                    if (c == '/' && pos + 1 < source.Length && source[pos + 1] == '*')
                    {
                        pos += 2;
                        while (pos + 1 < source.Length && !(source[pos] == '*' && source[pos + 1] == '/'))
                            pos++;
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
                pos++; // Consume the opening string quote.

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

                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;

                return token;
            }
        }

        private static object ParseJsonValue(JsonScanner s, int depth = 0)
        {
            if (depth >= 64) throw new FormatException("JSON nesting is too deep.");
            char c = s.Peek();
            if (c == '{') return ParseJsonObject(s, depth + 1);
            if (c == '[') return ParseJsonArray(s, depth + 1);
            if (c == '"') return s.ReadString();
            if (c == '\0') throw new FormatException("Unexpected end of input.");
            return s.ReadNumberOrKeyword();
        }

        private static Dictionary<string, object> ParseJsonObject(JsonScanner s, int depth)
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

                object val = ParseJsonValue(s, depth);
                if (!string.IsNullOrEmpty(key))
                {
                    dict[key] = val;
                }
            }

            throw new FormatException("Unterminated object.");
        }

        private static List<object> ParseJsonArray(JsonScanner s, int depth)
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

                object val = ParseJsonValue(s, depth);
                list.Add(val);
            }

            throw new FormatException("Unterminated array.");
        }
    }
}
