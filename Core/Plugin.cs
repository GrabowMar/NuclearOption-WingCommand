using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NOAvionics.Ui;
using UnityEngine;

// Unity calls Awake and OnDestroy by reflection, so suppress IDE0051 in this file.
#pragma warning disable IDE0051

namespace WingCommand
{
 /// <summary>Initialises configuration and Harmony patches, and owns the persistent manager.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.wingcommand";
        public const string PluginName = "Wing Command";
        public const string PluginVersion = "0.9.2";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static WingConfig Settings { get; private set; }

     /// <summary>Log diagnostics only when Debug/VerboseLogging is enabled. Keep errors, warnings, and
     /// the startup message on Logger.</summary>
        internal static void LogVerbose(string message)
        {
            if (Settings == null || !Settings.VerboseLogging.Value) return;
            Logger?.LogInfo(message);
        }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Settings = new WingConfig(Config);

            // Both plugins load avionics.avss from the same config directory, with an embedded fallback
            // for missing overrides.
            AvStyleHost.Configure(BepInEx.Paths.ConfigPath, LogVerbose, Logger.LogWarning);

            
            if (Settings.CheatFreePurchases || Settings.CheatNoWingLimit || Settings.CheatBypassRank)
            {
                Logger.LogWarning(
                    "Unsafe Debug cheats are enabled: " +
                    $"FreePlanePurchases={Settings.CheatFreePurchases}, " +
                    $"DisableWingSizeLimit={Settings.CheatNoWingLimit}, " +
                    $"BypassRankRequirement={Settings.CheatBypassRank}. " +
                    "These options may break mission balance, UI, formations or the mod itself.");
            }

            if (!FormationSolver.ValidateGeometry(WingFormation.MaxWingSize, out string geometryProblem))
                Logger.LogError("Formation geometry validation failed: " + geometryProblem);

            // Resolve reflection before patches consult GameAccess.Available.
            GameAccess.Initialise();
            WingHudTint.Initialise();
            CountermeasureAccess.Initialise();

            // Register built-in behaviours through the public API before the first tick.
            WingAi.FaultReporter = (id, e) => Logger.LogError(
                $"[Wing] AI provider '{id}' failed and has been disabled for this mission: " +
                $"{e.GetType().Name} - {e.Message}");
            WingReflexes.RegisterDefaults();
            WingCustomPilots.EnsurePilotsDirectory();

            harmony = new Harmony(PluginGuid);
            Type[] patchTypes =
            {
                typeof(AiCombatTweak),
                typeof(AiTargetDeconflictionPatch),
                typeof(WingMapWaypointPatch),
                typeof(WingMapSelectionPatch),
                typeof(WingMapTint.MapIconColorPatch),
                typeof(WingMapTint.ShowAirbasePatch),
                typeof(WingHudTint.UpdateColorPatch),
                typeof(CombatHUDHitAudioPatch),
                typeof(WingRadialMenuPatches),
                typeof(WingRadialMenuPatches.AwakePatch),
                typeof(WingMenuActionPatches),
                typeof(WingTakeoverPatches),
                typeof(WingInboundTaxiPatch),
                typeof(WingRefitEjectPatch),
                typeof(WingTakeoffQueuePatch),
                typeof(HangarDeliveryCompletionPatch),
            };
            for (int i = 0; i < patchTypes.Length; i++)
                harmony.PatchAll(patchTypes[i]);
            ReportPatches();

            var go = new GameObject("WingCommandManager");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<WingCommandManager>();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            // Log effective player settings: existing BepInEx values override new defaults. Resolve the
            // initial fidelity budget here; mission start snapshots it again. Tuning constants are
            // identified by the plugin version.
            WingFidelity.Begin(Settings.Mode.Value);
            LogVerbose(
                "Effective settings: " +
                $"Mode={Settings.Mode.Value} [{WingFidelity.Summary()}] " +
                $"Shape={WingFormation.Shape} " +
                $"DefaultRoe={Settings.DefaultRoe.Value} " +
                $"AutoReturnOnEmpty={Settings.AutoReturnOnEmpty.Value} " +
                $"RtbReturnsToReserve={Settings.RtbReturnsToReserve.Value} " +
                $"TakeoverOnDeath={Settings.TakeoverOnDeath.Value} " +
                $"Radio={Settings.Radio.Value} " +
                $"PilotProgression={Settings.PilotProgression.Value} " +
                $"Shop={Settings.ShopEnabled.Value} " +
                $"Highlight={Settings.Highlight.Value}");
        }

     /// <summary>Log patched methods at startup. Harmony silently skips classes missing a class-level
     /// HarmonyPatch attribute.</summary>
        private void ReportPatches()
        {
            var patched = new List<MethodBase>(harmony.GetPatchedMethods());
            var names = new List<string>(patched.Count);
            foreach (MethodBase m in patched)
            {
                if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
            }

            names.Sort(System.StringComparer.Ordinal);
            LogVerbose($"Harmony patched {names.Count} method(s): {string.Join(", ", names.ToArray())}");

            // Name expected patches so game API changes produce missing-patch diagnostics.
            string[] expected =
            {
                "RadialMenuMain.OpenMenu",
                "RadialMenuMain.SetupMain",
                "RadialMenuAction.AllowedOnAircraft",
                "RadialMenuAction.TriggerAction",
                "MapIcon.UpdateColor",
                "UnitMapIcon.UpdateIcon",
                "HUDUnitMarker.UpdateColor",
                "AIPilotCombatModes.EnterState",
                "CombatAI.ChooseHQTarget",
                "GameManager.FinishGame",
                // Both airfield patches depend on Pilot.SwitchState; losing them breaks apron pilot
                // retention and runway cleanup.
                "Pilot.SwitchState",
                "Hangar.DoorSequenceCarrier",
            };

            foreach (string want in expected)
            {
                if (!names.Contains(want))
                    Logger.LogWarning($"Expected Harmony patch missing: {want}");
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
