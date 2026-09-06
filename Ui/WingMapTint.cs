using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Wing outlines and command selection, applied after native icon updates.</summary>
    internal static class WingMapTint
    {
        // DynamicMap updates selectedIcons after calling the icon's colour callback.
        // Read the icon flag so a select/deselect repaint observes the new native state.
        private static readonly AccessTools.FieldRef<MapIcon, bool> nativeSelected =
            AccessTools.FieldRefAccess<MapIcon, bool>("isSelected");

        /// <summary>Reconcile one unit's map markings with its current roster identity.</summary>
        public static void Refresh(Unit unit)
        {
            if (unit == null) return;

            try
            {
                if (DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon) && icon != null)
                {
                    // The unit-level entry point, not MapIcon.UpdateColor: it adds the
                    // dimming for units excluded from the target list and the white
                    // highlight for the player's own aircraft. Calling the base method
                    // directly would drop both and make a refreshed icon look subtly
                    // different from one the game repainted.
                    icon.UnitMapIcon_UpdateColor();
                    Apply(icon);
                }
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Map icon refresh failed: " + e.Message);
            }
        }

        /// <summary>
        /// Reattach markings after icons are created, recreated, or another UI refresh
        /// changes their components. Does not repaint the native image or its fade state.
        /// </summary>
        public static void Reassert(WingRegistry wing)
        {
            if (SceneSingleton<DynamicMap>.i == null) return;
            if (wing != null)
            {
                foreach (WingMember member in wing.Members)
                    Reassert(member.Aircraft);
            }
            foreach (Unit unit in WingMarkers.EngagedTargets) Reassert(unit);
        }

        private static void Reassert(Unit unit)
        {
            if (unit != null && DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon))
                Apply(icon);
        }

        private static void Apply(UnitMapIcon icon)
        {
            if (icon == null || icon.iconImage == null) return;

            WingCommandManager manager = WingCommandManager.Instance;
            WingMember member = icon.unit is Aircraft aircraft ? manager?.Wing.Find(aircraft) : null;
            bool tactical = WmcScreen.TacticalCommandModeActive;
            bool nativeSelected = IsSelected(icon);
            bool commandSelected = manager != null && manager.Selection.Contains(member);

            // Stock SelectIcon disables raycasts. A plane already in the weapon target
            // list must still accept tactical clicks; leaving Tactical restores the stock
            // hit behavior without selecting or deselecting any weapon targets.
            bool isPlayer = SceneSingleton<CombatHUD>.i?.aircraft == icon.unit;
            icon.iconImage.raycastTarget = MapSelectionPolicy.IconReceivesPointer(
                isPlayer, nativeSelected, member != null, tactical);

            HighlightMode highlight = Plugin.Settings.Highlight.Value;
            WingMapPresentation presentation = WingMapPresentation.Resolve(
                isWingMember: member != null,
                isWingTarget: WingMarkers.RoleOf(icon.unit) == WingMarkers.Role.Target,
                highlightWing: highlight != HighlightMode.Off,
                highlightTargets: highlight == HighlightMode.WingAndTargets,
                tacticalActive: tactical,
                commandSelected: commandSelected);

            // Never replace the faction silhouette's colour. Native target clearing,
            // filters and theme changes repaint that Image; our separate mesh outline
            // keeps membership visible through all of them.
            WingMarkerBadge.Apply(icon.iconImage, presentation);
        }

        private static bool IsSelected(UnitMapIcon icon)
        {
            return nativeSelected(icon);
        }

        // UnselectAll/DeselectAllIcons call MapIcon.DeselectIcon -> UpdateColor, then
        // remove native TargetMarkers. Our outline belongs to the Image, not that list.
        // SetIcon and UpdateIcon also reconcile reused/recreated icons and command scope.
        [HarmonyPatch]
        internal static class MapIconColorPatch
        {
            [HarmonyTargetMethods]
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(MapIcon), nameof(MapIcon.UpdateColor));
                yield return AccessTools.Method(typeof(UnitMapIcon), nameof(UnitMapIcon.UnitMapIcon_UpdateColor));
                yield return AccessTools.Method(typeof(UnitMapIcon), nameof(UnitMapIcon.SetIcon));
                yield return AccessTools.Method(typeof(UnitMapIcon), nameof(UnitMapIcon.UpdateIcon));
            }
            [HarmonyPostfix]
            private static void Postfix(MapIcon __instance)
            {
                if (__instance is UnitMapIcon unitIcon) Apply(unitIcon);
            }
        }

        /// <summary>
        /// Keeps airbase icons visible on the tactical map when doing tactical planning
        /// or when any wingman is actively landing or returning to base.
        /// </summary>
        [HarmonyPatch(typeof(DynamicMap), "ShouldShowAirbase")]
        internal static class ShowAirbasePatch
        {
            [HarmonyPostfix]
            internal static void Postfix(ref bool __result)
            {
                if (__result) return;

                if (WmcScreen.TacticalCommandModeActive)
                {
                    __result = true;
                    return;
                }

                WingCommandManager manager = WingCommandManager.Instance;
                if (manager != null && manager.Wing != null && manager.Wing.HasAnyLandingOrder())
                {
                    __result = true;
                }
            }
        }
    }
}
