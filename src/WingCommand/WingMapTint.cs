using System;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Tints the map icons of wing members so they stand out from the rest of the
    /// friendly force.
    ///
    /// <c>MapIcon.UpdateColor</c> is the single place every icon assigns its colour, so a
    /// postfix there covers selection, deselection, faction changes and theme switches
    /// without touching the game's own colour logic. The stock call sites only fire on
    /// those events, so <see cref="RefreshAll"/> is called whenever wing membership
    /// changes.
    /// </summary>
    internal static class WingMapTint
    {
        private static Color cached = new Color(0.20f, 0.90f, 1f);
        private static string cachedFrom;

        /// <summary>Configured wing colour, parsed once per distinct config value.</summary>
        public static Color WingColor
        {
            get
            {
                string raw = Plugin.Config2.WingIconColor.Value;
                if (raw != cachedFrom)
                {
                    cachedFrom = raw;
                    if (!ColorUtility.TryParseHtmlString(raw, out cached))
                    {
                        cached = new Color(0.20f, 0.90f, 1f);
                        Plugin.Logger.LogWarning(
                            "Could not parse WingIconColor '" + raw + "'; using the default cyan.");
                    }
                }
                return cached;
            }
        }

        /// <summary>Re-apply the tint to every current member plus anything just dropped.</summary>
        public static void RefreshAll(WingRegistry wing, Unit alsoRefresh = null)
        {
            if (wing == null) return;

            foreach (WingMember m in wing.Members)
                Refresh(m.Aircraft);

            if (alsoRefresh != null) Refresh(alsoRefresh);
        }

        public static void Refresh(Unit unit)
        {
            if (unit == null) return;

            try
            {
                if (DynamicMap.TryGetMapIcon(unit, out UnitMapIcon icon) && icon != null)
                    icon.UpdateColor();
            }
            catch (Exception e)
            {
                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Map icon refresh failed: " + e.Message);
            }
        }

        [HarmonyPatch(typeof(MapIcon), nameof(MapIcon.UpdateColor))]
        [HarmonyPostfix]
        private static void UpdateColor_Postfix(MapIcon __instance)
        {
            if (!Plugin.Config2.HighlightWingOnMap.Value) return;

            WingCommandManager mgr = WingCommandManager.Instance;
            if (mgr == null || mgr.Wing.Count == 0) return;

            if (!(__instance is UnitMapIcon unitIcon)) return;

            Unit unit = unitIcon.unit;
            if (!(unit is Aircraft aircraft) || !mgr.Wing.Contains(aircraft)) return;

            Color tint = WingColor;

            // Keep the game's own selected-vs-unselected contrast by brightening the
            // selected state rather than flattening both to one colour.
            if (unitIcon.iconImage != null)
            {
                bool selected = IsSelected(unitIcon);
                unitIcon.iconImage.color = selected ? Brighten(tint, 0.35f) : tint;
            }
        }

        private static bool IsSelected(UnitMapIcon icon)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            return map != null && map.selectedIcons.Contains(icon);
        }

        private static Color Brighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        /// <summary>A theme-matched accent, used by the map panel so it does not clash.</summary>
        public static Color FriendlyThemeColor
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
                catch { return new Color(0.45f, 0.95f, 0.55f); }
            }
        }
    }
}
