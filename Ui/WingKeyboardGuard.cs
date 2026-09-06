using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Unity invokes OnDisable by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>
    /// Disables Rewired keyboard input while panel fields are focused, using a nesting count.
    /// Restores the prior enabled state after the last release or panel teardown.
    /// Callers check Available before offering text entry; capture failures are logged.
    /// </summary>
    internal static class WingKeyboardGuard
    {
        private static int depth;
        private static bool wasEnabled;
        private static bool held;

        /// <summary>
        /// Check before offering text entry so typing does not also control the aircraft.
        /// </summary>
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

        /// <summary>True while at least one field has the keyboard.</summary>
        public static bool Captured => depth > 0;

        /// <summary>Take the keyboard away from the game. Balanced by <see cref="Release"/>.</summary>
        public static void Capture()
        {
            depth++;
            if (depth > 1) return;

            try
            {
                Rewired.Keyboard keyboard = Rewired.ReInput.controllers?.Keyboard;
                if (keyboard == null)
                {
                    // Nothing to hold. Recorded so Release does not restore a state that was
                    // never captured.
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

        /// <summary>Give the keyboard back, once every field has let go of it.</summary>
        public static void Release()
        {
            if (depth == 0) return;

            depth--;
            if (depth > 0) return;
            ForceRelease();
        }

        /// <summary>
        /// Restores the captured keyboard state during panel teardown, including when
        /// a destroyed field never sends its deselect event.
        /// </summary>
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

        /// <summary>
        /// Deselects through the event system so the field releases its keyboard capture.
        /// </summary>
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
                // Nothing to do: ForceRelease is the backstop for this.
            }
        }
    }

    /// <summary>
    /// Publishes a line to the panel's status strip while the pointer is over something that
    /// is not a button — a text field, a framed readout.
    ///
    /// <see cref="WingButton"/> owns the hover-note channel because almost everything on the
    /// panel is a button; this is the small adapter for the things that are not.
    /// </summary>
    internal sealed class WingHoverNote : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Note { get; set; }

        public void OnPointerEnter(PointerEventData eventData) =>
            WingButton.PublishExternal(Note, entering: true);

        public void OnPointerExit(PointerEventData eventData) =>
            WingButton.PublishExternal(Note, entering: false);

        private void OnDisable() => WingButton.PublishExternal(Note, entering: false);
    }
}
