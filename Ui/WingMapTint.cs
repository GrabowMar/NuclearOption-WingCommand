using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// Harmony calls postfixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Wing marks on map icons (spec WMC program §5): a ring in the member's element colour with its badge, command
    /// brackets on the WMC selection, target and downed-pilot rings — kept apart from the game's own icon colours, which
    /// stay authoritative.</summary>
    internal static class WingMapTint
    {
        // The icon's own selected flag: DynamicMap updates selectedIcons after its colour callback.
        private static readonly AccessTools.FieldRef<MapIcon, bool> nativeSelected = AccessTools.FieldRefAccess<MapIcon, bool>("isSelected");
        private static bool warned;

        /// <summary>The game's colours back, then the current mark (none for a unit that lost it).</summary>
        public static void Restore(Unit unit)
        {
            if (unit == null || SceneSingleton<DynamicMap>.i == null) return;
            try
            {
                if (!DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon) || icon == null) return;
                // The unit repaint keeps the game's filter dimming and player highlight (MapIcon.UpdateColor does not).
                icon.UnitMapIcon_UpdateColor();
                Apply(icon);
            }
            catch (Exception e)
            {
                Warn(e);
            }
        }

        /// <summary>The mark again without repainting the game's fade state (icons are recreated and repainted at will).</summary>
        public static void Reassert(Unit unit)
        {
            if (unit == null || SceneSingleton<DynamicMap>.i == null) return;
            try
            {
                if (DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon)) Apply(icon);
            }
            catch (Exception e)
            {
                Warn(e);
            }
        }

        private static void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            Plugin.Logger.LogWarning("[Map] wing marks failed once (further failures are silent): " + e.Message);
        }

        internal static void Apply(UnitMapIcon icon)
        {
            if (icon == null || icon.iconImage == null) return;
            WingMarkers.Role role = WingMarkers.RoleOf(icon.unit, out int element, out int number);
            WmcPanel panel = WmcPanel.Instance;
            bool wmc = panel != null && panel.Visible;
            // The WMC selection is by clicks on wing icons: keep them clickable while WMC is open, even when the game has
            // them selected.
            bool isPlayer = SceneSingleton<CombatHUD>.i != null && ReferenceEquals(SceneSingleton<CombatHUD>.i.aircraft, icon.unit);
            icon.iconImage.raycastTarget = MapSelectionPolicy.IconReceivesPointer(isPlayer, nativeSelected(icon), role == WingMarkers.Role.Member, wmc);
            HighlightMode mode = Plugin.Settings.MapMarkers.Value;
            bool selected = wmc && role == WingMarkers.Role.Member && icon.unit is Aircraft a && panel.Context.Selection.Contains(a.persistentID.Id);
            WingMapPresentation p = WingMapPresentation.Resolve(role == WingMarkers.Role.Member, role == WingMarkers.Role.Target,
                mode != HighlightMode.Off, mode == HighlightMode.WingAndTargets, wmc, selected, role == WingMarkers.Role.Downed);
            string status = role == WingMarkers.Role.Member ? (mode != HighlightMode.Off ? WingMarkers.Badge(element, number) : null)
                : role == WingMarkers.Role.Downed ? WingMarkers.DownedLabel(icon.unit) : null;
            WingMarkerBadge.Apply(icon.iconImage, p, WingMarkers.ColorOf(role, element), status);
        }

        /// <summary>The mark survives the game's repaint on setup, reuse and colour changes.</summary>
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
                if (__instance is UnitMapIcon u && WingMarkers.RoleOf(u.unit, out _, out _) != WingMarkers.Role.None) Apply(u);
            }
        }

        /// <summary>Airbases stay on the map while WMC is open or a member is landing or going home.</summary>
        [HarmonyPatch(typeof(DynamicMap), "ShouldShowAirbase")]
        internal static class ShowAirbasePatch
        {
            [HarmonyPostfix]
            internal static void Postfix(ref bool __result)
            {
                if (__result) return;
                if (WmcPanel.Instance != null && WmcPanel.Instance.Visible)
                {
                    __result = true;
                    return;
                }
                WingService w = WingService.Instance;
                if (w == null) return;
                foreach (WingMember m in w.Members)
                    if (m.Recovery != null)
                    {
                        __result = true;
                        return;
                    }
            }
        }
    }
}
