using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Keeps exactly one MFD panel on screen at a time.
    ///
    /// <para>Vanilla radio-buttons each bezel column <em>independently</em>: opening a left
    /// screen closes the other left screens and does not touch the right column, because the
    /// two columns render on opposite sides of the map and can happily both be open. The
    /// three-column layout renders every mod panel into the same left dock, so a left screen
    /// and a right screen open together would sit on top of each other — which the player
    /// sees as WMC and RAD fighting over one rectangle.</para>
    ///
    /// <para>Postfixing the press handlers rather than prefixing them means vanilla has
    /// already decided what to open; this only closes what should no longer show. Registered
    /// in <c>Core/Plugin.cs</c>'s explicit patch list — an unlisted patch class is skipped in
    /// total silence.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class MfdSinglePanelPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressLeftButton))]
        public static void PressLeftPostfix(VirtualMFD __instance, Button button) =>
            AfterPress(__instance, button, left: true);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressRightButton))]
        public static void PressRightPostfix(VirtualMFD __instance, Button button) =>
            AfterPress(__instance, button, left: false);

        private static void AfterPress(VirtualMFD mfd, Button button, bool left)
        {
            if (mfd == null || button == null) return;
            if (!MfdPresentation.Expanded) return;

            MFDScreen pressed = ScreenFor(mfd, button, left);

            // A press that closed its own screen leaves nothing to protect, and a button with
            // no screen behind it is a spare slot.
            if (pressed == null || !pressed.isActive) return;

            MfdPanelDock.CloseOthers(mfd, pressed);
        }

        /// <summary>
        /// The screen the pressed button drives.
        ///
        /// Vanilla pairs the two lists purely by index and indexes the screens list without
        /// checking its length — a short list throws inside a UI callback. This does the same
        /// lookup with the bounds check vanilla omits, so a mod that has claimed a slot past
        /// the end of the screens list cannot turn a button press into an exception.
        /// </summary>
        private static MFDScreen ScreenFor(VirtualMFD mfd, Button button, bool left)
        {
            List<Button> buttons = left ? GameAccess.GetLeftButtons(mfd) : GameAccess.GetRightButtons(mfd);
            List<MFDScreen> screens = left ? GameAccess.GetLeftScreens(mfd) : GameAccess.GetRightScreens(mfd);
            if (buttons == null || screens == null) return null;

            int index = buttons.IndexOf(button);
            if (index < 0 || index >= screens.Count) return null;

            return screens[index];
        }
    }
}
