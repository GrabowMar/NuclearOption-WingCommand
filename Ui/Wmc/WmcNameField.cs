using System;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WingCommand
{
    /// <summary>The bezel's one text field (LOADOUT's NAME; critique C18): the keyboard is the field's only while it is focused. Focus
    /// holds the toolkit's keyboard guard and the game's pause key (Esc cancels the edit instead of pausing); <see cref="Typing"/> lets
    /// WC's own hotkeys and the map's Esc stand aside; Enter commits to the template that was being edited when the field took focus
    /// (a tile click meanwhile cannot redirect it), then the field lets go on the next tick. <see cref="BlurAny"/> releases everything
    /// even when the field was disabled or destroyed — called on a tab switch, the panel hiding, a reset and the room opening.</summary>
    internal sealed class WmcNameField
    {
        private static WmcNameField focusedField;
        private static int typingUntilFrame = -1;
        private static bool pauseHeld, pauseWas;
        private static int pauseReleaseFrame = -1;

        private TMP_InputField field;
        private Action<string, string> commit;
        private string idAtFocus, shown;
        private bool focused, blurPending;

        /// <summary>A field has the keyboard now, or let go this frame or the last (the key that ended the edit acts nowhere else).</summary>
        public static bool Typing => focusedField != null || Time.frameCount <= typingUntilFrame;

        /// <summary>The id the page is editing; the field remembers it when it takes focus.</summary>
        public string EditingId { get; set; }

        public bool Focused => focused;

        /// <summary>The field holds text not yet saved (the build card's EDITED).</summary>
        public bool Dirty => focused && field != null && field.text != shown;

        public static WmcNameField Build(RectTransform parent, Rect r, int limit, Action<string, string> commit, string tip,
            string placeholder = "NAME", bool multiline = false)
        {
            var w = new WmcNameField { commit = commit };
            w.field = AvKit.InputField(parent, r, limit, w.OnEndEdit, w.OnFocus, w.OnBlur, tip, placeholder, multiline);
            return w;
        }

        /// <summary>What the field holds now, typed or not (SAVE reads the fields first: a click straight from a field is not lost).</summary>
        public string Text => field != null ? field.text : "";

        /// <summary>The saved name, shown while the field is not being typed in.</summary>
        public void SetText(string text)
        {
            if (focused || field == null) return;
            shown = text ?? "";
            if (field.text != shown) field.SetTextWithoutNotify(shown);
        }

        public void SetInteractable(bool on)
        {
            if (field == null || field.interactable == on) return;
            if (!on) Blur();
            field.interactable = on;
        }

        /// <summary>Automation: focus the field as a click would.</summary>
        public void Focus()
        {
            if (field == null || !field.interactable || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
        }

        /// <summary>Automation: commit what is typed, as Enter would.</summary>
        public void Submit(string text)
        {
            if (field == null) return;
            if (!focused) idAtFocus = EditingId;
            OnEndEdit(text ?? field.text);
        }

        private void OnFocus()
        {
            // Review R5: a blur pending from an edit that ended some other way (a click away, a tab switch) must not end this one.
            blurPending = false;
            focused = true;
            idAtFocus = EditingId;
            focusedField = this;
            if (pauseHeld) return;
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            pauseHeld = true;
        }

        private void OnEndEdit(string text)
        {
            if (idAtFocus != null) commit?.Invoke(idAtFocus, text);
            blurPending = true;
        }

        private void OnBlur()
        {
            blurPending = false;
            focused = false;
            if (ReferenceEquals(focusedField, this)) focusedField = null;
            typingUntilFrame = Time.frameCount + 1;
            // The Esc that ended the edit must not pause the game later this frame: the pause key comes back next frame.
            pauseReleaseFrame = Time.frameCount + 1;
        }

        /// <summary>Lets go: the field is deselected (the toolkit releases the keyboard); a field that still thinks it is focused
        /// (disabled or destroyed) is released by force.</summary>
        public void Blur()
        {
            blurPending = false;
            EventSystem es = EventSystem.current;
            if (field != null && es != null && es.currentSelectedGameObject == field.gameObject) es.SetSelectedGameObject(null);
            if (!focused) return;
            focused = false;
            if (ReferenceEquals(focusedField, this)) focusedField = null;
            AvKit.ReleaseKeyboardGuard();
            ReleasePause();
        }

        /// <summary>Lets any field go now, the pause key included (review R5: the room opening records the pause key as the player had
        /// it, so a release a frame later must not turn it on under the room and leave it off after).</summary>
        public static void BlurAny()
        {
            focusedField?.Blur();
            if (focusedField == null && pauseHeld) ReleasePause();
        }

        /// <summary>Every frame (WmcPanel.Tick): a pending blur after Enter, and the pause key back one frame after the edit ended.</summary>
        public static void TickAll()
        {
            if (focusedField != null && focusedField.blurPending) focusedField.Blur();
            if (pauseReleaseFrame >= 0 && Time.frameCount >= pauseReleaseFrame && focusedField == null) ReleasePause();
        }

        private static void ReleasePause()
        {
            pauseReleaseFrame = -1;
            if (!pauseHeld) return;
            pauseHeld = false;
            GameplayUI.AllowPauseKeybind = pauseWas;
        }
    }
}
