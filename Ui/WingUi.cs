global using UiButtonStyle = NOAvionics.AvButtonStyle;
using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Unity invokes OnDisable by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>
    /// The Wing Command widget bridge over NOAvionics and NOAvionics.Ui.
    /// Provides chamfered bezels, tactile cards, segmented tabs, and input-guarded controls.
    /// </summary>
    internal static class WingUi
    {
        // ------------------------------------------------------------------- spacing
        public const float Space1 = AvTokens.Space1;
        public const float Space2 = AvTokens.Space2;
        public const float Space3 = AvTokens.Space3;
        public const float Space4 = AvTokens.Space4;
        public const float Space5 = AvTokens.Space5;
        public const float Space6 = AvTokens.Space6;

        public const float RowHeight = AvTokens.RowHeight;
        public const float TabHeight = AvTokens.TabBarHeight;
        public const float Gap = AvTokens.Gap;
        public const float Pad = AvTokens.Pad;
        public const float RowPitch = AvTokens.RowPitch;

        // Button widths
        public const float ButtonCompact = 44f;
        public const float ButtonAction = 104f;
        public const float ButtonPrimary = 132f;

        // ---------------------------------------------------------------------- type
        public const float FontMicro = AvTokens.FontMicro;
        public const float FontSmall = AvTokens.FontSmall;
        public const float FontBody = AvTokens.FontBody;
        public const float FontLead = AvTokens.FontLead;
        public const float FontTitle = AvTokens.FontTitle;

        public static TMP_FontAsset Font
        {
            get => AvFont.Font;
            set => AvFont.Font = value;
        }

        public static void Reset()
        {
            AvFont.Reset();
            AvSprites.Reset();
        }

        // ------------------------------------------------------------------- colours
        public static Color Green => AvTheme.Accent;
        public static Color Grey => Color.grey;
        public static Color Friendly => AvTheme.Friendly;
        public static Color Warning => AvTheme.Warning;
        public static Color Alert => AvTheme.Alert;
        public static Color Dim => AvTheme.Dim;
        public static Color Disabled => AvTheme.Disabled;
        public static Color FrameColor => AvTheme.Frame;

        public static Color HeadingColor
        {
            get
            {
                Color c = Friendly;
                return new Color(c.r * 0.88f, c.g * 0.88f, c.b * 0.88f, 1f);
            }
        }

        public static Color PanelBackground => AvTheme.Ground;
        public static Color PanelEdge => AvTheme.Unity(AvTokens.PanelEdge);
        public static Color PanelShadow => AvTheme.Unity(AvTokens.PanelShadow);

        public static Color CardFill => new Color(0f, 0f, 0f, AvTokens.RowRestShade);
        public static Color CardFillHover =>
            AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha));
        public static Color CardFillSelected =>
            AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.RowSelectedScale, AvTokens.RowSelectedAlpha));

        public static Color SurfaceCard => AvTheme.Surface;
        public static Color BorderSubtle => AvTheme.Hairline;
        public static Color RailEmerald => AvTheme.RailReady;
        public static Color RailCyan => AvTheme.RailInfo;
        public static Color RailInert => AvTheme.RailInert;
        public static Color TextPrimary => AvTheme.Unity(AvTokens.TextPrimary);

        // ------------------------------------------------------------------- widgets
        public static void Place(RectTransform target, Rect rect) => AvKit.Place(target, rect);

        public static void Stretch(RectTransform target) => AvKit.Stretch(target);

        public static (Image CardFill, Image Rail) TacticalCard(
            RectTransform parent, Rect rect, Color railColor, bool hasRail = true) =>
            AvKit.TacticalCard(parent, rect, railColor, hasRail);

        public static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                     Color color, float size, FontStyles style,
                                     TextAlignmentOptions align) =>
            AvKit.Label(parent, text, rect, color, size, style, align, wrap: false);

        public static Image Panel(RectTransform parent, Rect rect, Color color) =>
            AvKit.Panel(parent, rect, color, AvSprites.Control);

        public static Image Rule(RectTransform parent, Rect rect, Color color) =>
            AvKit.Rule(parent, rect, color);

        public static Image[] Outline(RectTransform parent, Rect rect, Color color) =>
            AvKit.Outline(parent, rect, color);

        public static void CornerTicks(RectTransform parent, Rect rect, Color color, float len = 6f) =>
            AvKit.CornerTicks(parent, rect, color, len);

        public static float Heading(RectTransform parent, float y, string text, float panelWidth = AvTokens.PanelWidth) =>
            AvKit.Heading(parent, y, text, panelWidth);

        public static (Image Background, TMP_Text Label) StatusChip(
            RectTransform parent, string text, Rect rect, Color railColor, Color textColor,
            float fontSize = FontMicro) => AvKit.StatusChip(parent, text, rect, railColor, textColor, fontSize);

        public static WingButton Button(RectTransform parent, string text, Rect rect, Action onClick) =>
            Button(parent, text, rect, onClick, FontBody, AvButtonStyle.Default);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        Action onClick, AvButtonStyle style) =>
            Button(parent, text, rect, onClick, FontBody, style);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        float fontSize, Action onClick) =>
            Button(parent, text, rect, onClick, fontSize, AvButtonStyle.Default);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        float fontSize, AvButtonStyle style, Action onClick) =>
            Button(parent, text, rect, onClick, fontSize, style);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        Action onClick, float fontSize, AvButtonStyle style)
        {
            var go = new GameObject("WingButton", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image fill = go.GetComponent<Image>();
            fill.sprite = AvSprites.Control;
            fill.type = Image.Type.Sliced;
            fill.raycastTarget = true;

            Image[] frame = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor);
            Image underline = style == AvButtonStyle.Tab
                ? Rule(rt, new Rect(0f, -(rect.height - 2f), rect.width, 2f), Color.clear)
                : null;

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                                   Green, fontSize, FontStyles.Bold, TextAlignmentOptions.Center);

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.Initialise(style, fill, frame, underline, label, onClick);
            return behaviour;
        }

        public static WingButton HitButton(RectTransform parent, Rect rect, Action onClick)
        {
            var go = new GameObject("HitTarget", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.InitialiseHit(onClick);
            return behaviour;
        }

        public static WingButton Tab(RectTransform parent, string text, Rect rect, Action onClick) =>
            Button(parent, text, rect, onClick, FontSmall, AvButtonStyle.Tab);

        public static WingButton[] Stepper(RectTransform parent, float x, float y, float w,
                                           out TMP_Text valueLabel, Action onPrev, Action onNext,
                                           string tooltip = null)
        {
            Panel(parent, new Rect(x, y, w, RowHeight), AvTheme.SurfaceInert);
            Outline(parent, new Rect(x, y, w, RowHeight), FrameColor);

            const float arrowWidth = Space6 + Space1;
            WingButton prev = Button(parent, "<", new Rect(x + 1f, y - 1f, arrowWidth, RowHeight - 2f),
                                     onPrev, FontBody, AvButtonStyle.Quiet);
            WingButton next = Button(parent, ">", new Rect(x + w - arrowWidth - 1f, y - 1f, arrowWidth, RowHeight - 2f),
                                     onNext, FontBody, AvButtonStyle.Quiet);

            valueLabel = Label(parent, "", new Rect(x + arrowWidth + Space2, y, w - (arrowWidth + Space2) * 2f, RowHeight),
                               TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);

            if (!string.IsNullOrEmpty(tooltip))
            {
                prev.WithTooltip(tooltip);
                next.WithTooltip(tooltip);
            }

            return new[] { prev, next };
        }

        public static WingButton Pager(RectTransform parent, float y, string glyph, float panelWidth, Action onClick, string tooltip = null)
        {
            const float arrowWidth = 34f;
            float x = glyph == "<" ? Pad : panelWidth - Pad - arrowWidth;
            WingButton btn = Button(parent, glyph, new Rect(x, y, arrowWidth, RowHeight),
                                    onClick, FontBody, AvButtonStyle.Quiet);
            if (!string.IsNullOrEmpty(tooltip)) btn.WithTooltip(tooltip);
            return btn;
        }

        public static TMP_Text PagerLabel(RectTransform parent, float y, float panelWidth, float arrowWidth = 34f) =>
            AvKit.PagerLabel(parent, y, panelWidth, arrowWidth);

        public struct ColumnHeader
        {
            public string Text;
            public float X;
            public float Width;
            public bool RightAligned;

            public ColumnHeader(string text, float x, float width, bool rightAligned = false)
            {
                Text = text;
                X = x;
                Width = width;
                RightAligned = rightAligned;
            }
        }

        public static float ColumnHeaders(RectTransform parent, float y, ColumnHeader[] columns)
        {
            foreach (ColumnHeader col in columns)
            {
                Label(parent, col.Text, new Rect(Pad + col.X, y, col.Width, Space4),
                      Dim, FontMicro, FontStyles.Normal,
                      col.RightAligned ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            }
            return y - Space4;
        }

        public static Image ProgressBar(RectTransform parent, Rect rect, float percent, Color fillCol) =>
            AvKit.ProgressBar(parent, rect, percent, fillCol);

        public static void PipMeter(RectTransform parent, Rect rect, int filled, int total, Color activeColor, Color emptyColor) =>
            AvKit.PipMeter(parent, rect, filled, total, activeColor, emptyColor);

        public static TMP_Text StatusStrip(RectTransform parent, Rect rect, Color? railColor = null) =>
            AvKit.StatusStrip(parent, rect, railColor);

        public static TMP_InputField InputField(RectTransform parent, Rect rect, int characterLimit,
                                                Action<string> onChanged, string tooltip = null,
                                                string placeholderText = "NAME")
        {
            return AvKit.InputField(parent, rect, characterLimit, onChanged,
                                    onFocus: WingKeyboardGuard.Capture,
                                    onBlur: WingKeyboardGuard.Release,
                                    tooltip: tooltip, placeholderText: placeholderText);
        }

        public static Sprite PanelSprite() => AvSprites.Panel;
        public static Sprite CardSprite() => AvSprites.Card;
        public static Sprite ControlSprite() => AvSprites.Control;

        public static Color Unity(Rgba c) => AvTheme.Unity(c);
        public static Rgba Rgba(Color c) => c.ToRgba();
        public static string Truncate(string s, int max) => AvTheme.Truncate(s, max);
    }

    /// <summary>
    /// Wing Command button extending AvButton to preserve WingButton type compatibility.
    /// </summary>
    internal class WingButton : AvButton
    {
        public new WingButton WithTooltip(string text)
        {
            base.WithTooltip(text);
            return this;
        }
    }
}
