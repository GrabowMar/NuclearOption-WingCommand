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

            // Styled boxes never need four independently movable edges. One cached
            // sliced frame keeps the same border cue with fewer Canvas objects.
            if (style.Border.HasValue && style.BorderWidth > 0f)
                AvKit.Panel(parent, area, AvStyleHost.Resolve(style.Border, AvTheme.Hairline),
                            AvSprites.ControlFrame);

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

        /// <summary>
        /// Keep the horizontal alignment a caller asked for and only drop the vertical one,
        /// so a one-line label sits on the middle of its box. Mapping the midline variants by
        /// exclusion sent <c>MidlineRight</c> and <c>TopRight</c> to the left edge, which is
        /// how a form number ended up printed over the title it was supposed to sit opposite.
        /// </summary>
        private static TextAlignmentOptions Midline(TextAlignmentOptions a)
        {
            switch (a)
            {
                case TextAlignmentOptions.Right:
                case TextAlignmentOptions.MidlineRight:
                case TextAlignmentOptions.TopRight:
                case TextAlignmentOptions.BottomRight:
                    return TextAlignmentOptions.MidlineRight;
                case TextAlignmentOptions.Center:
                case TextAlignmentOptions.Midline:
                case TextAlignmentOptions.Top:
                case TextAlignmentOptions.Bottom:
                    return TextAlignmentOptions.Center;
                default:
                    return TextAlignmentOptions.MidlineLeft;
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

            return new Rect(innerX, y, innerW, Mathf.Max(0f, y - area.Bottom() - padB));
        }

        private static float Bottom(this Rect r) => r.y - r.height;

        // ------------------------------------------------------------------- data bar

        /// <summary>
        /// Screen identity and telemetry have separate lanes. Small overlay headers keep
        /// the compact one-line form; full MFD screens give the title the complete top row.
        /// </summary>
        public static DataBar TopBar(
            RectTransform parent, Rect area, string id, int chipCount)
        {
            Box(parent, area, "databar");

            bool twoRows = area.height >= AvTokens.ScreenHeaderHeight;
            float titleHeight = twoRows ? 26f : area.height;
            if (twoRows)
                AvKit.Rule(parent, new Rect(area.x, area.y - titleHeight - 1f, area.width, 1f),
                           AvTheme.Frame.WithAlpha(0.72f));
            float tagWidth = 20f + id.Length * 9f;
            Box(parent, new Rect(area.x, area.y, tagWidth, titleHeight), "id-plate");
            AvKit.Rule(parent, new Rect(area.x, area.y, 3f, titleHeight), AvTheme.RailInfo);
            Label(parent, new Rect(area.x + 5f, area.y, tagWidth - 5f, titleHeight), id, "id-tag",
                  align: TextAlignmentOptions.Center);

            var bar = new DataBar
            {
                Chips = new TMP_Text[chipCount],
                ChipBoxes = new Image[chipCount],
                ChipRails = new Image[chipCount],
            };
            if (twoRows)
                bar.PageIndex = Label(parent,
                    new Rect(area.x + area.width - 48f, area.y, 48f, titleHeight),
                    "", "databar-state-key", align: TextAlignmentOptions.MidlineRight);

            const float preferredChipWidth = 82f;
            const float chipGap = AvTokens.Space1;
            const float stateGap = 8f;
            float gapsWidth = Mathf.Max(0, chipCount - 1) * chipGap;
            float chipWidth = chipCount <= 0 ? 0f : twoRows
                ? Mathf.Max(0f, (area.width - gapsWidth) / chipCount)
                : Mathf.Min(preferredChipWidth, Mathf.Max(0f, (area.width - tagWidth - 140f - stateGap - gapsWidth) / chipCount));
            float chipsWidth = chipCount * chipWidth + gapsWidth;
            float chipsX = twoRows ? area.x : area.x + area.width - chipsWidth;
            float stateX = area.x + tagWidth + stateGap;
            float stateRight = twoRows ? area.x + area.width - 54f
                : chipCount == 0 ? area.x + area.width : chipsX - stateGap;
            bar.State = Label(parent,
                              new Rect(stateX, area.y, Mathf.Max(0f, stateRight - stateX), titleHeight),
                              "", "databar-state");
            bar.State.enableWordWrapping = false;
            bar.State.enableAutoSizing = true;
            bar.State.fontSizeMin = AvTokens.FontMicro;
            bar.State.fontSizeMax = bar.State.fontSize;
            bar.State.overflowMode = TextOverflowModes.Ellipsis;

            for (int i = 0; i < chipCount; i++)
            {
                var chipRect = new Rect(chipsX + i * (chipWidth + chipGap),
                                        twoRows ? area.y - 32f : area.y - (area.height - 20f) * 0.5f,
                                        chipWidth, 20f);
                bar.ChipBoxes[i] = Box(parent, chipRect, "chip");
                bar.ChipRails[i] = AvKit.Rule(parent,
                    new Rect(chipRect.x + 6f, chipRect.y - 7f, 4f, 6f), AvTheme.RailInert);
                bar.Chips[i] = Label(parent,
                    new Rect(chipRect.x + 16f, chipRect.y, Mathf.Max(0f, chipWidth - 20f), chipRect.height),
                    "", "chip", align: TextAlignmentOptions.Left);
                bar.Chips[i].enableWordWrapping = false;
                bar.Chips[i].enableAutoSizing = true;
                bar.Chips[i].fontSizeMin = AvTokens.FontMicro;
                bar.Chips[i].fontSizeMax = bar.Chips[i].fontSize;
                bar.Chips[i].overflowMode = TextOverflowModes.Ellipsis;
            }

            return bar;
        }

        /// <summary>The mutable parts of a <see cref="TopBar"/>, kept for the refresh pass.</summary>
        public sealed class DataBar
        {
            public TMP_Text State;
            public TMP_Text PageIndex;
            public TMP_Text[] Chips;
            public Image[] ChipBoxes;
            public Image[] ChipRails;

            public void SetPageIndex(int index, int count)
            {
                if (PageIndex != null)
                    PageIndex.text = (index + 1).ToString("00") + "/" + count.ToString("00");
            }

            /// <summary>Set a chip's text and whether it reads as live.</summary>
            public void SetChip(int index, string text, bool live)
            {
                SetChip(index, text, live ? "live" : null);
            }

            /// <summary>Set a chip's semantic state: live, warn, danger, info, or inert.</summary>
            public void SetChip(int index, string text, string state)
            {
                if (index < 0 || index >= Chips.Length) return;
                Chips[index].text = text;

                string classes = string.IsNullOrEmpty(state) ? "chip" : "chip " + state;
                AvStyle style = AvStyleHost.Style(classes);
                Chips[index].color = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
                if (ChipBoxes[index] != null)
                    ChipBoxes[index].color = AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert);
                if (ChipRails[index] != null)
                {
                    ChipRails[index].color = state == "live" ? AvTheme.Accent
                        : state == "warn" ? AvTheme.Warning
                        : state == "danger" ? AvTheme.Alert
                        : state == "info" ? AvTheme.RailInfo
                        : AvTheme.RailInert;
                }
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
            float padL = style.HasPad ? style.PadLeft : 10f;
            float padR = style.HasPad ? style.PadRight : 10f;

            float x = area.x + padL;
            float w = Mathf.Max(0f, area.width - padL - padR);
            float y = area.y - 2f;

            var metric = new Metric();

            Label(parent, new Rect(x, y, w, 11f), key, "metric-key");
            metric.Unit = Label(parent, new Rect(x, y, w, 11f), unit, "metric-unit",
                                align: TextAlignmentOptions.Right);
            y -= 12f;

            metric.Value = Label(parent, new Rect(x, y, w, 18f), "—", "metric-value");
            metric.Value.enableAutoSizing = true;
            metric.Value.fontSizeMin = AvTokens.FontSmall;
            metric.Value.fontSizeMax = metric.Value.fontSize;
            y -= 17f;

            metric.Caption = Label(parent, new Rect(x, y, w, 10f), "", "metric-cap");
            y = area.y - area.height + 2f;

            AvStyle track = AvStyleHost.Style("metric-track");
            float th = track.HasHeight ? track.Height : 2f;
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
        public static TMP_Text StatusStrip(RectTransform parent, Rect area) =>
            StatusStrip(parent, area, out _);

        public static TMP_Text StatusStrip(RectTransform parent, Rect area, out Image rail)
        {
            Box(parent, area, "status");
            AvStyle style = AvStyleHost.Style("status");
            float padL = style.HasPad ? style.PadLeft : 14f;
            float padT = style.HasPad ? style.PadTop : 9f;
            AvKit.Rule(parent, new Rect(area.x, area.y, area.width, 1f), AvTheme.Frame.WithAlpha(0.72f));
            rail = AvKit.Rule(parent, new Rect(area.x, area.y, 3f, area.height), AvTheme.RailInert);

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
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                if (declared.Tracking != 0f) label.characterSpacing = declared.Tracking;
                if (declared.Bold) label.fontStyle = FontStyles.Bold;
            }
            return button;
        }
    }
}
