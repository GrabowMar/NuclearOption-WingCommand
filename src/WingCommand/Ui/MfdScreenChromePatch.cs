using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Stops a closed MFD screen's own chrome from rendering.
    ///
    /// <para><c>MFDScreen.CloseScreen</c> hides a screen by deactivating its child
    /// <c>displayPanel</c> and parking the root one screen-width to the side. Several stock
    /// screens — <c>MFD_MIS</c> and the faction-info panels among them — also carry an
    /// <c>Image</c> on the <b>root</b>, outside <c>displayPanel</c>, which that method never
    /// touches. Vanilla gets away with it because the park distance is measured from a
    /// roughly centred position, so the leftover graphic ends up comfortably off screen.</para>
    ///
    /// <para>Once every screen is docked into a left-hand column, "one screen-width to the
    /// right" starts from the far left of the canvas and lands a wide panel's edge back
    /// inside the viewport — which is what put a large white-outlined rectangle across the
    /// top-right of the map and over the button rail.</para>
    ///
    /// <para>Only root-level graphics are touched, and only their <c>enabled</c> flag. The
    /// GameObject stays active so components like <c>ObjectiveInfoList</c> keep ticking
    /// exactly as they did before, and vanilla's own show/hide of <c>displayPanel</c> is left
    /// to do its job.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class MfdScreenChromePatch
    {
        /// <summary>Root graphics this patch switched off, so they can be switched back on.</summary>
        private static readonly List<Graphic> scratch = new List<Graphic>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MFDScreen), nameof(MFDScreen.CloseScreen))]
        public static void CloseScreenPostfix(MFDScreen __instance) => SetRootChrome(__instance, false);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MFDScreen), nameof(MFDScreen.ShowScreen))]
        public static void ShowScreenPostfix(MFDScreen __instance)
        {
            SetRootChrome(__instance, true);
            if (Plugin.Settings.FitMapToPanels.Value && GameAccess.MfdAvailable)
            {
                MfdPanelDock.OnScreenShown(__instance);
            }
        }

        private static void SetRootChrome(MFDScreen screen, bool visible)
        {
            if (screen == null) return;

            // Only while this mod owns the layout. With the stock bezel the park distance is
            // sufficient and vanilla should be left entirely alone.
            if (!Plugin.Settings.FitMapToPanels.Value || !GameAccess.MfdAvailable) return;

            scratch.Clear();
            screen.GetComponents(scratch);

            for (int i = 0; i < scratch.Count; i++)
            {
                Graphic graphic = scratch[i];
                if (graphic != null) graphic.enabled = visible;
            }

            scratch.Clear();

            // A stock panel's ground is a slot child, and the slot stays active when the
            // panel is parked — take the ground with the panel.
            VanillaPanelSkin.SetGroundVisible(screen, visible);
        }
    }
}
