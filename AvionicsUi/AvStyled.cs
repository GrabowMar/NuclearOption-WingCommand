using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Draws the components of the "instrument stack" language, taking every visual
    /// decision from the stylesheet rather than from a literal at the call site.
    ///
    /// The rule these are built on: a panel is a spine with sections hanging off it, not
    /// a wall of bordered boxes. <see cref="AvKit.TacticalCard"/> — outline plus corner
    /// ticks on every group — is still available for the surfaces that genuinely are
    /// objects, but ordinary grouping now uses <see cref="Section"/>, whose only
    /// decoration is a tick on the spine and, on alternate sections, a faint band.
    /// </summary>
    public static class AvStyled
    {
        // ------------------------------------------------------------------ primitives

        /// <summary>Paint a box's background, border, sprite and corner ticks from its style.</summary>
        public static Image Box(RectTransform parent, Rect area, string classes, string state = null)
        {
            AvStyle style = AvStyleHost.Style(classes, state);
            Image fill = null;

            if (style.Background.HasValue)
            {
                fill = AvKit.Panel(parent, area,
                                   AvStyleHost.Resolve(style.Background, Color.clear),
                                   AvStyleHost.ResolveSprite(style.Sprite));
            }

            if (style.Border.HasValue && style.BorderWidth > 0f)
                AvKit.Outline(parent, area, AvStyleHost.Resolve(style.Border, AvTheme.Hairline));

            if (style.HasTicks && style.Ticks)
                AvKit.CornerTicks(parent, area, AvStyleHost.Resolve(style.Border, AvTheme.Hairline));

            return fill;
        }

        /// <summary>A label whose size, weight, colour, tracking and wrap all come from the sheet.</summary>
        public static TMP_Text Label(
            RectTransform parent, Rect area, string text, string classes,
            string state = null, TextAlignmentOptions? align = null)
        {
            AvStyle style = AvStyleHost.Style(classes, state);

            TMP_Text label = AvKit.Label(
                parent, text ?? "", area,
                AvStyleHost.Resolve(style.Color, AvTheme.TextPrimary),
                style.HasFont ? style.FontSize : AvTokens.FontBody,
                style.Bold ? FontStyles.Bold : FontStyles.Normal,
                align ?? (style.HasAlign ? AvStyleHost.ResolveAlign(style.Align) : TextAlignmentOptions.Left),
                style.Wrap);

            if (style.Tracking != 0f) label.characterSpacing = style.Tracking;
            // Tabular figures come from the game font itself; TMP exposes no runtime
            // feature toggle in this version, so the flag is honoured by choosing
            // fixed-width columns at the call site instead.


            // Vertical alignment: a one-line label in a taller box should sit on the
            // middle, not hang off the top, which is what made the old rows look ragged.
            if (!style.Wrap) label.alignment = Midline(label.alignment);

            return label;
        }

        private static TextAlignmentOptions Midline(TextAlignmentOptions a)
        {
            switch (a)
            {
                case TextAlignmentOptions.Right: return TextAlignmentOptions.MidlineRight;
                case TextAlignmentOptions.Center: return TextAlignmentOptions.Center;
                default: return TextAlignmentOptions.MidlineLeft;
            }
        }

        /// <summary>Draw a laid-out text node using the string and classes it was built with.</summary>
        public static TMP_Text Draw(RectTransform parent, AvNode node, string state = null)
        {
            string text = node.TextOf();
            return text == null ? null : Label(parent, node.Rect.ToUnity(), text, node.Classes, state);
        }

        /// <summary>A status rail: 3px by default, coloured by a state class.</summary>
        public static Image Rail(RectTransform parent, Rect area, string stateClass)
        {
            AvStyle style = AvStyleHost.Style("rail " + stateClass);
            float w = style.RailWidth > 0f ? style.RailWidth
                    : style.HasWidth ? style.Width
                    : 3f;
            return AvKit.Rule(parent, new Rect(area.x, area.y, w, area.height),
                              AvStyleHost.Resolve(style.Background, AvTheme.RailInert));
        }

        // ---------------------------------------------------------------------- spine

        /// <summary>
        /// The accent stroke a page's sections hang off. Drawn once, full content height.
        /// </summary>
        public static Image Spine(RectTransform parent, Rect content)
        {
            AvStyle style = AvStyleHost.Style("spine");
            float w = style.HasWidth ? style.Width : 3f;
            return AvKit.Rule(parent, new Rect(content.x, content.y, w, content.height),
                              AvStyleHost.Resolve(style.Background, AvTheme.Accent));
        }

        /// <summary>The short mark that ties one section back to the spine.</summary>
        public static void SpineTick(RectTransform parent, float spineX, float y)
        {
            AvStyle style = AvStyleHost.Style("spine-tick");
            float w = style.HasWidth ? style.Width : 9f;
            float h = style.HasHeight ? style.Height : 1f;
            AvKit.Rule(parent, new Rect(spineX, y, w, h),
                       AvStyleHost.Resolve(style.Background, AvTheme.Accent));
        }

        // -------------------------------------------------------------------- section

        /// <summary>
        /// A section: an optional band, a spine tick, a title and a right-hand note.
        /// Returns the inner rect the section's content should be laid into.
        /// </summary>
        public static Rect Section(
            RectTransform parent, Rect area, string title, string note = null, bool band = false)
        {
            string classes = band ? "section band" : "section";
            AvStyle style = AvStyleHost.Style(classes);

            if (style.Background.HasValue)
                AvKit.Panel(parent, area, AvStyleHost.Resolve(style.Background, Color.clear));

            SpineTick(parent, area.x - AvTokens.Pad, area.y - 15f);

            float padL = style.HasPad ? style.PadLeft : 12f;
            float padT = style.HasPad ? style.PadTop : 12f;
            float padR = style.HasPad ? style.PadRight : 14f;
            float padB = style.HasPad ? style.PadBottom : 14f;

            float innerX = area.x + padL;
            float innerW = Mathf.Max(0f, area.width - padL - padR);
            float y = area.y - padT;

            if (!string.IsNullOrEmpty(title))
            {
                TMP_Text label = Label(parent, new Rect(innerX, y, innerW, 14f), title, "section-title");

                if (!string.IsNullOrEmpty(note))
                {
                    float titleWidth = Mathf.Ceil(label.GetPreferredValues(title).x);
                    float noteX = innerX + titleWidth + AvTokens.Space2;
                    Label(parent, new Rect(noteX, y, Mathf.Max(0f, innerW - titleWidth - AvTokens.Space2), 14f),
                          note, "section-title-note");
                }

                y -= 14f + AvTokens.Space2;
            }

            return new Rect(innerX, y, innerW, Mathf.Max(0f, area.Bottom() - y - padB));
        }

        private static float Bottom(this Rect r) => r.y - r.height;

        // ------------------------------------------------------------------- data bar

        /// <summary>
        /// The hard top strip: a filled id tag, the live state, and the status chips.
        /// Replaces the centred title, subtitle and separate chip rail all three panels
        /// used to carry, which cost roughly 24px of height for no information.
        /// </summary>
        public static DataBar TopBar(
            RectTransform parent, Rect area, string id, int chipCount)
        {
            Box(parent, area, "databar");

            AvStyle tag = AvStyleHost.Style("id-tag");
            float tagWidth = 12f + id.Length * 11f + 12f;

            AvKit.Panel(parent, new Rect(area.x, area.y, tagWidth, area.height),
                        AvStyleHost.Resolve(tag.Background, AvTheme.Accent));
            Label(parent, new Rect(area.x, area.y, tagWidth, area.height), id, "id-tag",
                  align: TextAlignmentOptions.Center);

            var bar = new DataBar { Chips = new TMP_Text[chipCount], ChipBoxes = new Image[chipCount] };

            const float chipWidth = 74f;
            const float chipGap = 2f;
            float chipsWidth = chipCount * chipWidth + Mathf.Max(0, chipCount - 1) * chipGap;
            float chipsX = area.x + area.width - chipsWidth - 6f;

            float stateX = area.x + tagWidth;
            bar.State = Label(parent,
                              new Rect(stateX, area.y, Mathf.Max(0f, chipsX - stateX - 8f), area.height),
                              "", "databar-state");

            for (int i = 0; i < chipCount; i++)
            {
                var chipRect = new Rect(chipsX + i * (chipWidth + chipGap),
                                        area.y - (area.height - 16f) * 0.5f, chipWidth, 16f);
                bar.ChipBoxes[i] = Box(parent, chipRect, "chip");
                bar.Chips[i] = Label(parent, chipRect, "", "chip", align: TextAlignmentOptions.Center);
            }

            return bar;
        }

        /// <summary>The mutable parts of a <see cref="TopBar"/>, kept for the refresh pass.</summary>
        public sealed class DataBar
        {
            public TMP_Text State;
            public TMP_Text[] Chips;
            public Image[] ChipBoxes;

            /// <summary>Set a chip's text and whether it reads as live.</summary>
            public void SetChip(int index, string text, bool live)
            {
                if (index < 0 || index >= Chips.Length) return;
                Chips[index].text = text;

                AvStyle style = AvStyleHost.Style(live ? "chip live" : "chip");
                Chips[index].color = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
            }
        }

        // -------------------------------------------------------------------- metrics

        /// <summary>
        /// One display metric: a small key, a large tabular value, its unit, and a track.
        ///
        /// These are what the panels are for. They used to be 12px body text buried inside
        /// whichever tab happened to own them; here they sit above the tabs, at a size that
        /// says so.
        /// </summary>
        public static Metric MetricCell(RectTransform parent, Rect area, string key, string unit)
        {
            AvStyle style = AvStyleHost.Style("metric");
            float padL = style.HasPad ? style.PadLeft : 14f;
            float padT = style.HasPad ? style.PadTop : 9f;
            float padR = style.HasPad ? style.PadRight : 14f;

            float x = area.x + padL;
            float w = Mathf.Max(0f, area.width - padL - padR);
            float y = area.y - padT;

            var metric = new Metric();

            Label(parent, new Rect(x, y, w, 11f), key, "metric-key");
            y -= 12f;

            metric.Value = Label(parent, new Rect(x, y, w * 0.72f, 26f), "—", "metric-value");
            metric.Unit = Label(parent, new Rect(x, y, w, 26f), unit, "metric-unit",
                                align: TextAlignmentOptions.Right);
            y -= 28f;

            metric.Caption = Label(parent, new Rect(x, y, w, 11f), "", "metric-cap");
            y -= 12f;

            AvStyle track = AvStyleHost.Style("metric-track");
            float th = track.HasHeight ? track.Height : 3f;
            AvKit.Panel(parent, new Rect(x, y, w, th),
                        AvStyleHost.Resolve(track.Background, AvTheme.Unity(AvTokens.Hairline)));
            metric.Fill = AvKit.Panel(parent, new Rect(x, y, 0f, th), AvTheme.Accent);
            metric.TrackWidth = w;
            metric.TrackHeight = th;

            return metric;
        }

        /// <summary>The mutable parts of a <see cref="MetricCell"/>.</summary>
        public sealed class Metric
        {
            public TMP_Text Value;
            public TMP_Text Unit;
            public TMP_Text Caption;
            public Image Fill;
            public float TrackWidth;
            public float TrackHeight;

            public void Set(string value, string caption, float fraction, Color fill)
            {
                Value.text = value;
                Caption.text = caption ?? "";
                Fill.color = fill;

                RectTransform rt = Fill.rectTransform;
                rt.sizeDelta = new Vector2(Mathf.Clamp01(fraction) * TrackWidth, TrackHeight);
            }
        }

        // -------------------------------------------------------------- status strip

        /// <summary>
        /// The bottom strip. Shows the hovered control's tooltip, else an armed picker's
        /// prompt, else idle copy — and it is where a disabled control says why.
        /// </summary>
        public static TMP_Text StatusStrip(RectTransform parent, Rect area)
        {
            Box(parent, area, "status");
            AvStyle style = AvStyleHost.Style("status");
            float padL = style.HasPad ? style.PadLeft : 14f;
            float padT = style.HasPad ? style.PadTop : 9f;

            return Label(parent,
                         new Rect(area.x + padL, area.y - padT,
                                  Mathf.Max(0f, area.width - padL * 2f),
                                  Mathf.Max(0f, area.height - padT * 2f)),
                         "", "status-text");
        }

        // -------------------------------------------------------------------- buttons

        /// <summary>A button whose paint comes from the sheet rather than the token palette.</summary>
        public static AvButton Button(
            RectTransform parent, Rect area, string text, string classes, Action onClick,
            AvButtonStyle style = AvButtonStyle.Default)
        {
            AvStyle declared = AvStyleHost.Style(classes);
            float size = declared.HasFont ? declared.FontSize : AvTokens.FontMicro;

            AvButton button = AvKit.Button(parent, text, area, onClick, size, style);
            return button;
        }
    }
}
