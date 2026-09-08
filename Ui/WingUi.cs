global using UiButtonStyle = NOAvionics.AvButtonStyle;
using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Wing widget adapter over shared NOAvionics controls, styling, and guarded input.</summary>
    internal static class WingUi
    {
        // Shared spacing tokens.
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

        // Standard button widths.
        public const float ButtonCompact = 44f;
        public const float ButtonAction = 104f;
        public const float ButtonPrimary = 132f;

        // Shared text sizes.
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

        // Shared theme colours.
        public static Color Green => AvTheme.Accent;
        public static Color Grey => Color.grey;
        public static Color Friendly => AvTheme.Friendly;
        public static Color Warning => AvTheme.Warning;
        public static Color Alert => AvTheme.Alert;
        public static Color Dim => AvTheme.Dim;
        public static Color Disabled => AvTheme.Disabled;
        public static Color FrameColor => AvTheme.Frame;
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

        // Widget helpers.
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

    }

    /// <summary>AvButton subclass retaining WingButton type compatibility.</summary>
    internal class WingButton : AvButton
    {
        public new WingButton WithTooltip(string text)
        {
            base.WithTooltip(text);
            return this;
        }
    }
}
