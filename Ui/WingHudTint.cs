using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Harmony calls postfixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Wing marks on the game's HUD markers (spec WMC program §5): members in their element's colour, the wing's
    /// targets and downed pilots; the player's own selected target keeps the game's colour and brackets.</summary>
    internal static class WingHudTint
    {
        private static MethodInfo updateColor;
        private static bool resolved;

        /// <summary>The game's colour back (then the current mark, if any).</summary>
        public static void Restore(Unit unit)
        {
            if (!TryMarker(unit, out HUDUnitMarker marker)) return;
            if (!resolved)
            {
                resolved = true;
                updateColor = AccessTools.Method(typeof(HUDUnitMarker), "UpdateColor");
                if (updateColor == null) Plugin.Logger.LogWarning("[Map] HUDUnitMarker.UpdateColor not found; HUD marks will not clear");
            }
            try
            {
                updateColor?.Invoke(marker, null);
            }
            catch (Exception)
            {
                // A marker torn down between the poll and the repaint.
            }
        }

        /// <summary>The mark again: the game recolours markers on creation, fades and stale tracks.</summary>
        public static void Reassert(Unit unit)
        {
            if (TryMarker(unit, out HUDUnitMarker marker)) Apply(marker);
        }

        private static bool TryMarker(Unit unit, out HUDUnitMarker marker)
        {
            marker = null;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            return unit != null && hud != null && hud.TryGetMarker(unit, out marker) && marker != null;
        }

        private static void Apply(HUDUnitMarker marker)
        {
            if (marker.image == null || marker.selected || Plugin.Settings.MapMarkers.Value == HighlightMode.Off) return;
            WingMarkers.Role role = WingMarkers.RoleOf(marker.unit, out int element, out _);
            if (role == WingMarkers.Role.None) return;
            // The game's alpha carries range, stale tracks and jamming.
            Color tint = WingMarkers.ColorOf(role, element);
            marker.image.color = new Color(tint.r, tint.g, tint.b, marker.image.color.a);
        }

        [HarmonyPatch(typeof(HUDUnitMarker), "UpdateColor")]
        internal static class UpdateColorPatch
        {
            [HarmonyPostfix]
            private static void Postfix(HUDUnitMarker __instance) => Apply(__instance);
        }
    }
}
