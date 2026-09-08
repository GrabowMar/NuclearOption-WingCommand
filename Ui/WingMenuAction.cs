using System;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Delegate-backed native radial action. Harmony prefixes dispatch nonvirtual
    /// AllowedOnAircraft and TriggerAction only for this subclass, allowing other mods' action types to
    /// coexist.</summary>
    internal class WingMenuAction : RadialMenuAction
    {
        /// <summary>Action callback receiving the player's aircraft.</summary>
        public Action<Aircraft> OnTrigger;

        /// <summary>Optional visibility/availability predicate; null permits display.</summary>
        public Func<Aircraft, bool> OnAllowed;

        /// <summary>IconFactory glyph key.</summary>
        public string IconKey;

        public static WingMenuAction Create(string label, Action<Aircraft> onTrigger,
                                            Func<Aircraft, bool> onAllowed = null)
        {
            var action = CreateInstance<WingMenuAction>();
            action.hideFlags = HideFlags.HideAndDontSave;
            action.DisplayName = label;
            action.OnTrigger = onTrigger;
            action.OnAllowed = onAllowed;
            // Use native no-op NavLights as fallback if no prefix claims this action.
            GameAccess.SetActionType(action, ActionType.NavLights);
            action.weapon_number = -1;
            return action;
        }

        /// <summary>Copy native wedge sprites and colours because new ScriptableObjects default to
        /// invisible appearance.</summary>
        public void CopyAppearanceFrom(RadialMenuAction template)
        {
            GameAccess.CopyAppearance(this, template);
        }
    }

    [HarmonyPatch(typeof(RadialMenuAction))]
    internal static class WingMenuActionPatches
    {
        [HarmonyPatch(nameof(RadialMenuAction.AllowedOnAircraft))]
        [HarmonyPrefix]
        private static bool AllowedOnAircraft_Prefix(
            RadialMenuAction __instance, Aircraft aircraft, ref bool __result)
        {
            if (!(__instance is WingMenuAction wing)) return true;

            try
            {
                __result = wing.OnAllowed == null || wing.OnAllowed(aircraft);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("WingMenuAction.AllowedOnAircraft threw: " + e);
                __result = false;
            }
            return false;
        }

        [HarmonyPatch(nameof(RadialMenuAction.TriggerAction))]
        [HarmonyPrefix]
        private static bool TriggerAction_Prefix(RadialMenuAction __instance, Aircraft aircraft)
        {
            if (!(__instance is WingMenuAction wing)) return true;

            try
            {
                wing.OnTrigger?.Invoke(aircraft);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("WingMenuAction.TriggerAction threw: " + e);
            }
            return false;
        }

        /// <summary>Guard Flash after submenu replacement destroys slice objects; native FlashSelection
        /// continues invoking the old action for two seconds.</summary>
        [HarmonyPatch(nameof(RadialMenuAction.Flash))]
        [HarmonyPrefix]
        private static bool Flash_Prefix(RadialMenuAction __instance)
        {
            if (!GameAccess.Available) return true;
            return GameAccess.GetIconImage(__instance) != null;
        }
    }
}
