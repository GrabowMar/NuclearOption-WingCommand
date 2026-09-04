using System;
using System.Collections.Generic;
using System.Text;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WingCommand
{
    /// <summary>
    /// Mirrors the native kill/game-message streams into the left map column. With an MFD
    /// open it occupies the free area above that screen; with no selection it expands to
    /// the entire reserved panel bay rather than leaving an empty column.
    /// The native MessageUI remains the lifetime and content authority; only its two text
    /// renderers are temporarily hidden, so closing the MFD restores vanilla losslessly.
    /// </summary>
    internal static class MfdLogPanel
    {
        private const string PanelName = "NOAvionics.TacticalLog";
        private const float MinimumHeight = 72f;
        private const float HeaderHeight = 30f;
        private const float Inset = 12f;
        private const float RetentionSeconds = 30f;
        private const int MaximumEntries = 120;

        private sealed class Entry
        {
            public string Text;
            public float CapturedAt;
            public float ExpiresAt;
        }

        private static RectTransform panel;
        private static TMP_Text body;
        private static RectTransform scrollContent;
        private static ScrollRect scroll;
        private static VirtualMFD mfd;
        private static MfdLayout.Columns columns;
        private static TextMeshProUGUI messageSource;
        private static TextMeshProUGUI killSource;
        private static bool originalsHidden;
        private static bool messageWasEnabled;
        private static bool killWasEnabled;
        private static Vector2 builtSize;
        private static float bodyWidth;
        private static float viewportHeight;
        private static readonly List<Entry> history = new List<Entry>();
        private static readonly List<string> previousMessages = new List<string>();
        private static readonly List<string> previousKills = new List<string>();

        public static void Ensure(Canvas canvas, MfdLayout.Columns layout, VirtualMFD virtualMfd)
        {
            if (canvas == null || virtualMfd == null || !GameAccess.MfdLogAvailable) return;

            if (panel != null && panel.parent != canvas.transform) Restore();

            mfd = virtualMfd;
            columns = layout;
            ResolveSources();

            if (panel == null)
            {
                Transform existing = canvas.transform.Find(PanelName);
                panel = existing as RectTransform;
            }

            if (panel == null)
            {
                var go = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
                panel = go.GetComponent<RectTransform>();
                panel.SetParent(canvas.transform, worldPositionStays: false);
            }

            Image background = panel.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            Tick();
        }

        public static void Tick()
        {
            if (panel == null || mfd == null) return;

            ResolveSources();
            bool added = CaptureChanges(messageSource == null ? null : messageSource.text, previousMessages);
            added |= CaptureChanges(killSource == null ? null : killSource.text, previousKills);
            PruneHistory();

            MFDScreen active = ActiveScreen();
            if (!DynamicMap.mapMaximized)
            {
                HidePanel();
                return;
            }

            float availableHeight = MfdPanelDock.AvailableHeight(columns.Panel.height);
            float screenHeight = active == null
                ? 0f
                : Mathf.Clamp(MfdPanelDock.VisibleHeight(active), 0f, availableHeight);
            float height = active == null
                ? availableHeight
                : availableHeight - screenHeight - MfdLayout.Gutter;
            if (height < MinimumHeight)
            {
                HidePanel();
                return;
            }

            if (messageSource == null && killSource == null)
            {
                HidePanel();
                return;
            }

            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = new Vector2(columns.Panel.width, height);
            panel.anchoredPosition = new Vector2(columns.Panel.x, columns.Panel.y);
            panel.localScale = Vector3.one;
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();

            if (!Approximately(builtSize, panel.sizeDelta) || body == null)
                Rebuild(panel.sizeDelta);

            HideOriginals();
            body.text = HistoryText();
            ResizeScrollContent(added);
        }

        public static void Restore()
        {
            RestoreOriginals();
            if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
            panel = null;
            body = null;
            scrollContent = null;
            scroll = null;
            mfd = null;
            messageSource = null;
            killSource = null;
            builtSize = Vector2.zero;
            bodyWidth = 0f;
            viewportHeight = 0f;
            history.Clear();
            previousMessages.Clear();
            previousKills.Clear();
        }

        public static void Reset() => Restore();

        private static void ResolveSources()
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            TextMeshProUGUI nextMessage = GameAccess.GetMessageText(ui);
            TextMeshProUGUI nextKill = GameAccess.GetKillFeedText(ui);
            if (nextMessage == messageSource && nextKill == killSource) return;

            RestoreOriginals();
            messageSource = nextMessage;
            killSource = nextKill;
            previousMessages.Clear();
            previousKills.Clear();
        }

        private static MFDScreen ActiveScreen()
        {
            MFDScreen active = FindActive(GameAccess.GetLeftScreens(mfd));
            return active ?? FindActive(GameAccess.GetRightScreens(mfd));
        }

        private static MFDScreen FindActive(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            for (int i = 0; i < screens.Count; i++)
            {
                MFDScreen screen = screens[i];
                if (screen != null && screen.isActive) return screen;
            }
            return null;
        }

        private static void Rebuild(Vector2 size)
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            builtSize = size;
            var bar = AvStyled.TopBar(panel, new Rect(0f, 0f, size.x, HeaderHeight), "LOG", 1);
            bar.State.text = "TACTICAL EVENT STREAM";
            bar.SetChip(0, "LIVE", true);

            AvStyled.Spine(panel,
                new Rect(Inset, -HeaderHeight - 8f, 3f,
                         Mathf.Max(0f, size.y - HeaderHeight - 16f)));

            bodyWidth = Mathf.Max(0f, size.x - Inset * 2f - 24f);
            viewportHeight = Mathf.Max(0f, size.y - HeaderHeight - 16f);

            var scrollGo = new GameObject("EventScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.SetParent(panel, worldPositionStays: false);
            AvKit.Place(scrollRect,
                new Rect(Inset + 12f, -HeaderHeight - 8f, bodyWidth + 12f, viewportHeight));

            Image scrollHitArea = scrollGo.GetComponent<Image>();
            scrollHitArea.color = Color.clear;
            scrollHitArea.raycastTarget = true;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(scrollRect, worldPositionStays: false);
            Stretch(viewport);
            Image viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            scrollContent = contentGo.GetComponent<RectTransform>();
            scrollContent.SetParent(viewport, worldPositionStays: false);
            scrollContent.anchorMin = new Vector2(0f, 1f);
            scrollContent.anchorMax = new Vector2(1f, 1f);
            scrollContent.pivot = new Vector2(0.5f, 1f);
            scrollContent.anchoredPosition = Vector2.zero;
            scrollContent.sizeDelta = new Vector2(0f, viewportHeight);

            body = AvStyled.Label(
                scrollContent,
                new Rect(0f, 0f, bodyWidth, viewportHeight),
                "", "row-sub", align: TextAlignmentOptions.TopLeft);
            body.richText = true;
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;

            scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = scrollContent;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 28f;
            scroll.verticalNormalizedPosition = 1f;
        }

        private static void HideOriginals()
        {
            if (!originalsHidden)
            {
                messageWasEnabled = messageSource != null && messageSource.enabled;
                killWasEnabled = killSource != null && killSource.enabled;
                originalsHidden = true;
            }

            if (messageSource != null) messageSource.enabled = false;
            if (killSource != null) killSource.enabled = false;
        }

        private static void RestoreOriginals()
        {
            if (!originalsHidden) return;
            if (messageSource != null) messageSource.enabled = messageWasEnabled;
            if (killSource != null) killSource.enabled = killWasEnabled;
            originalsHidden = false;
        }

        private static void HidePanel()
        {
            RestoreOriginals();
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private static bool CaptureChanges(string text, List<string> previous)
        {
            List<string> current = SplitLines(text);
            var matched = new bool[current.Count];
            int[,] lcs = new int[previous.Count + 1, current.Count + 1];

            for (int i = 1; i <= previous.Count; i++)
            {
                for (int j = 1; j <= current.Count; j++)
                {
                    lcs[i, j] = string.Equals(previous[i - 1], current[j - 1], StringComparison.Ordinal)
                        ? lcs[i - 1, j - 1] + 1
                        : Mathf.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }

            int oldIndex = previous.Count;
            int newIndex = current.Count;
            while (oldIndex > 0 && newIndex > 0)
            {
                if (string.Equals(previous[oldIndex - 1], current[newIndex - 1], StringComparison.Ordinal))
                {
                    matched[newIndex - 1] = true;
                    oldIndex--;
                    newIndex--;
                }
                else if (lcs[oldIndex - 1, newIndex] >= lcs[oldIndex, newIndex - 1])
                {
                    oldIndex--;
                }
                else
                {
                    newIndex--;
                }
            }

            bool added = false;
            for (int i = current.Count - 1; i >= 0; i--)
            {
                if (matched[i]) continue;
                AddEntry(current[i]);
                added = true;
            }

            previous.Clear();
            previous.AddRange(current);
            return added;
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0) result.Add(line);
            }
            return result;
        }

        private static void AddEntry(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < Mathf.Min(4, history.Count); i++)
            {
                if (history[i].Text == text && now - history[i].CapturedAt < 0.5f) return;
            }

            history.Insert(0, new Entry
            {
                Text = text,
                CapturedAt = now,
                ExpiresAt = now + RetentionSeconds,
            });
            while (history.Count > MaximumEntries) history.RemoveAt(history.Count - 1);
        }

        private static void PruneHistory()
        {
            float now = Time.unscaledTime;
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i].ExpiresAt <= now) history.RemoveAt(i);
        }

        private static string HistoryText()
        {
            if (history.Count == 0) return "NO ACTIVE TRAFFIC";

            var text = new StringBuilder();
            for (int i = 0; i < history.Count; i++)
            {
                if (i > 0) text.Append('\n');
                text.Append(history[i].Text);
            }
            return text.ToString();
        }

        private static void ResizeScrollContent(bool stickToTop)
        {
            if (body == null || scrollContent == null) return;

            float preferred = body.GetPreferredValues(body.text, bodyWidth, 0f).y + 6f;
            float height = Mathf.Max(viewportHeight, preferred);
            scrollContent.sizeDelta = new Vector2(0f, height);
            AvKit.Place(body.transform as RectTransform, new Rect(0f, 0f, bodyWidth, height));

            if (stickToTop && scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f;
    }
}
