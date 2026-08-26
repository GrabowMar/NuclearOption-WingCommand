using System;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// A <see cref="RadialMenuAction"/> that runs a delegate instead of one of the stock
    /// <c>ActionType</c> cases.
    ///
    /// <c>RadialMenuAction.AllowedOnAircraft</c> and <c>TriggerAction</c> are not virtual,
    /// so behaviour is injected with Harmony prefixes that dispatch to this subclass and
    /// skip the original. This mirrors how BOTE extends the same menu, and the two coexist:
    /// each prefix only claims instances of its own type and returns true (continue) for
    /// everything else.
    /// </summary>
    internal class WingMenuAction : RadialMenuAction
    {
        /// <summary>Runs when the slice is selected. Receives the player's aircraft.</summary>
        public Action<Aircraft> OnTrigger;

        /// <summary>Optional gate. Null means always shown.</summary>
        public Func<Aircraft, bool> OnAllowed;

        /// <summary>Key into <see cref="IconFactory"/> for this entry's drawn glyph.</summary>
        public string IconKey;

        public static WingMenuAction Create(string label, Action<Aircraft> onTrigger,
                                            Func<Aircraft, bool> onAllowed = null)
        {
            var action = CreateInstance<WingMenuAction>();
            action.hideFlags = HideFlags.HideAndDontSave;
            action.DisplayName = label;
            action.OnTrigger = onTrigger;
            action.OnAllowed = onAllowed;
            // NavLights is a no-op in the stock TriggerAction switch, so even if a prefix
            // ever fails to claim this instance the worst case is that nothing happens.
            GameAccess.SetActionType(action, ActionType.NavLights);
            action.weapon_number = -1;
            return action;
        }

        /// <summary>
        /// Borrow sprites and colours from a stock action so the slice renders in the
        /// game's own style. Runtime-created ScriptableObjects have null sprites and
        /// transparent colours, which would otherwise draw an invisible wedge.
        /// </summary>
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

        /// <summary>
        /// Swapping the wheel contents destroys the slice GameObjects, but the stock
        /// <c>FlashSelection</c> coroutine keeps calling <c>Flash()</c> on the action that
        /// was just picked for another two seconds. Without this guard that throws a
        /// NullReferenceException every frame after any submenu switch.
        /// </summary>
        [HarmonyPatch(nameof(RadialMenuAction.Flash))]
        [HarmonyPrefix]
        private static bool Flash_Prefix(RadialMenuAction __instance)
        {
            if (!GameAccess.Available) return true;
            return GameAccess.GetIconImage(__instance) != null;
        }
    }
}
