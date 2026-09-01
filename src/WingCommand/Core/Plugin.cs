using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Unity invokes Awake and OnDestroy by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>
    /// Entry point. Owns configuration, applies Harmony patches, and keeps a single
    /// persistent manager object alive for the lifetime of the process.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.wingcommand";
        public const string PluginName = "Wing Command";
        public const string PluginVersion = "0.9.5.0";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static WingConfig Settings { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Settings = new WingConfig(Config);

            // The retired keys these warnings described are no longer bound at all, so an
            // old configuration file simply carries dead lines that BepInEx drops on its
            // next save. Warning about a setting the player can no longer see was worse
            // than silence: it named keys that had already stopped existing.
            if (Settings.CheatFreePurchases || Settings.CheatNoWingLimit)
            {
                Logger.LogWarning(
                    "Unsafe Debug cheats are enabled: " +
                    $"FreePlanePurchases={Settings.CheatFreePurchases}, " +
                    $"DisableWingSizeLimit={Settings.CheatNoWingLimit}. " +
                    "These options may break mission balance, UI, formations or the mod itself.");
            }

            if (!FormationSolver.ValidateGeometry(WingFormation.MaxWingSize, out string geometryProblem))
                Logger.LogError("Formation geometry validation failed: " + geometryProblem);

            // Resolve reflection accessors before patching: the radial patches consult
            // GameAccess.Available and quietly stand down if the game layout has moved.
            GameAccess.Initialise();
            WingHudTint.Initialise();
            CountermeasureAccess.Initialise();

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            ReportPatches();

            var go = new GameObject("WingCommandManager");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<WingCommandManager>();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            // Log what is actually in force, not what the defaults say.
            //
            // BepInEx only applies a default to a *new* key: an existing config file keeps
            // its own value forever. Changing a default in code therefore does nothing for
            // anyone who has already run the mod, which silently left tuning changes
            // unapplied and a feature enabled long after it was supposedly turned off.
            // The Smart/Performance mode is resolved per mission (WingBrain.Begin); this
            // logs the configured mode and the derived budget for a default mission start.
            //
            // Only settings a player can actually have changed. The tuned numbers are
            // constants in WingTuning now, so logging them told a bug report nothing it
            // could not read off the version, and buried the lines that do vary.
            WingBrain.Begin(Settings.Mode.Value);
            Logger.LogInfo(
                "Effective settings: " +
                $"Mode={Settings.Mode.Value} [{WingBrain.Summary()}] " +
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

        /// <summary>
        /// Report what Harmony actually patched.
        ///
        /// A patch class with no class-level <c>[HarmonyPatch]</c> is skipped in total
        /// silence: <c>PatchClassProcessor</c> returns before it looks at a single method
        /// attribute. Map tinting shipped that way and did nothing for weeks, with no
        /// error anywhere to say so. Listing the patched methods at startup turns that
        /// failure from invisible into a line in the log.
        /// </summary>
        private void ReportPatches()
        {
            var patched = new List<MethodBase>(harmony.GetPatchedMethods());
            var names = new List<string>(patched.Count);
            foreach (MethodBase m in patched)
            {
                if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
            }

            names.Sort(System.StringComparer.Ordinal);
            Logger.LogInfo($"Harmony patched {names.Count} method(s): {string.Join(", ", names.ToArray())}");

            // Named so a future game update that moves one of these is reported as a
            // missing patch rather than as a feature that silently stopped working.
            string[] expected =
            {
                "MapIcon.UpdateColor",
                "HUDUnitMarker.UpdateColor",
                "AIPilotCombatModes.EnterState",
                "CombatAI.ChooseHQTarget",
                "GameManager.FinishGame",
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
