using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Reconciles wing outlines and command selection after native icon updates.</summary>
    internal static class WingMapTint
    {
        // Read the icon's selected flag because DynamicMap updates selectedIcons after its colour
        // callback.
        private static readonly AccessTools.FieldRef<MapIcon, bool> nativeSelected =
            AccessTools.FieldRefAccess<MapIcon, bool>("isSelected");

        /// <summary>Refresh map identity for one unit.</summary>
        public static void Refresh(Unit unit)
        {
            if (unit == null) return;

            try
            {
                if (DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon) && icon != null)
                {
                    // Use the unit repaint path to preserve native filter dimming and player
                    // highlighting; base MapIcon.UpdateColor omits them.
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

        /// <summary>Restore markings after icon recreation or external component changes without
        /// repainting native fade state.</summary>
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

            // Keep native-selected aircraft clickable for Tactical; restore stock raycast behaviour on
            // exit without changing weapon targets.
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

            // Keep wing identity in a separate outline so native faction, target, filter, and theme
            // repainting remains authoritative.
            WingMarkerBadge.Apply(icon.iconImage, presentation);
        }

        private static bool IsSelected(UnitMapIcon icon)
        {
            return nativeSelected(icon);
        }

        // Attach identity to the Image rather than native TargetMarkers removed during deselection.
        // Reconcile on icon setup, reuse, and scope changes.
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

        /// <summary>Keep airbase icons visible during tactical planning and active wing landing or
        /// RTB.</summary>
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
