using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WingCommand
{
    /// <summary>
    /// Holds the game's keyboard off while the player is typing into a panel field.
    ///
    /// This is the one piece of this mod that is dangerous if it is wrong. Nuclear Option
    /// reads the keyboard through Rewired continuously, and it does not care that a text
    /// field has focus: without this, naming a loadout template "Strike" rolls the aircraft,
    /// cuts the throttle and fires whatever is selected, one keystroke at a time, while the
    /// player watches their own callsign appear in a box.
    ///
    /// The game solves the same problem the same way for its chat box, which disables the
    /// Rewired keyboard on open and re-enables it a frame after close. This does the same
    /// thing, with three differences that matter for a mod:
    ///
    /// <list type="bullet">
    /// <item>It counts. Two fields on one panel must not have the first one released while
    /// the second is still focused.</item>
    /// <item>It restores what it found rather than forcing the keyboard back on, so it
    /// cannot switch input back on for a game that had turned it off for its own reasons.</item>
    /// <item>It fails safe in the opposite direction to most of this mod. Everywhere else, a
    /// member that cannot be reached degrades to doing nothing; here, doing nothing means
    /// the keystrokes reach the aircraft. If the guard cannot take the keyboard, the caller
    /// is told so and the field is not offered.</item>
    /// </list>
    /// </summary>
    internal static class WingKeyboardGuard
    {
        private static int depth;
        private static bool wasEnabled;
        private static bool held;

        /// <summary>
        /// True when the guard can actually take the keyboard on this build.
        ///
        /// Checked before a text field is built, not after it is focused: a rename field
        /// that silently flies the aircraft is worse than no rename field.
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

            if (!held) return;
            held = false;

            try
            {
                Rewired.Keyboard keyboard = Rewired.ReInput.controllers?.Keyboard;

                // Restore, do not force: if the game had its own reason for the keyboard
                // being off when the field took focus, that reason still stands.
                if (keyboard != null) keyboard.enabled = wasEnabled;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[UI] could not restore keyboard input: " + e.Message);
            }
        }

        /// <summary>
        /// Force the keyboard back, whatever the count says.
        ///
        /// The escape hatch for the one case the counting cannot cover: a field destroyed
        /// while focused — a page switched away from, a mission ended — never fires its
        /// deselect, and a mod that leaves the player unable to fly is not a mod they will
        /// keep. Called from the panel's own teardown.
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
        /// Drop focus from whatever field currently has it.
        ///
        /// Used when the panel is closing something out from under the player. Deselecting
        /// through the event system fires the field's own deselect handler, so the guard
        /// unwinds through the normal path rather than needing the forced one.
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
        public string Note;

        public void OnPointerEnter(PointerEventData eventData) =>
            WingButton.PublishExternal(Note, entering: true);

        public void OnPointerExit(PointerEventData eventData) =>
            WingButton.PublishExternal(Note, entering: false);

        private void OnDisable() => WingButton.PublishExternal(Note, entering: false);
    }
}
