using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NOAvionics.Ui;
using UnityEngine;

// Unity calls Awake and OnDestroy by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Loads settings, applies the patch manifest and creates the persistent runtime host.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.wingcommand";
        public const string PluginName = "Wing Command";
        public const string PluginVersion = "1.0.0";
        public const string PluginPrerelease = "alpha.0";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static WingConfig Settings { get; private set; }

        /// <summary>Diagnostics shown only with Debug/VerboseLogging enabled.</summary>
        internal static void LogVerbose(string message)
        {
            if (Settings == null || !Settings.VerboseLogging.Value) return;
            Logger?.LogInfo($"[frame={Time.frameCount}] {message}");
        }

        internal static void LogAction(string message,
                                       [CallerMemberName] string caller = "",
                                       [CallerFilePath] string source = "")
        {
            LogVerbose($"[Action] [{System.IO.Path.GetFileNameWithoutExtension(source)}.{caller}] {message}");
        }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            WingLogExport.Start(Logger);
            Settings = new WingConfig(Config);
            Settings.VerboseLogging.SettingChanged += OnLoggingChanged;

            // Both plugins load avionics.avss from the shared config directory with an embedded fallback.
            AvStyleHost.Configure(Paths.ConfigPath, LogVerbose, Logger.LogWarning);

            harmony = new Harmony(PluginGuid);
            PatchManifest.Apply(harmony, Logger);
            GameAccess.Initialise();
            WingData.Load(Logger);

            var go = new GameObject("WingCommandRuntime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            WingRuntime runtime = go.AddComponent<WingRuntime>();
            runtime.Register(new WingService());
            runtime.Register(new SpawnService());
            runtime.Register(new PlayerAutopilot());
            runtime.Register(new WingHotkeys());
            runtime.Register(new WingHudPanel());
            runtime.Register(new DevService());
            go.AddComponent<BridgeState>();

            Logger.LogInfo($"{PluginName} {PluginVersion}-{PluginPrerelease} loaded. " +
                $"mvid={typeof(Plugin).Assembly.ManifestModule.ModuleVersionId}");
            Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PluginReady, 0));
            LogVerbose($"Effective settings: Mode={Settings.Mode.Value} DevTools={Settings.DevTools.Value} " +
                $"DataRoot={WingConfig.DataRoot}");
        }

        private void OnLoggingChanged(object sender, EventArgs e)
        {
            Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.VerboseLoggingChanged,
                Settings.VerboseLogging.Value ? 1 : 0));
        }

        private void OnDestroy()
        {
            WingLogExport.Stop(Logger);
            if (Settings != null) Settings.VerboseLogging.SettingChanged -= OnLoggingChanged;
            harmony?.UnpatchSelf();
        }
    }
}
