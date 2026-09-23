using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>The single list of Harmony patch classes and the game methods they must patch. Harmony skips
    /// a class without a class-level [HarmonyPatch] silently, so startup compares what was patched with
    /// what is expected and warns on every gap.</summary>
    internal static class PatchManifest
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(WingMenuActionPatches),
            typeof(WingRadialMenuPatches),
            typeof(WingRadialMenuPatches.AwakePatch),
            typeof(PlayerAutopilotPatches),
        };

        /// <summary>"DeclaringType.Method" names that must be patched after <see cref="Apply"/>.</summary>
        internal static readonly string[] Expected =
        {
            "RadialMenuAction.AllowedOnAircraft",
            "RadialMenuAction.TriggerAction",
            "RadialMenuAction.Flash",
            "RadialMenuMain.SetupMain",
            "RadialMenuMain.OpenMenu",
            "RadialMenuMain.OnDestroy",
            "SceneSingleton`1.Awake",
            "PilotPlayerState.PlayerAxisControls",
            "PilotPlayerState.PlayerThrottleAxis1Controls",
        };

        internal static void Apply(Harmony harmony, ManualLogSource log)
        {
            for (int i = 0; i < PatchTypes.Length; i++)
            {
                try
                {
                    harmony.PatchAll(PatchTypes[i]);
                }
                catch (Exception e)
                {
                    log.LogError($"[Patches] {PatchTypes[i].Name} failed to apply: {e.Message}");
                }
            }

            var names = new List<string>();
            foreach (MethodBase m in harmony.GetPatchedMethods())
            {
                if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
            }
            names.Sort(StringComparer.Ordinal);
            log.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PatchesInstalled, names.Count));
            Plugin.LogVerbose($"Harmony patched {names.Count} method(s): {string.Join(", ", names)}");

            foreach (string want in Expected)
            {
                if (!names.Contains(want)) log.LogWarning($"Expected Harmony patch missing: {want}");
            }
        }
    }
}
