using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Harmony calls postfixes by reflection, so suppress IDE0051 in this file.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Shared wing/target identity on native HUD markers. Cache private UpdateColor for patching
    /// and restoring native colour when roles change.</summary>
    internal static class WingHudTint
    {
        private static MethodInfo updateColor;
        private static bool resolved;

        /// <summary>Resolve native marker repaint once; disable HUD tinting on failure.</summary>
        public static void Initialise()
        {
            resolved = true;
            updateColor = AccessTools.Method(typeof(HUDUnitMarker), "UpdateColor");

            if (updateColor == null)
            {
                Plugin.Logger.LogWarning(
                    "HUDUnitMarker.UpdateColor not found; wing HUD symbology will not " +
                    "reset when a unit leaves the wing.");
            }
        }

        /// <summary>Apply or clear one unit's wing HUD tint.</summary>
        public static void Refresh(Unit unit)
        {
            if (unit == null || Plugin.Settings.Highlight.Value == HighlightMode.Off) return;

            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud == null) return;
                if (!hud.TryGetMarker(unit, out HUDUnitMarker marker) || marker == null) return;

                Apply(marker);
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("HUD marker refresh failed: " + e.Message);
            }
        }

        /// <summary>Periodically reassert colours because native marker creation, fading, and
        /// track-staleness paths can recolour outside UpdateColor.</summary>
        public static void Reassert(WingRegistry wing)
        {
            if (Plugin.Settings.Highlight.Value == HighlightMode.Off) return;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return;

            if (wing != null)
            {
                foreach (WingMember m in wing.Members)
                {
                    if (m.Aircraft != null && hud.TryGetMarker(m.Aircraft, out HUDUnitMarker marker))
                        Apply(marker);
                }
            }

            foreach (Unit u in WingMarkers.EngagedTargets)
            {
                if (u != null && hud.TryGetMarker(u, out HUDUnitMarker marker))
                    Apply(marker);
            }
        }

        private static void Apply(HUDUnitMarker marker)
        {
            if (marker.image == null) return;

            // Preserve native player-selected target colour and brackets over wing tint.
            if (marker.selected) return;

            WingMarkers.Role role = WingMarkers.RoleOf(marker.unit);
            if (role == WingMarkers.Role.None)
            {
                Restore(marker);
                return;
            }

            // Retain native alpha for range, stale tracks, and jamming.
            Color tint = WingMarkers.ColorFor(role);
            marker.image.color = new Color(tint.r, tint.g, tint.b, marker.image.color.a);
        }

        private static void Restore(HUDUnitMarker marker)
        {
            if (!resolved) Initialise();
            if (updateColor == null) return;

            try { updateColor.Invoke(marker, null); }
            catch { /* Ignore transient marker teardown without per-poll logging. */ }
        }

        /// <summary>Apply tint immediately after native repaint, including theme/faction changes and newly
        /// visible tracks.</summary>
        [HarmonyPatch(typeof(HUDUnitMarker), "UpdateColor")]
        internal static class UpdateColorPatch
        {
            [HarmonyPostfix]
            private static void Postfix(HUDUnitMarker __instance)
            {
                if (Plugin.Settings.Highlight.Value == HighlightMode.Off) return;
                if (__instance.image == null) return;
                if (__instance.selected) return;

                WingMarkers.Role role = WingMarkers.RoleOf(__instance.unit);
                if (role == WingMarkers.Role.None) return;

                Color tint = WingMarkers.ColorFor(role);
                __instance.image.color =
                    new Color(tint.r, tint.g, tint.b, __instance.image.color.a);
            }
        }
    }
}
