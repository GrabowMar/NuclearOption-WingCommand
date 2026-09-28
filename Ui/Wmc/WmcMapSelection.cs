using HarmonyLib;
using UnityEngine;

// Harmony calls prefixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Selecting wingmen on the map (spec WMC program §5): while the WMC panel is open, a click on a wing member's
    /// icon selects it for WMC (shift adds or removes) instead of the game's own selection; every other icon keeps the
    /// game's click (targets, and friendlies to recruit).</summary>
    internal static class WmcMapSelection
    {
        /// <summary>True when WMC took the click (the game's click is skipped).</summary>
        public static bool Take(UnitMapIcon icon, MapIcon.ClickSource source)
        {
            WmcPanel panel = WmcPanel.Instance;
            WingService w = WingService.Instance;
            if (panel == null || !panel.Visible || w == null || icon == null || !(icon.unit is Aircraft a)) return false;
            bool member = false;
            foreach (WingMember m in w.Members) member |= ReferenceEquals(m.Aircraft, a);
            if (!member) return false;
            // A click through a foreground panel is nobody's.
            if (!WmcMapPointer.TryGet(SceneSingleton<DynamicMap>.i, out _, out _, out bool overIcon)) return true;
            // One physical click reaches here twice (Rewired's Select on press, the icon's click on release): wait for the mouse.
            bool mouse = Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0);
            if (MapSelectionPolicy.DeferToMouseClick(source == MapIcon.ClickSource.Controller, mouse, overIcon)) return true;
            WmcContext c = panel.Context;
            uint id = a.persistentID.Id;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) c.Selection.Toggle(id);
            else c.Selection.SelectOnly(id);
            c.Rescope();
            panel.Refresh();
            return true;
        }

        [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.ClickIcon))]
        internal static class ClickIconPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(UnitMapIcon __instance, MapIcon.ClickSource clickSource) => !Take(__instance, clickSource);
        }
    }
}
