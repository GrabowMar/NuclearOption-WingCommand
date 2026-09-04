using System;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Tints map icons so wingmen and the units they are engaging stand out from the rest
    /// of the friendly force.
    ///
    /// <c>MapIcon.UpdateColor</c> is the single place every icon assigns its colour, so a
    /// postfix there covers selection, deselection, faction changes and theme switches
    /// without touching the game's own colour logic. The stock call sites only fire on
    /// those events, so <see cref="Refresh"/> is called whenever wing membership or the
    /// engaged set changes.
    /// </summary>
    internal static class WingMapTint
    {
        /// <summary>Re-apply the tint to one unit's map icon.</summary>
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
                }
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Map icon refresh failed: " + e.Message);
            }
        }

        /// <summary>
        /// The patch itself, in its own class because the attribute has to be on a class.
        ///
        /// This is why map tinting never worked: the postfix lived on a method of
        /// <c>WingMapTint</c>, which carries no class-level <c>[HarmonyPatch]</c>.
        /// <c>PatchClassProcessor</c> returns before reading any method attribute when the
        /// containing class is unannotated, so <c>PatchAll</c> skipped it in silence and
        /// the wing was never coloured on the map at all.
        /// </summary>
        [HarmonyPatch(typeof(MapIcon), nameof(MapIcon.UpdateColor))]
        internal static class MapIconColorPatch
        {
            [HarmonyPostfix]
            private static void Postfix(MapIcon __instance)
            {
                if (Plugin.Settings.Highlight.Value == HighlightMode.Off) return;
                if (!(__instance is UnitMapIcon unitIcon) || unitIcon.iconImage == null) return;

                WingMarkers.Role role = WingMarkers.RoleOf(unitIcon.unit);
                bool commandSelected = false;
                if (role == WingMarkers.Role.Member &&
                    WmcScreen.TacticalCommandModeActive && unitIcon.unit is Aircraft aircraft)
                {
                    WingCommandManager manager = WingCommandManager.Instance;
                    WingMember member = manager?.Wing.Find(aircraft);
                    commandSelected = manager != null && manager.Selection.Contains(member);
                }
                WingMarkerBadge.ApplyCommandSelection(unitIcon.iconImage, commandSelected);
                if (role == WingMarkers.Role.None)
                {
                    Unit u = unitIcon.unit;
                    if (u != null && !u.disabled && u.definition != null)
                    {
                        if (GameManager.GetLocalAircraft(out Aircraft local) && local != null &&
                            local.NetworkHQ != null && u.NetworkHQ != null && u.NetworkHQ != local.NetworkHQ)
                        {
                            if (u.definition.typeIdentity.radar > 0.25f || (u is Ship && u.definition.typeIdentity.air > 0.4f))
                            {
                                unitIcon.iconImage.color = IsSelected(unitIcon)
                                    ? new Color(1f, 0.45f, 0.15f, 1f)
                                    : new Color(0.95f, 0.25f, 0.2f, 0.85f);
                            }
                        }
                    }
                    return;
                }

                // Keep the game's own selected-vs-unselected contrast by brightening the
                // selected state rather than flattening both to one colour.
                unitIcon.iconImage.color = WingMarkers.ColorFor(
                    role, IsSelected(unitIcon) || commandSelected);
            }

            private static bool IsSelected(UnitMapIcon icon)
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return map != null && map.selectedIcons.Contains(icon);
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
