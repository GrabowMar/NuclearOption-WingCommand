using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>Shared WMC shell geometry, readouts and widget helpers used by every tab.</summary>
    internal static partial class WmcScreen
    {
        // ---------------------------------------------------------------- geometry

        /// <summary>Two-lane identity header. Chips need the full second row or they truncate.</summary>
        internal const float HeaderHeight = 54f;

        /// <summary>One-line key, value, and caption over a 2px track.</summary>
        internal const float MetricsHeight = 32f;

        /// <summary>Content column inside the panel, clear of the 8-unit scroll gutter.</summary>
        internal const float ContentWidth = PageWidth - Pad * 2f;

        /// <summary>Panel-local y of the first body row, below header, metrics and tabs.</summary>
        internal static float BodyTop =>
            -(Pad + HeaderHeight + Space2 + MetricsHeight + Space2 + WingUi.TabHeight + Space2);

        /// <summary>Panel-local y of the pinned status strip top.</summary>
        internal static float StripTop => -(panelHeight - Pad - StatusStripHeight);

        /// <summary>Usable body height between the tab bar and the pinned strip.</summary>
        internal static float BodyHeight => Mathf.Max(0f, BodyTop - StripTop - Space2);

        /// <summary>Panel-local y of the body foot, keeping clearance above the strip.</summary>
        internal static float BodyBottom => StripTop + Space2;

        // ------------------------------------------------------------ status strip

        /// <summary>What a status-strip line is telling the player.</summary>
        internal enum StripKind
        {
            Ambient,
            Help,
            Map,
            Confirm,
            Done,
            Blocked,
        }

        private static readonly Image[] statusRails = new Image[PageCount];

        // --------------------------------------------------------------- widgets

        /// <summary>Section head: accent rail, bold title, optional right-aligned note.</summary>
        internal static float SectionHeader(RectTransform parent, float x, float y, float width,
                                            string title, string note = null, float height = 22f)
        {
            Rule(parent, new Rect(x, y - 4f, 3f, Mathf.Max(6f, height - 8f)), Green());
            Label(parent, title, new Rect(x + 10f, y - 1f, width - 10f, 14f),
                  Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            if (!string.IsNullOrEmpty(note))
                Label(parent, note, new Rect(x + 10f, y - 1f, width - 10f, 14f),
                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            return y - height;
        }

        /// <summary>Two-line control: 12-unit action with a 10-unit consequence cue beneath.</summary>
        internal static WingButton CueButton(RectTransform parent, string title, string cue, Rect rect,
                                             Action onClick, UiButtonStyle style = UiButtonStyle.Default)
        {
            string text = string.IsNullOrEmpty(cue)
                ? title
                : title + "\n<size=10>" + cue + "</size>";
            WingButton button = WingUi.Button(parent, text, rect, FontBody, style, onClick);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.lineSpacing = -4f;
            return button;
        }

        /// <summary>Track plus fill; the fill is resized by <see cref="SetMeter"/>.</summary>
        internal static Image MeterBar(RectTransform parent, Rect rect, out Image fill, Color? fillColor = null)
        {
            Image track = Panel(parent, rect, WingUi.BorderSubtle);
            track.color = track.color.WithAlpha(0.5f);
            fill = Panel(parent, new Rect(rect.x, rect.y, 0f, rect.height), fillColor ?? Green());
            return track;
        }

        internal static void SetMeter(Image fill, float width, float fraction, Color color)
        {
            if (fill == null) return;
            fill.color = color;
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(fraction) * width,
                                                       fill.rectTransform.sizeDelta.y);
        }

        /// <summary>Quiet full-width list pager row: arrow, summary, arrow.</summary>
        internal static (WingButton Prev, TMP_Text Label, WingButton Next) PagerRow(
            RectTransform parent, float y, float width, Action onPrevious, Action onNext,
            string tooltip = null, float rowHeight = 0f)
        {
            float h = rowHeight > 0f ? rowHeight : RowHeight;
            WingButton prev = WingUi.Button(parent, "<", new Rect(0f, y, ArrowWidth, h),
                                            FontBody, UiButtonStyle.Quiet, onPrevious)
                                    .WithTooltip(tooltip ?? OrderHint.Pager);
            TMP_Text label = Label(parent, "", new Rect(ArrowWidth + Gap, y, width - (ArrowWidth + Gap) * 2f, h),
                                   Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            WingButton next = WingUi.Button(parent, ">", new Rect(width - ArrowWidth, y, ArrowWidth, h),
                                            FontBody, UiButtonStyle.Quiet, onNext)
                                    .WithTooltip(tooltip ?? OrderHint.Pager);
            return (prev, label, next);
        }

        /// <summary>Bounded vertical viewport with an auto-hiding gutter scrollbar.</summary>
        internal static RectTransform BuildViewport(RectTransform parent, Rect rect, string name,
                                                    out RectTransform content, out ScrollRect scroll)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect));
            RectTransform viewport = go.GetComponent<RectTransform>();
            viewport.SetParent(parent, worldPositionStays: false);
            Place(viewport, rect);
            go.GetComponent<Image>().color = Color.clear;

            content = PageRoot(viewport, name + "Content");
            Place(content, new Rect(0f, 0f, PageWidth, 1f));

            scroll = go.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RowPitch;
            scroll.inertia = false;

            var track = new GameObject(name + "ScrollTrack", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.SetParent(parent, worldPositionStays: false);
            Place(trackRect, new Rect(PanelWidth - 5f, rect.y, 4f, rect.height));
            track.GetComponent<Image>().color = WingUi.BorderSubtle;

            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            RectTransform thumbRect = thumb.GetComponent<RectTransform>();
            thumbRect.SetParent(trackRect, worldPositionStays: false);
            Stretch(thumbRect);
            thumb.GetComponent<Image>().color = WingUi.Dim;

            var scrollbar = track.GetComponent<Scrollbar>();
            AvInput.StripNavigation(scrollbar);
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = thumbRect;
            scrollbar.targetGraphic = thumb.GetComponent<Image>();
            scrollbar.value = 1f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return viewport;
        }

        /// <summary>Grow viewport content to its measured bottom without moving the scroll offset.</summary>
        internal static void Reflow(ScrollRect scroll, float contentBottom)
        {
            if (scroll == null || scroll.content == null) return;
            float minH = scroll.viewport != null ? scroll.viewport.rect.height : 0f;
            float contentH = Mathf.Max(minH, -contentBottom + Pad);
            scroll.content.sizeDelta = new Vector2(scroll.content.sizeDelta.x, contentH);
            ClampScrollOffset(scroll);
            SyncScrollbar(scroll);
        }

        /// <summary>Keep the scroll inside its bounds after content shrinks; an out-of-bounds offset
        /// also corrupts the thumb fraction.</summary>
        internal static void ClampScrollOffset(ScrollRect scroll)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            float maxOffset = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
            Vector2 position = scroll.content.anchoredPosition;
            scroll.content.anchoredPosition = new Vector2(position.x, Mathf.Clamp(position.y, 0f, maxOffset));
        }

        /// <summary>
        /// Keep the thumb fraction truthful after a programmatic content resize; the ScrollRect's
        /// own change detection can miss an in-place sizeDelta write.
        /// </summary>
        internal static void SyncScrollbar(ScrollRect scroll)
        {
            if (scroll == null || scroll.verticalScrollbar == null) return;
            float viewportH = scroll.viewport != null ? scroll.viewport.rect.height : 0f;
            float contentH = scroll.content != null ? Mathf.Max(1f, scroll.content.rect.height) : 1f;
            scroll.verticalScrollbar.size = Mathf.Clamp01(viewportH / contentH);
        }

        internal static void ScrollToTop(ScrollRect scroll)
        {
            if (scroll == null) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
            SyncScrollbar(scroll);
        }

        /// <summary>Write the pinned strip with a colour-coded kind and optional prefix.</summary>
        private static void WriteStatus(Page page, StripKind kind, string text)
        {
            TMP_Text label = statusLabels[(int)page];
            if (label == null) return;

            string prefix;
            Color rail;
            switch (kind)
            {
                case StripKind.Map:     prefix = "> MAP · ";     rail = Warning(); break;
                case StripKind.Confirm: prefix = "> CONFIRM · "; rail = Alert(); break;
                case StripKind.Done:    prefix = "> DONE · ";    rail = Green(); break;
                case StripKind.Blocked: prefix = "> BLOCKED · "; rail = Alert(); break;
                case StripKind.Help:    prefix = "> ";           rail = WingUi.RailCyan; break;
                default:                prefix = "> ";           rail = WingUi.RailInert; break;
            }

            label.text = prefix + text;
            label.color = kind == StripKind.Ambient ? Dim() : WingUi.TextPrimary;

            Image railImage = statusRails[(int)page];
            if (railImage != null) railImage.color = rail;
        }
    }
}
