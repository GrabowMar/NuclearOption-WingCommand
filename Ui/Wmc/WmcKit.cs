using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>A bezel page (spec WMC rebuild §bezel shell): built once into its page root, refreshed at the panel's rate
    /// while it shows; its metric tiles, status-strip alert and ambient hint are its own.</summary>
    internal interface IWmcPage
    {
        void Build(RectTransform page, Rect body);

        void Refresh(WmcContext c);

        /// <summary>The status strip's ambient line while the page shows.</summary>
        string Hint { get; }

        /// <summary>The status strip's alert line, or null.</summary>
        string Alert { get; }

        void Metrics(WmcContext c, WmcMetricRow m);
    }

    /// <summary>The widgets WMC pages share that the NOAvionics toolkit does not have (spec WMC rebuild §widget kit), built
    /// only from toolkit primitives — WC never edits the toolkit.</summary>
    internal static class WmcKit
    {
        /// <summary>No "…" anywhere (spec WMC rebuild): every label under <paramref name="root"/> overflows instead of cutting
        /// and shrinks to the 10 px floor before it does. Once after a build (and after a popup opens).</summary>
        public static void FitAll(RectTransform root)
        {
            if (root == null) return;
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.overflowMode == TextOverflowModes.Ellipsis) t.overflowMode = TextOverflowModes.Overflow;
                if (t.enableAutoSizing) continue;
                t.fontSizeMax = t.fontSize;
                t.fontSizeMin = Mathf.Min(AvTokens.FontMicro, t.fontSize);
                t.enableAutoSizing = true;
            }
        }

        /// <summary>Labels that would still spill out of their box at their smallest size (the automation's text-fit audit).
        /// ponytail: estimated from the preferred width at the largest size scaled to the smallest; measure per size if the
        /// estimate ever disagrees with a screenshot.</summary>
        public static int Overflow(RectTransform root)
        {
            if (root == null) return 0;
            int n = 0;
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(t.text) || t.enableWordWrapping) continue;
                float width = t.rectTransform.rect.width;
                if (width <= 1f) continue;
                float scale = t.enableAutoSizing && t.fontSizeMax > 0f ? t.fontSizeMin / t.fontSizeMax : 1f;
                if (t.GetPreferredValues(t.text).x * scale > width + 1f) n++;
            }
            return n;
        }

        /// <summary>A strip of sub-tabs (Boscali's PageFrame pattern): tab-styled buttons, the picked one latched.</summary>
        public static AvButton[] SubTabs(RectTransform parent, Rect r, string[] labels, string idPrefix,
            Dictionary<string, AvButton> ids, Action<int> pick)
        {
            var tabs = new AvButton[labels.Length];
            float w = r.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                tabs[i] = AvStyled.Button(parent, new Rect(r.x + i * w, r.y, w - (i < labels.Length - 1 ? 1f : 0f), r.height),
                    labels[i], "tab", () => pick(k), AvButtonStyle.Tab);
                ids[idPrefix + labels[i].ToLowerInvariant()] = tabs[i];
            }
            return tabs;
        }

        /// <summary>A small text label that never wraps.</summary>
        public static TMP_Text Text(RectTransform parent, Rect r, string classes, TextAlignmentOptions? align = null)
        {
            TMP_Text t = AvStyled.Label(parent, r, "", classes, align: align);
            t.enableWordWrapping = false;
            return t;
        }

        /// <summary>Sets a label only when its text changed (TMP relays out on every assignment).</summary>
        public static void Set(TMP_Text t, string text)
        {
            if (t != null && t.text != text) t.text = text;
        }
    }

    /// <summary>A key and a row of toggle segments (the 0.9 doctrine rows): one latched, none for MIXED, disabled with a
    /// reason in every segment's tooltip.</summary>
    internal sealed class SegmentRow
    {
        private AvButton[] segments;
        private string[] tips;
        private int latched = -2;
        private bool enabled = true;
        private string why;

        public static SegmentRow Build(RectTransform parent, Rect r, float keyWidth, string key, string[] labels, string[] tips,
            string idPrefix, string[] keys, Dictionary<string, AvButton> ids, Action<int> pick)
        {
            var row = new SegmentRow { segments = new AvButton[labels.Length], tips = tips };
            AvStyled.Label(parent, new Rect(r.x, r.y, keyWidth, r.height), key, "metric-key");
            float x = r.x + keyWidth, w = (r.width - keyWidth - WmcUi.Gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                row.segments[i] = AvStyled.Button(parent, new Rect(x + i * (w + WmcUi.Gap), r.y, w, r.height), labels[i], "btn",
                    () => pick(k), AvButtonStyle.Toggle);
                if (tips != null && i < tips.Length) row.segments[i].WithTooltip(tips[i]);
                ids[idPrefix + keys[i]] = row.segments[i];
            }
            return row;
        }

        /// <summary>Latch segment <paramref name="index"/>; -1 latches none (MIXED).</summary>
        public void Set(int index)
        {
            if (index == latched) return;
            latched = index;
            for (int i = 0; i < segments.Length; i++) segments[i].SetLatched(i == index);
        }

        public void SetEnabled(bool on, string reason)
        {
            if (on == enabled && reason == why) return;
            enabled = on;
            why = reason;
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i].SetEnabled(on);
                segments[i].WithTooltip(on ? (tips != null && i < tips.Length ? tips[i] : null) : reason);
            }
        }
    }

    /// <summary>The header's three metric tiles with WC-drawn keys (spec WMC rebuild §bezel shell): the toolkit's
    /// <c>Metric</c> keeps no handle on its key label, so the shell is built with empty keys and these labels change with the
    /// tab. ponytail: until Boscali's toolkit exposes <c>Metric.Key</c> (a sync then retires these labels).</summary>
    internal sealed class WmcMetricRow
    {
        private readonly AvStyled.Metric[] metrics;
        private readonly TMP_Text[] keys;
        private readonly string[] shown;

        public WmcMetricRow(RectTransform content, AvStyled.Metric[] cells)
        {
            metrics = cells;
            keys = new TMP_Text[cells.Length];
            shown = new string[cells.Length * 3];
            for (int i = 0; i < cells.Length; i++)
            {
                RectTransform value = cells[i].Value.rectTransform;
                // MetricCell places the key 14 px above the value, as wide (AvStyled.MetricCell).
                keys[i] = AvStyled.Label(content, new Rect(value.anchoredPosition.x, value.anchoredPosition.y + 14f,
                    value.rect.width, 12f), "", "metric-key");
            }
        }

        public void SetKeys(string[][] pairs)
        {
            for (int i = 0; i < keys.Length && i < pairs.Length; i++)
            {
                WmcKit.Set(keys[i], pairs[i][0]);
                WmcKit.Set(metrics[i].Unit, pairs[i][1]);
                Set(i, WmcText.Unknown, "", 0f, AvTheme.Friendly);
            }
        }

        /// <summary>A tile's value, caption and bar; the strings are assigned only when they changed.</summary>
        public void Set(int i, string value, string caption, float fraction, Color fill)
        {
            if (i < 0 || i >= metrics.Length) return;
            AvStyled.Metric m = metrics[i];
            if (shown[i * 3] != value) { shown[i * 3] = value; m.Value.text = value; }
            if (shown[i * 3 + 1] != caption) { shown[i * 3 + 1] = caption; m.Caption.text = caption ?? ""; }
            m.Fill.color = fill;
            m.Fill.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction) * m.TrackWidth, m.TrackHeight);
        }
    }

    /// <summary>A scroll viewport whose content height may change after build (the toolkit's <c>AvScreen.Scroll</c> decides
    /// once): a clamped, inertia-free ScrollRect with a 4 px thumb in an 8 px gutter; changing the height keeps the reader's
    /// place (0.9 critique: the scroll reset every refresh).</summary>
    internal sealed class WmcScroll
    {
        private ScrollRect scroll;
        private RectTransform view, track;
        private float height = -1f;

        public RectTransform Content { get; private set; }
        public float Width { get; private set; }

        public static WmcScroll Build(RectTransform parent, Rect viewport, string name)
        {
            const float gutter = 8f;
            var s = new WmcScroll { Width = Mathf.Max(0f, viewport.width - gutter) };
            Image view = AvKit.Panel(parent, new Rect(viewport.x, viewport.y, s.Width, viewport.height), Color.clear);
            view.gameObject.name = name;
            view.raycastTarget = true;
            view.gameObject.AddComponent<RectMask2D>();
            s.view = view.rectTransform;
            var go = new GameObject(name + "Content", typeof(RectTransform));
            s.Content = (RectTransform)go.transform;
            s.Content.SetParent(view.rectTransform, false);
            AvKit.Place(s.Content, new Rect(0f, 0f, s.Width, viewport.height));

            s.scroll = view.gameObject.AddComponent<ScrollRect>();
            s.scroll.viewport = view.rectTransform;
            s.scroll.content = s.Content;
            s.scroll.horizontal = false;
            s.scroll.movementType = ScrollRect.MovementType.Clamped;
            s.scroll.scrollSensitivity = 24f;
            s.scroll.inertia = false;

            Image track = AvKit.Panel(parent, new Rect(viewport.x + viewport.width - 4f, viewport.y, 4f, viewport.height), AvTheme.Hairline);
            track.raycastTarget = true;
            s.track = track.rectTransform;
            Image thumb = AvKit.Panel(track.rectTransform, new Rect(0f, 0f, 4f, viewport.height), AvTheme.Dim);
            thumb.raycastTarget = true;
            AvKit.Stretch(thumb.rectTransform);
            Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = thumb.rectTransform;
            bar.targetGraphic = thumb;
            bar.direction = Scrollbar.Direction.BottomToTop;
            AvInput.StripNavigation(bar);
            s.scroll.verticalScrollbar = bar;
            s.scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            s.SetContentHeight(viewport.height);
            return s;
        }

        /// <summary>The content's height; the offset stays where the reader left it, clamped into the new range.</summary>
        public void SetContentHeight(float h)
        {
            if (Mathf.Abs(h - height) < 0.5f) return;
            height = h;
            Content.sizeDelta = new Vector2(Content.sizeDelta.x, h);
            Clamp();
        }

        /// <summary>Moves or resizes the viewport (the flight list above it grew or shrank); the reader's place is kept.</summary>
        public void SetViewport(Rect viewport)
        {
            Width = Mathf.Max(0f, viewport.width - 8f);
            AvKit.Place(view, new Rect(viewport.x, viewport.y, Width, viewport.height));
            AvKit.Place(track, new Rect(viewport.x + viewport.width - 4f, viewport.y, 4f, viewport.height));
            Clamp();
        }

        private void Clamp()
        {
            Vector2 at = Content.anchoredPosition;
            float max = Mathf.Max(0f, height - view.rect.height);
            if (at.y > max) Content.anchoredPosition = new Vector2(at.x, max);
        }
    }
}
