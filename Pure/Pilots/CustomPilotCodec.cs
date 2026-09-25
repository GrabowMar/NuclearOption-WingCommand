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

            if (!MiniJson.TryParse(json, out object root)) return payload;

            if (root is Dictionary<string, object> dict)
            {
                if (MiniJson.TryGetList(dict, "pilots", out List<object> pilotList))
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

                if (MiniJson.TryGetList(dict, "chatters", out List<object> chatterList))
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
            string callsign = MiniJson.GetString(dict, "callsign");
            if (string.IsNullOrWhiteSpace(callsign)) return null;

            string name = MiniJson.GetString(dict, "name");
            if (string.IsNullOrWhiteSpace(name)) name = callsign;

            string tag = MiniJson.GetString(dict, "dialoguetag");
            if (string.IsNullOrWhiteSpace(tag)) tag = callsign.ToUpperInvariant();

            string personaStr = MiniJson.GetString(dict, "persona");
            ChatterPersona persona = ChatterPersona.Professional;
            if (!string.IsNullOrWhiteSpace(personaStr))
            {
                if (Enum.TryParse(personaStr, ignoreCase: true, out ChatterPersona parsed))
                    persona = parsed;
            }

            string background = MiniJson.GetString(dict, "background") ?? "";
            int xp = MiniJson.GetInt(dict, "xp", 0);
            int kills = MiniJson.GetInt(dict, "kills", 0);
            int sorties = MiniJson.GetInt(dict, "sorties", 0);

            int face = MiniJson.GetInt(dict, "face", -1);
            int hair = MiniJson.GetInt(dict, "hair", -1);
            int uniform = MiniJson.GetInt(dict, "uniform", -1);
            int accessory = MiniJson.GetInt(dict, "accessory", 0);
            int backdrop = MiniJson.GetInt(dict, "backdrop", -1);
            int portraitVersion = MiniJson.GetInt(dict, "portraitVersion", 0);
            PortraitBody body = string.Equals(MiniJson.GetString(dict, "body"), "female", StringComparison.OrdinalIgnoreCase)
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
                    sb.AppendLine($"      \"name\": \"{MiniJson.Escape(p.Name)}\",");
                    sb.AppendLine($"      \"callsign\": \"{MiniJson.Escape(p.Callsign)}\",");
                    sb.AppendLine($"      \"dialogueTag\": \"{MiniJson.Escape(p.ResolvedDialogueTag)}\",");
                    sb.AppendLine($"      \"persona\": \"{p.Persona}\",");
                    sb.AppendLine($"      \"background\": \"{MiniJson.Escape(p.Background)}\",");
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
                        chatterSb.AppendLine($"      \"speakerTag\": \"{MiniJson.Escape(c.SpeakerTag)}\",");
                        chatterSb.AppendLine($"      \"opening\": \"{MiniJson.Escape(c.Opening)}\",");
                        chatterSb.AppendLine($"      \"reply\": \"{MiniJson.Escape(c.Reply)}\",");
                        chatterSb.Append($"      \"replyTag\": \"{MiniJson.Escape(c.ReplyTag)}\"");
                    }
                    else if (c.IsEventLine)
                    {
                        chatterSb.AppendLine($"      \"event\": \"{MiniJson.Escape(c.Event)}\",");
                        chatterSb.AppendLine($"      \"speakerTag\": \"{MiniJson.Escape(c.SpeakerTag)}\",");
                        chatterSb.Append($"      \"text\": \"{MiniJson.Escape(c.Text)}\"");
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

        private static CustomChatterRecord ParseChatter(Dictionary<string, object> dict)
        {
            string opening = MiniJson.GetString(dict, "opening");
            string reply = MiniJson.GetString(dict, "reply");
            string speakerTag = MiniJson.GetString(dict, "speakertag");
            string replyTag = MiniJson.GetString(dict, "replytag");
            string eventName = MiniJson.GetString(dict, "event");
            string text = MiniJson.GetString(dict, "text");

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
    }
}
