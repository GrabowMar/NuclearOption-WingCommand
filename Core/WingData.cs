using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;

namespace WingCommand
{
    /// <summary>Load-time data and SelfCheck (spec §5.2, §8):
    /// <list type="bullet">
    /// <item>Tuning: <c>tuning.defaults.json</c> is written from the compiled defaults, then
    /// <c>tuning.user.json</c> is applied.</item>
    /// <item>Formations: the embedded built-ins, validated, with valid entries of <c>formations.user.json</c>
    /// merged on top by id. Invalid user entries are logged and skipped, so the built-ins stand.</item>
    /// <item>Airframe layers: shipped <c>airframes.json</c>, then <c>airframes.calibrated.json</c>, then
    /// <c>airframes.user.json</c>.</item>
    /// </list>
    /// Never throws; every problem is logged.</summary>
    internal static class WingData
    {
        public static List<FormationDefinition> Formations { get; private set; } = new List<FormationDefinition>();
        public static ProfileLibrary Profiles { get; private set; } = new ProfileLibrary();

        public static void Load(ManualLogSource log)
        {
            string root = WingConfig.DataRoot;
            Write(Path.Combine(root, "tuning.defaults.json"), Tuning.Export(Tuning.Types), log);
            string tuning = ReadIfExists(Path.Combine(root, "tuning.user.json"), log);
            if (tuning != null)
                foreach (string key in Tuning.Apply(tuning, Tuning.Types)) log.LogWarning("[Data] tuning.user.json: ignored " + key);

            var errors = new List<string>();
            List<FormationDefinition> builtIns = FormationCatalog.Parse(Embedded("WingCommand.formations.json"), errors);
            foreach (string e in errors) log.LogError("[Data] built-in formations: " + e);
            errors.Clear();
            string user = ReadIfExists(Path.Combine(root, "formations.user.json"), log);
            List<FormationDefinition> custom = user == null ? new List<FormationDefinition>() : FormationCatalog.Parse(user, errors);
            foreach (string e in errors) log.LogWarning("[Data] formations.user.json: " + e);
            Formations = FormationCatalog.Merge(builtIns, custom);

            LoadProfiles(log);
            log.LogInfo($"[Data] {Formations.Count} formations ({custom.Count} user); tuning and airframes from {root}");
        }

        /// <summary>(Re)build the airframe layers, e.g. after a calibration wrote a new calibrated file.</summary>
        public static void LoadProfiles(ManualLogSource log)
        {
            string root = WingConfig.DataRoot;
            var profiles = new ProfileLibrary();
            Layer(profiles, "airframes.json", Embedded("WingCommand.airframes.json"), log);
            Layer(profiles, "airframes.calibrated.json", ReadIfExists(Path.Combine(root, "airframes.calibrated.json"), log), log);
            Layer(profiles, "airframes.user.json", ReadIfExists(Path.Combine(root, "airframes.user.json"), log), log);
            Profiles = profiles;
        }

        private static void Layer(ProfileLibrary profiles, string source, string json, ManualLogSource log)
        {
            if (json == null) return;
            foreach (string e in profiles.AddLayer(source, json)) log.LogWarning("[Data] " + e);
        }

        private static string Embedded(string name)
        {
            using (Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) return "";
                using (var reader = new StreamReader(s)) return reader.ReadToEnd();
            }
        }

        private static string ReadIfExists(string path, ManualLogSource log)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException e)
            {
                log.LogWarning($"[Data] could not read {path}: {e.Message}");
                return null;
            }
        }

        private static void Write(string path, string text, ManualLogSource log)
        {
            try
            {
                File.WriteAllText(path, text);
            }
            catch (IOException e)
            {
                log.LogWarning($"[Data] could not write {path}: {e.Message}");
            }
        }
    }
}
