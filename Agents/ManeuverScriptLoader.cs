using System;
using System.IO;
using UnityEngine;

namespace WingCommand
{
    internal static class ManeuverScriptLoader
    {
        private static ManeuverScripts scripts;
        public static void Reset() => scripts = null;

        public static ManeuverRecipe Get(ManeuverKind kind)
        {
            if (scripts == null)
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "WingCommand", "maneuvers.json");
                if (File.Exists(path))
                {
                    try
                    {
                        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("maximum size is 64 KiB");
                        scripts = Parse(File.ReadAllText(path));
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogWarning("Invalid manoeuvre scripts; using defaults: " + e.Message);
                    }
                }
                if (scripts == null)
                {
                    using (var stream = typeof(ManeuverScriptLoader).Assembly.GetManifestResourceStream("WingCommand.maneuvers.json"))
                    using (var reader = new StreamReader(stream))
                        scripts = Parse(reader.ReadToEnd());
                }
            }
            return scripts.Find(kind);
        }

        private static ManeuverScripts Parse(string json)
        {
            var value = JsonUtility.FromJson<ManeuverScripts>(json);
            if (value == null || !value.IsValid()) throw new InvalidDataException("invalid phases or manoeuvre names");
            return value;
        }
    }
}
