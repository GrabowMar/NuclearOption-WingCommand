using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WingCommand
{
    /// <summary>
    /// File discovery, sample generation, and loading for custom pilots and chatter.
    /// </summary>
    internal static class WingCustomPilots
    {
        public static string PilotsDirectory =>
            Path.Combine(BepInEx.Paths.ConfigPath, "WingCommand", "Pilots");

        private static readonly Dictionary<string, List<string>> customEvents =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ensures the Pilots folder exists, creating it and writing sample_pilots.json if empty.
        /// </summary>
        public static void EnsurePilotsDirectory()
        {
            try
            {
                string dir = PilotsDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    Plugin.Logger.LogInfo("[CustomPilots] Created Pilots directory at " + dir);
                }

                string[] jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
                if (jsonFiles.Length == 0)
                {
                    string samplePath = Path.Combine(dir, "sample_pilots.json");
                    File.WriteAllText(samplePath, CustomPilotCodec.SampleJson());
                    Plugin.Logger.LogInfo("[CustomPilots] Wrote sample_pilots.json to " + samplePath);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[CustomPilots] Failed to initialize Pilots directory: " + e.Message);
            }
        }

        /// <summary>
        /// Open the Pilots folder in Windows Explorer.
        /// </summary>
        public static void OpenFolder()
        {
            EnsurePilotsDirectory();
            string dir = PilotsDirectory;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
                WingCommandManager.Instance?.Toast("Opened Pilots folder");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[CustomPilots] Could not open folder: " + e.Message);
                WingCommandManager.Instance?.Toast("Pilots folder: " + dir);
            }
        }

        /// <summary>
        /// Scan the Pilots folder, load all custom pilots and chatters, and return the pilots list.
        /// </summary>
        public static List<CustomPilotRecord> LoadAllCustomPilots(out int chattersCount)
        {
            EnsurePilotsDirectory();
            chattersCount = 0;
            var pilots = new List<CustomPilotRecord>();
            var seenCallsigns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ChatterDialogue.ClearCustomAmbient();
            customEvents.Clear();

            string dir = PilotsDirectory;
            var searchPaths = new List<string> { dir };

            string pluginPilots = Path.Combine(BepInEx.Paths.PluginPath, "WingCommand", "Pilots");
            if (Directory.Exists(pluginPilots) && !string.Equals(pluginPilots, dir, StringComparison.OrdinalIgnoreCase))
            {
                searchPaths.Add(pluginPilots);
            }

            var allFiles = new List<string>();
            foreach (string searchDir in searchPaths)
            {
                if (Directory.Exists(searchDir))
                {
                    allFiles.AddRange(Directory.GetFiles(searchDir, "*.json", SearchOption.AllDirectories));
                }
            }

            foreach (string file in allFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    CustomPilotPayload payload = CustomPilotCodec.Decode(content);

                    foreach (CustomChatterRecord chatter in payload.Chatters)
                    {
                        if (chatter.IsAmbientExchange)
                        {
                            ChatterDialogue.RegisterCustomAmbient(new ChatterExchange(
                                chatter.Opening, chatter.Reply, chatter.SpeakerTag, chatter.ReplyTag));
                            chattersCount++;
                        }
                        else if (chatter.IsEventLine)
                        {
                            RegisterEventLine(chatter.SpeakerTag, chatter.Event, chatter.Text);
                            chattersCount++;
                        }
                    }

                    foreach (CustomPilotRecord pilot in payload.Pilots)
                    {
                        if (pilot != null && !string.IsNullOrWhiteSpace(pilot.Callsign) && seenCallsigns.Add(pilot.Callsign))
                        {
                            pilots.Add(pilot);
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[CustomPilots] Error loading " + Path.GetFileName(file) + ": " + e.Message);
                }
            }

            Plugin.Logger.LogInfo(
                $"[CustomPilots] Loaded {pilots.Count} pilot(s), {chattersCount} chatter(s) across {allFiles.Count} file(s)");
            return pilots;
        }

        /// <summary>
        /// Scan the Pilots folder and import all custom pilots and chatters.
        /// </summary>
        public static int ImportAll(out int chattersCount, out string message)
        {
            List<CustomPilotRecord> pilots = LoadAllCustomPilots(out chattersCount);
            int newPilotsCount = 0;

            foreach (CustomPilotRecord pilot in pilots)
            {
                if (!WingPilotRoster.ContainsCallsign(pilot.Callsign))
                {
                    WingPilot recruited = WingPilotRoster.ImportCustom(pilot);
                    if (recruited != null) newPilotsCount++;
                }
            }

            if (newPilotsCount > 0)
            {
                message = $"Imported {newPilotsCount} custom pilot(s) and {chattersCount} chatter(s)";
            }
            else if (pilots.Count > 0)
            {
                message = $"Loaded {chattersCount} chatter(s). All {pilots.Count} pilot(s) in folder already in squadron.";
            }
            else
            {
                message = "No pilots found in Pilots folder. Sample file created.";
            }

            return newPilotsCount;
        }

        private static void RegisterEventLine(string speakerTag, string eventName, string text)
        {
            string tag = string.IsNullOrWhiteSpace(speakerTag) ? "*" : speakerTag.Trim().ToUpperInvariant();
            string evt = eventName.Trim().ToUpperInvariant();
            string key = tag + "|" + evt;

            if (!customEvents.TryGetValue(key, out List<string> lines))
            {
                lines = new List<string>();
                customEvents[key] = lines;
            }
            lines.Add(text);
        }

        /// <summary>
        /// Check if a custom event phrase exists for this pilot and event.
        /// </summary>
        public static bool TryGetEventLine(string tag, string eventName, string detail, out string phrase)
        {
            phrase = null;
            if (string.IsNullOrWhiteSpace(eventName)) return false;

            string cleanTag = !string.IsNullOrWhiteSpace(tag) ? tag.Trim().ToUpperInvariant() : "*";
            string cleanEvent = eventName.Trim().ToUpperInvariant();

            string specificKey = cleanTag + "|" + cleanEvent;
            if (customEvents.TryGetValue(specificKey, out List<string> lines) && lines.Count > 0)
            {
                phrase = FormatLine(lines[Random.Range(0, lines.Count)], detail);
                return true;
            }

            string generalKey = "*|" + cleanEvent;
            if (customEvents.TryGetValue(generalKey, out List<string> generalLines) && generalLines.Count > 0)
            {
                phrase = FormatLine(generalLines[Random.Range(0, generalLines.Count)], detail);
                return true;
            }

            return false;
        }

        private static string FormatLine(string template, string detail)
        {
            if (string.IsNullOrWhiteSpace(template)) return "";
            string subject = string.IsNullOrWhiteSpace(detail) ? "target" : detail.Trim();
            return template.Replace("{target}", subject).Replace("{0}", subject);
        }

    }
}
