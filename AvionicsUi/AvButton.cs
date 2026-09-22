using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Tactile 4-state avionics button component.
    /// Manages enabled, latched, hover, and pressed states simultaneously, applies input isolation,
    /// and publishes hover tooltips to the panel status strip.
    /// </summary>
    public class AvButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
                            IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private AvButtonStyle style;
        private Image fill;
        private Image[] frame;
        private Image underline;
        private TMP_Text label;
        private Action onClick;

        private bool latched;
        private bool interactable = true;
        private bool hovered;
        private bool pressed;
        private bool decorated;

        private Image rowFill;
        private Color rowRest;
        private Color rowHover;

        private string tooltip;

        /// <summary>Current control description under the mouse pointer, or null when idle.</summary>
        public static string HoveredTooltip { get; private set; }

        public static void ClearTooltip() => HoveredTooltip = null;

        public static void PublishExternal(string text, bool entering)
        {
            if (entering)
            {
                if (!string.IsNullOrEmpty(text)) HoveredTooltip = text;
            }
            else if (!string.IsNullOrEmpty(text) && HoveredTooltip == text)
            {
                HoveredTooltip = null;
            }
        }

        public AvButton WithTooltip(string text)
        {
            string previous = tooltip;
            tooltip = text;
            if (hovered && HoveredTooltip == previous)
                HoveredTooltip = string.IsNullOrEmpty(tooltip) ? null : tooltip;
            return this;
        }

        private void PublishTooltip(bool entering)
        {
            if (entering)
            {
                if (!string.IsNullOrEmpty(tooltip)) HoveredTooltip = tooltip;
            }
            else if (!string.IsNullOrEmpty(tooltip) && HoveredTooltip == tooltip)
            {
                HoveredTooltip = null;
            }
        }

        public void Initialise(AvButtonStyle style, Image fill, Image[] frame,
                               Image underline, TMP_Text label, Action onClick)
        {
            this.style = style;
            this.fill = fill;
            this.frame = frame;
            this.underline = underline;
            this.label = label;
            this.onClick = onClick;
            decorated = true;
            Apply();
        }

        public void InitialiseHit(Action onClick)
        {
            this.onClick = onClick;
            decorated = false;
        }

        public void SetRowHighlight(Image target, Color rest, Color hover)
        {
            rowFill = target;
            rowRest = rest;
            rowHover = hover;
            if (rowFill != null) rowFill.color = hovered && interactable ? rowHover : rowRest;
        }

        public void SetLatched(bool on)
        {
            if (latched == on) return;
            latched = on;
            Apply();
        }

        public void SetEnabled(bool on)
        {
            if (interactable == on) return;
            interactable = on;
            if (!interactable) { hovered = false; pressed = false; }
            Apply();
        }

        public void SetAction(Action action) => onClick = action;

        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        private AvPaletteInputs PaletteInputs => new AvPaletteInputs
        {
            Accent = AvTheme.Accent.ToRgba(),
            Alert = AvTheme.Alert.ToRgba(),
            Frame = AvTokens.Frame,
            Dim = AvTokens.TextDim,
            Disabled = AvTokens.TextMuted,
        };

        private bool hasCustomColors;
        private Color customFill;
        private Color customFrame;
        private Color customText;

        public void SetCustomColors(Color fill, Color frame, Color text)
        {
            hasCustomColors = true;
            customFill = fill;
            customFrame = frame;
            customText = text;
            Apply();
        }

        public void ClearCustomColors()
        {
            if (!hasCustomColors) return;
            hasCustomColors = false;
            Apply();
        }

        public void Apply()
        {
            if (rowFill != null) rowFill.color = hovered && interactable ? rowHover : rowRest;
            if (!decorated) return;

            if (hasCustomColors && interactable)
            {
                // A semantic tint must not turn off the shared pointer feedback.
                float lift = pressed ? 0.18f : hovered ? 0.08f : 0f;
                if (fill != null) fill.color = Color.Lerp(customFill, Color.white, lift);
                if (label != null) label.color = hovered || pressed ? Color.white : customText;
                if (frame != null)
                {
                    for (int i = 0; i < frame.Length; i++)
                        if (frame[i] != null) frame[i].color = hovered || pressed ? Color.white : customFrame;
                }
                if (underline != null) underline.color = latched ? customFrame : Color.clear;
                return;
            }

            AvButtonPaint paint = AvTokens.Paint(style, PaletteInputs, interactable, latched, hovered, pressed);

            if (fill != null) fill.color = AvTheme.Unity(paint.Fill);
            if (label != null) label.color = AvTheme.Unity(paint.Text);

            if (frame != null)
            {
                Color edgeColor = AvTheme.Unity(paint.Frame);
                for (int i = 0; i < frame.Length; i++)
                {
                    if (frame[i] != null) frame[i].color = edgeColor;
                }
            }

            if (underline != null)
            {
                underline.color = latched && interactable ? AvTheme.Unity(paint.Frame) : Color.clear;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!interactable) return;

            AvInput.Deselect(gameObject);

            try { onClick?.Invoke(); }
            catch (Exception e) { Debug.LogError("Avionics button click failed: " + e); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            PublishTooltip(entering: true);
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            PublishTooltip(entering: false);
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            pressed = false;
            Apply();
        }

#pragma warning disable IDE0051 // Unity message
        private void OnDisable()
#pragma warning restore IDE0051
        {
            if (!hovered && !pressed) return;
            hovered = false;
            pressed = false;
            PublishTooltip(entering: false);
            Apply();
        }
    }

    /// <summary>
    /// Publishes status-strip help for interactive areas that are not avionics buttons,
    /// and optionally tints a row background while the pointer is inside the area.
    ///
    /// <para>A row is read as a row, not as its control: the same help text has to appear
    /// whether the pointer is over the label or the value box, and the row should react as
    /// one target.</para>
    /// </summary>
    public sealed class AvTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private string tooltip;
        private bool hovered;
        private Graphic tint;
        private Color tintRest;
        private Color tintHover;

        public void Initialise(string text) => tooltip = text;

        /// <summary>Help text can change with availability; refresh it without re-hovering.</summary>
        public void SetText(string text) => tooltip = text;

        /// <summary>Optional background that lights on hover, so the whole row reads as one control.</summary>
        public void SetTint(Graphic target, Color rest, Color hover)
        {
            tint = target;
            tintRest = rest;
            tintHover = hover;
            if (tint != null && !hovered) tint.color = rest;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (tint != null) tint.color = tintHover;
            AvButton.PublishExternal(tooltip, entering: true);
        }

        /// <summary>Rest and hover tint for a state that changes after build, e.g. a latched row.</summary>
        public void SetColors(Color rest, Color hover)
        {
            tintRest = rest;
            tintHover = hover;
            if (tint != null && !hovered) tint.color = rest;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            if (tint != null) tint.color = tintRest;
            AvButton.PublishExternal(tooltip, entering: false);
        }

#pragma warning disable IDE0051 // Unity message
        private void OnDisable()
#pragma warning restore IDE0051
        {
            if (!hovered) return;
            hovered = false;
            if (tint != null) tint.color = tintRest;
            AvButton.PublishExternal(tooltip, entering: false);
        }
    }
}
