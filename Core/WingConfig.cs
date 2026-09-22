using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace WingCommand
{
    /// <summary>Wing Command 1.0 settings. The first 1.0 launch archives the 0.9 file beside itself so every
    /// key starts from its 1.0 default (BepInEx only applies defaults to keys it has not seen). Data files
    /// (formations, profiles, roster) live under <see cref="DataRoot"/>.</summary>
    internal sealed class WingConfig
    {
        internal const int SchemaVersion = 1;
        private const string SchemaMarker = "SchemaVersion = 1";

        internal static string DataRoot => Path.Combine(Paths.ConfigPath, "WingCommand", "v1");

        public ConfigEntry<WingMode> Mode { get; }
        public ConfigEntry<bool> VerboseLogging { get; }
        public ConfigEntry<bool> DevTools { get; }

        public WingConfig(ConfigFile c)
        {
            ArchiveLegacy(c);
            c.Bind("Meta", "SchemaVersion", SchemaVersion, new ConfigDescription(
                "Settings schema written by Wing Command. Do not edit.", null,
                new ConfigurationManagerAttributes { Browsable = false }));

            Mode = c.Bind("AI", "Mode", WingMode.Smart, new ConfigDescription(
                "Smart runs the full AI. Performance halves guidance rate and decision cadence for AI-led " +
                "wings (player-led formations always run at full rate). Applies at the next mission start.",
                null, new ConfigurationManagerAttributes { Order = 100 }));

            DevTools = c.Bind("Debug", "DevTools", false, new ConfigDescription(
                "Enable developer tools: debug overlay, telemetry recorder, step tests and calibration.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 70 }));

            c.Bind("Debug", "ExportLogs", false, new ConfigDescription(
                "Export the latest Wing Command log events from this session beside WingCommand.dll " +
                "(WingCommand-logs.txt). Safe diagnostics only; no upload.", null,
                new ConfigurationManagerAttributes
                {
                    DispName = "Export logs",
                    CustomDrawer = WingLogExport.DrawButton,
                    HideDefaultButton = true,
                    Order = 65,
                }));

            VerboseLogging = c.Bind("Debug", "VerboseLogging", false, new ConfigDescription(
                "Log decisions, state transitions and flight diagnostics. Applies immediately.",
                null, new ConfigurationManagerAttributes { DispName = "Debug action logging", Order = 60 }));

            Directory.CreateDirectory(DataRoot);
        }

        /// <summary>Move a pre-1.0 settings file aside once, then reload the now-empty file so orphaned
        /// 0.9 keys and values cannot leak into 1.0.</summary>
        private static void ArchiveLegacy(ConfigFile c)
        {
            string path = c.ConfigFilePath;
            if (!File.Exists(path)) return;
            if (File.ReadAllText(path).Contains(SchemaMarker)) return;
            File.Copy(path, path + ".0.9.bak", overwrite: true);
            File.WriteAllText(path, string.Empty);
            c.Reload();
        }
    }
}
