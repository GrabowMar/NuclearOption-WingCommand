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
            tooltip = text;
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

        public void Apply()
        {
            if (rowFill != null) rowFill.color = hovered && interactable ? rowHover : rowRest;
            if (!decorated) return;

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
}
