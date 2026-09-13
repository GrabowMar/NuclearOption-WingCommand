using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace WingCommand
{
    internal static class WingLogExport
    {
        private const string FileName = "WingCommand-logs.txt";
        private static WingLogBuffer buffer;
        private static string status = "Exports mod log events only; message bodies are omitted for privacy.";

        internal static void Start(ManualLogSource source)
        {
            buffer = new WingLogBuffer(source, typeof(Plugin).Assembly);
            source.LogEvent += OnLog;
        }

        internal static void Stop(ManualLogSource source)
        {
            source.LogEvent -= OnLog;
            buffer = null;
        }

        private static void OnLog(object sender, LogEventArgs e) =>
            buffer?.Record(e.Source, (int)e.Level, e.Data);

        internal static void DrawButton(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical();
            if (GUILayout.Button("Export Wing Command logs"))
            {
                try
                {
                    var lines = buffer?.Snapshot();
                    if (lines == null) status = "Log capture is unavailable. Restart the game and retry.";
                    else
                    {
                        string directory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                        var output = new List<string> { "Wing Command " + Plugin.PluginVersion };
                        output.Add("Build=" + typeof(Plugin).Assembly.ManifestModule.ModuleVersionId);
                        var settings = Plugin.Settings;
                        // Explicit numeric/boolean/enum fields only. Never dump the config file.
                        output.Add($"AI: configuredMode={settings.Mode.Value}, activeMode={WingFidelity.Mode}, " +
                            $"sharpTurns={settings.AiSharpTurns.Value}, targetSpreading={settings.AiTargetSpreading.Value}, " +
                            $"missileWarningRepair={settings.AiMissileWarningRepair.Value}");
                        output.Add(FormattableString.Invariant($"Formation: shape={WingFormation.Shape}, spacing={WingFormation.SlotSpacing}, geometryStride={WingFidelity.GeometryStride}"));
                        output.Add($"Features: autoReturnOnEmpty={settings.AutoReturnOnEmpty.Value}, " +
                            $"returnToReserve={settings.RtbReturnsToReserve.Value}, takeover={settings.TakeoverOnDeath.Value}, " +
                            $"shop={settings.ShopEnabled.Value}, pilotProgression={settings.PilotProgression.Value}, " +
                            $"mapCommands={settings.MapCommandEnabled.Value}, mfd={settings.UseMfdPanel.Value}");
                        output.Add($"Debug: verbose={settings.VerboseLogging.Value}, freePurchases={settings.CheatFreePurchases}, " +
                            $"noWingLimit={settings.CheatNoWingLimit}, bypassRank={settings.CheatBypassRank}");
                        output.AddRange(lines);
                        File.WriteAllLines(Path.Combine(directory, FileName), output);
                        status = "Saved " + FileName + " beside WingCommand.dll (replaces previous export).";
                    }
                }
                catch (Exception)
                {
                    // Exception messages can disclose local paths or account names.
                    status = "Export failed. Check the plugin folder is writable and retry.";
                }
            }
            GUILayout.Label(status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.EndVertical();
        }
    }
}
