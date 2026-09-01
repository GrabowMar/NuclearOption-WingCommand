using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Harmony invokes patch Postfix methods by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>
    /// Wing symbology on the in-cockpit HUD, so the display in front of the player agrees
    /// with the tactical map about who is in the wing and what it is shooting at.
    ///
    /// Until this existed the HUD marked exactly one aircraft distinctly — the game's
    /// <c>AllyInfo</c> picks the nearest friendly each second and swaps its marker to
    /// <c>closestAircraftSprite</c>. That is a proximity indicator, but it looks like a
    /// wing designation, so it read as the wing marking one arbitrary aircraft and
    /// missing everybody else.
    ///
    /// <c>HUDUnitMarker.UpdateColor</c> is private and every colour assignment funnels
    /// through it, so it is patched by name and the original is kept for restoring a
    /// unit's own colour when it leaves the wing.
    /// </summary>
    internal static class WingHudTint
    {
        private static MethodInfo updateColor;
        private static bool resolved;

        /// <summary>Resolve the private repaint method once. Failure disables HUD tinting.</summary>
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

        /// <summary>Re-apply, or clear, the tint on one unit's HUD marker.</summary>
        public static void Refresh(Unit unit)
        {
            if (unit == null || !Plugin.Settings.HighlightWingOnHud.Value) return;

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

        /// <summary>
        /// Repaint every wingman and engaged target.
        ///
        /// Unlike the map, a HUD marker's colour is rewritten outside the repaint method:
        /// for the first second of a marker's life it fades in from the warning colour,
        /// and it is recoloured whenever a track goes stale or comes back. Reasserting on
        /// the poll timer is what keeps a wingman's colour from quietly reverting.
        /// </summary>
        public static void Reassert(WingRegistry wing)
        {
            if (!Plugin.Settings.HighlightWingOnHud.Value) return;

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

            // A selected target is drawn in the theme's selected colour and gets the
            // bracket sprite. That is the player's own designation and outranks ours.
            if (marker.selected)
            {
                WingMarkerBadge.Clear(marker.image);
                return;
            }

            WingMarkers.Role role = WingMarkers.RoleOf(marker.unit);
            if (role == WingMarkers.Role.None)
            {
                WingMarkerBadge.Clear(marker.image);
                Restore(marker);
                return;
            }

            // Preserve whatever alpha the marker is carrying: it encodes range fade,
            // stale-track dimming and jamming, none of which are ours to override.
            Color tint = WingMarkers.ColorFor(role);
            marker.image.color = new Color(tint.r, tint.g, tint.b, marker.image.color.a);
            WingMarkerBadge.Apply(marker.image, role);
        }

        private static void Restore(HUDUnitMarker marker)
        {
            if (!resolved) Initialise();
            if (updateColor == null) return;

            try { updateColor.Invoke(marker, null); }
            catch { /* a marker mid-teardown is not worth a log line every poll */ }
        }

        /// <summary>
        /// Catch markers the game repaints on its own — theme changes, faction changes, a
        /// track going stale, and marker creation for a wingman that only just came into
        /// view — so they take the wing colour immediately rather than on the next poll.
        /// </summary>
        [HarmonyPatch(typeof(HUDUnitMarker), "UpdateColor")]
        internal static class UpdateColorPatch
        {
            [HarmonyPostfix]
            private static void Postfix(HUDUnitMarker __instance)
            {
                if (!Plugin.Settings.HighlightWingOnHud.Value) return;
                if (__instance.image == null) return;
                if (__instance.selected)
                {
                    WingMarkerBadge.Clear(__instance.image);
                    return;
                }

                WingMarkers.Role role = WingMarkers.RoleOf(__instance.unit);
                WingMarkerBadge.Apply(__instance.image, role);
                if (role == WingMarkers.Role.None) return;

                Color tint = WingMarkers.ColorFor(role);
                __instance.image.color =
                    new Color(tint.r, tint.g, tint.b, __instance.image.color.a);
            }
        }
    }
}
