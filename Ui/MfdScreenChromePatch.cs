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
        private static readonly HashSet<MFDScreen> managed = new HashSet<MFDScreen>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MFDScreen), nameof(MFDScreen.CloseScreen))]
        public static void CloseScreenPostfix(MFDScreen __instance) => SetRootChrome(__instance, false);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MFDScreen), nameof(MFDScreen.ShowScreen))]
        public static void ShowScreenPostfix(MFDScreen __instance)
        {
            MfdPresentation.Apply(__instance);
            SetRootChrome(__instance, true);
            if (MfdPresentation.Expanded)
            {
                MfdPanelDock.OnScreenShown(__instance);

                // VirtualMFD reopens its remembered left and right pages in sequence on
                // every maximise. Both now share one dock, so the later ShowScreen must win
                // immediately or the two page surfaces ghost through one another.
                VirtualMFD mfd = Object.FindObjectOfType<VirtualMFD>();
                if (mfd != null) MfdPanelDock.CloseOthers(mfd, __instance);
            }
        }

        /// <summary>
        /// Apply the screen's existing active state after it has entered the shared dock.
        /// A screen can be closed before the maximised-map postfix creates that dock; in
        /// that ordering the CloseScreen postfix quite correctly leaves vanilla alone, but
        /// its root backplate would otherwise be carried on-screen when it is reparented.
        /// </summary>
        public static void SyncDockedState(MFDScreen screen)
        {
            if (screen == null) return;
            managed.Add(screen);
            SetRootChrome(screen, screen.isActive);
        }

        /// <summary>Discard scene-owned screen references after the MFD is destroyed.</summary>
        public static void Reset() => managed.Clear();

        private static void SetRootChrome(MFDScreen screen, bool visible)
        {
            if (screen == null) return;

            // Once a screen has passed through our dock, keep its root chrome paired with
            // isActive for the rest of the scene. Minimize restores the stock parent before
            // the final CloseScreen callback; limiting this to IsDocked would miss that
            // callback and strand the restored white border over the cockpit.
            if (!managed.Contains(screen) && !MfdPanelDock.IsDocked(screen)) return;

            scratch.Clear();
            screen.GetComponents(scratch);

            for (int i = 0; i < scratch.Count; i++)
            {
                Graphic graphic = scratch[i];
                if (graphic != null) graphic.enabled = visible;
            }

            scratch.Clear();
        }
    }
}
