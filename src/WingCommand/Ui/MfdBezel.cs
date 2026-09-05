using System.Collections.Generic;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Named MFD bezel claim on top of <see cref="BezelRegistry"/>. Coordinates with
    /// Boscali Summer's OPS/RAD screens without a compile-time reference.
    /// </summary>
    internal static class MfdBezel
    {
        public static bool TryClaim(
            string id, bool preferLeft, VirtualMFD mfd,
            out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left)
        {
            buttons = null;
            screens = null;
            slot = -1;
            left = preferLeft;
            if (mfd == null || !GameAccess.MfdAvailable) return false;

            List<Button> leftButtons = GameAccess.GetLeftButtons(mfd);
            List<Button> rightButtons = GameAccess.GetRightButtons(mfd);
            List<MFDScreen> leftScreens = GameAccess.GetLeftScreens(mfd);
            List<MFDScreen> rightScreens = GameAccess.GetRightScreens(mfd);

            if (!BezelRegistry.TryClaim(
                id, preferLeft,
                leftButtons == null ? 0 : leftButtons.Count,
                rightButtons == null ? 0 : rightButtons.Count,
                (isLeft, index) => IsFree(
                    isLeft ? leftButtons : rightButtons,
                    isLeft ? leftScreens : rightScreens,
                    index),
                out left, out slot))
                return false;

            buttons = left ? leftButtons : rightButtons;
            screens = left ? leftScreens : rightScreens;
            return buttons != null && screens != null && slot >= 0 && slot < buttons.Count;
        }

        public static void Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens,
            int slot, bool left, MFDScreen screen)
        {
            while (screens.Count <= slot) screens.Add(null);
            screens[slot] = screen;
            mfd.SetupButtons();

            Button bezel = buttons[slot];
            bezel.enabled = true;
            bezel.interactable = true;
            if (bezel.onClick.GetPersistentEventCount() == 0)
            {
                VirtualMFD owner = mfd;
                bool onLeft = left;
                bezel.onClick.AddListener(() =>
                {
                    if (onLeft) owner.PressLeftButton(bezel);
                    else owner.PressRightButton(bezel);
                });
            }

            screen.CloseScreen(Screen.width * (left ? Vector3.left : Vector3.right));
        }

        public static MFDScreen FindTemplate(VirtualMFD mfd)
        {
            return FindTemplate(GameAccess.GetLeftScreens(mfd)) ??
                   FindTemplate(GameAccess.GetRightScreens(mfd));
        }

        public static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            // Fixed option pages give a stable footprint; faction/target lists can grow.
            foreach (MFDScreen s in screens)
            {
                if (s != null && (s.shortName == "MAP" || s.shortName == "HUD") &&
                    s.transform.parent != null && MfdPresentation.HasNativeLayout(s)) return s;
            }
            foreach (MFDScreen s in screens)
            {
                if (s != null && s.transform.parent != null && MfdPresentation.HasNativeLayout(s)) return s;
            }
            return null;
        }

        private static bool IsFree(List<Button> buttons, List<MFDScreen> screens, int index)
        {
            if (buttons == null || index < 0 || index >= buttons.Count || buttons[index] == null)
                return false;
            return screens == null || index >= screens.Count || screens[index] == null;
        }
    }
}
