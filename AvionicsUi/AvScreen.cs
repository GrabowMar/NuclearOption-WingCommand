using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// The chrome every bezel screen wears: an id tag and status chips across the top, a
    /// row of display metrics, a segmented tab bar, the page body, and a pinned status
    /// strip.
    ///
    /// <para>This existed twice before it existed once — inline in the OPS panel and again
    /// as a private shell inside the vanilla-panel rebuild — and a third screen would have
    /// made three. The shape is not the interesting part of any of them, so it lives here
    /// and each screen spends its code on what it actually shows.</para>
    ///
    /// <para>Nothing here runs per frame. The whole tree is measured and arranged once, at
    /// build time, and what survives is the rectangles plus the handful of labels a refresh
    /// pass writes into.</para>
    /// </summary>
    public sealed class AvScreen
    {
        /// <summary>How far a page spine sits inside the panel padding.</summary>
        public const float SpineInset = 14f;

        private const float MetricRowHeight = 58f;

        private readonly GameObject[] pages;
        private readonly Action<int> onTab;

        /// <summary>The stretched child every page and widget is parented to.</summary>
        public RectTransform Content { get; private set; }

        /// <summary>Id tag, live state line and status chips.</summary>
        public AvStyled.DataBar DataBar { get; private set; }

        /// <summary>The display metrics above the tabs, in the order they were declared.</summary>
        public AvStyled.Metric[] Metrics { get; private set; }

        public AvButton[] Tabs { get; private set; }

        /// <summary>The rectangle a page lays itself out inside.</summary>
        public Rect Body { get; private set; }

        public TMP_Text Status { get; private set; }

        /// <summary>Which tab is latched. -1 until the first <see cref="SetPage"/>.</summary>
        public int Page { get; private set; } = -1;

        private AvScreen(int tabCount, Action<int> tabHandler)
        {
            pages = new GameObject[Math.Max(0, tabCount)];
            onTab = tabHandler;
        }

        /// <summary>
        /// Build the chrome into <paramref name="content"/>.
        /// </summary>
        /// <param name="id">Two to four characters for the filled tag, e.g. "OPS".</param>
        /// <param name="tabLabels">One label per page. A single tab still draws a bar.</param>
        /// <param name="metrics">{key, unit} per display metric; null or empty omits the row.</param>
        /// <param name="chipCount">Status chips in the top bar.</param>
        public static AvScreen Build(
            RectTransform content, string id, string[] tabLabels, string[][] metrics,
            int chipCount, float width, float height, Action<int> onTab)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            string[] labels = tabLabels ?? new string[0];
            int metricCount = metrics == null ? 0 : metrics.Length;

            var screen = new AvScreen(labels.Length, onTab);

            AvNode shell = AvBox.Column("screen").Pad(AvTokens.Pad).Gaps(AvTokens.Space2)
                .Add(AvBox.Row("databar").Height(AvTokens.TitleBarHeight + 2f));

            if (metricCount > 0)
            {
                AvNode row = AvBox.Grid("metrics", metricCount).Height(MetricRowHeight).Gaps(0f);
                for (int i = 0; i < metricCount; i++) row.Add(AvBox.Cell("m" + i));
                shell.Add(row);
            }

            AvNode tabs = AvBox.Row("tabs").Height(AvTokens.TabBarHeight).Gaps(1f);
            for (int i = 0; i < labels.Length; i++) tabs.Add(AvBox.Cell("t" + i).Grow());

            shell.Add(tabs)
                 .Add(AvBox.Cell("body").Grow())
                 .Add(AvBox.Cell("status").Height(AvTokens.StatusStripHeight));

            shell.Arrange(new Rect(0f, 0f, width, height));

            screen.Content = content;
            screen.DataBar = AvStyled.TopBar(content, shell.At("databar"), id, chipCount);

            screen.Metrics = new AvStyled.Metric[metricCount];
            if (metricCount > 0)
            {
                Rect row = shell.At("metrics");
                AvStyled.Box(content, row, "metrics");
                for (int i = 0; i < metricCount; i++)
                {
                    string[] pair = metrics[i] ?? new string[0];
                    screen.Metrics[i] = AvStyled.MetricCell(
                        content, shell.At("metrics.m" + i),
                        pair.Length > 0 ? pair[0] : "", pair.Length > 1 ? pair[1] : "");

                    // Rules between cells, not around them: the row already has a border.
                    if (i > 0)
                    {
                        float x = row.x + row.width * i / metricCount;
                        AvKit.Rule(content, new Rect(x, row.y, 1f, row.height), AvTheme.Hairline);
                    }
                }
            }

            screen.Tabs = new AvButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                screen.Tabs[i] = AvStyled.Button(
                    content, shell.At("tabs.t" + i), labels[i], "tab",
                    () => screen.SetPage(index), AvButtonStyle.Tab);
            }

            screen.Body = shell.At("body");
            screen.Status = AvStyled.StatusStrip(content, shell.At("status"));
            return screen;
        }

        /// <summary>
        /// The height this panel should take, given the slot it was parented into.
        ///
        /// <para>A screen inherits its bay from the stock template it was cloned beside, and
        /// that bay is taller than the panels used to be. Measuring it beats a second
        /// hard-coded constant that would be wrong at the next resolution; when there is
        /// nothing measurable yet, <paramref name="min"/> is the honest answer.</para>
        /// </summary>
        public static float ResolveHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;

            float available = parent.rect.height;

            // The immediate parent may be a zero-height anchor. Walk up to the first
            // ancestor that has actually been laid out.
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }

            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        /// <summary>A page root, stretched over the content and hidden until selected.</summary>
        public GameObject CreatePage(int index, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            var rect = page.GetComponent<RectTransform>();
            rect.SetParent(Content, false);
            AvKit.Stretch(rect);
            page.SetActive(false);

            if (index >= 0 && index < pages.Length) pages[index] = page;
            return page;
        }

        public void SetPage(int index)
        {
            Page = index;
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] != null) pages[i].SetActive(i == index);
            }
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (Tabs[i] != null) Tabs[i].SetLatched(i == index);
            }
            onTab?.Invoke(index);
        }

        /// <summary>
        /// Wrap a page body in a clipped, scrollable viewport when its content is taller
        /// than the space available, and return the transform the page should build into.
        ///
        /// <para>Returns <paramref name="parent"/> untouched when everything fits, so a short
        /// page never pays for a mask it does not need.</para>
        /// </summary>
        public static RectTransform Scroll(
            RectTransform parent, Rect body, float contentHeight, out Rect contentArea)
        {
            contentArea = body;
            if (parent == null || contentHeight <= body.height) return parent;

            Image viewportImage = AvKit.Panel(parent, body, Color.clear);
            var viewport = (RectTransform)viewportImage.transform;
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("ScrollContent", typeof(RectTransform));
            var scrolled = (RectTransform)content.transform;
            scrolled.SetParent(viewport, false);

            contentArea = new Rect(0f, 0f, body.width, contentHeight);
            AvKit.Place(scrolled, contentArea);

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = scrolled;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = false;
            return scrolled;
        }

        /// <summary>
        /// The strip's copy, in priority order: an alert outranks everything, then whatever
        /// the pointer is over, then an armed prompt, then ambient telemetry. A disabled
        /// control says <em>why</em> here, which is the only place it can.
        /// </summary>
        public void WriteStatus(string alert, string prompt, string ambient)
        {
            if (Status == null) return;

            if (!string.IsNullOrEmpty(alert))
            {
                Status.text = "> " + alert;
                Status.color = AvTheme.Alert;
                return;
            }

            string hovered = AvButton.HoveredTooltip;
            if (!string.IsNullOrEmpty(hovered))
            {
                Status.text = "> " + hovered;
                Status.color = AvTheme.Friendly;
                return;
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                Status.text = "> " + prompt;
                Status.color = AvTheme.Friendly;
                return;
            }

            Status.text = "> " + (ambient ?? "");
            Status.color = AvTheme.Dim;
        }

        /// <summary>
        /// A share bar: one track with several fills laid end to end, so the ground between
        /// two sides can read as neither side's rather than as one side's shortfall.
        /// </summary>
        public static void ShareBar(
            RectTransform parent, Rect area, float[] fractions, string[] classes)
        {
            AvStyled.Box(parent, area, "bar");
            if (fractions == null || classes == null) return;

            float x = area.x;
            int count = Math.Min(fractions.Length, classes.Length);
            for (int i = 0; i < count; i++)
            {
                float w = Mathf.Max(0f, area.width * Mathf.Clamp01(fractions[i]));
                if (w <= 0.5f) continue;

                AvStyle style = AvStyleHost.Style("bar " + classes[i]);
                AvKit.Panel(parent, new Rect(x, area.y, w, area.height),
                            AvStyleHost.Resolve(style.Background, AvTheme.RailInert));
                x += w;
            }
        }
    }
}
