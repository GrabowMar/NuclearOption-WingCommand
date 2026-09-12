using System;
using UnityEngine.EventSystems;

namespace WingCommand
{
    /// <summary>Nest keyboard capture while text fields are focused, restoring the previous Rewired state
    /// after the last release or teardown. Check availability before offering entry and log capture
    /// failures.</summary>
    internal static class WingKeyboardGuard
    {
        private static int depth;
        private static bool wasEnabled;
        private static bool held;

        /// <summary>Whether text entry can safely suppress aircraft keyboard controls.</summary>
        public static bool Available
        {
            get
            {
                try
                {
                    return Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                           Rewired.ReInput.controllers.Keyboard != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Whether any focused field owns keyboard capture.</summary>
        public static bool Captured => depth > 0;

        /// <summary>Capture game keyboard input; balance with Release.</summary>
        public static void Capture()
        {
            depth++;
            if (depth > 1) return;

            try
            {
                Rewired.Keyboard keyboard = Rewired.ReInput.controllers?.Keyboard;
                if (keyboard == null)
                {
                    // Record that nothing was captured so Release does not invent a prior state.
                    held = false;
                    return;
                }

                wasEnabled = keyboard.enabled;
                keyboard.enabled = false;
                held = true;
            }
            catch (Exception e)
            {
                held = false;
                Plugin.Logger.LogWarning(
                    "[UI] could not hold the keyboard for text entry; typing may reach the " +
                    "aircraft: " + e.Message);
            }
        }

        /// <summary>Restore keyboard input after all captures are released.</summary>
        public static void Release()
        {
            if (depth == 0) return;

            depth--;
            if (depth > 0) return;
            ForceRelease();
        }

        /// <summary>Restore captured state during teardown even if destroyed fields never
        /// deselect.</summary>
        public static void ForceRelease()
        {
            if (depth == 0 && !held) return;

            depth = 0;
            if (!held) return;

            held = false;
            try
            {
                Rewired.Keyboard keyboard = Rewired.ReInput.controllers?.Keyboard;
                if (keyboard != null) keyboard.enabled = wasEnabled;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[UI] could not restore keyboard input: " + e.Message);
            }
        }

        /// <summary>Deselect through EventSystem so focused fields release capture.</summary>
        public static void Defocus()
        {
            try
            {
                EventSystem current = EventSystem.current;
                if (current != null && current.currentSelectedGameObject != null)
                    current.SetSelectedGameObject(null);
            }
            catch (Exception)
            {
                // ForceRelease covers failed deselection.
            }
        }
    }
}
