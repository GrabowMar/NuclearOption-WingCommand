using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>The Wing Command room (spec WMC program §6): a full-screen window over the maximized map on its own overlay
    /// canvas — backdrop, frame, nine notches (Ctrl+1..9, Ctrl+Tab), × CLOSE (Esc, or right-click outside), the keyboard held
    /// while it is open. Pages share the WMC panel's context: one selection, one scope, one draft.</summary>
    internal sealed class WmcRoom : IWingService
    {
        private const int SortingOrder = 29990;   // under Boscali's OPS window (30000) if both are open
        private const float Reference = 1920f, ReferenceHeight = 1080f, NotchGap = 4f, NotchMax = 164f, CloseWidth = 132f;
        private const float HeaderHeight = 44f, FooterHeight = 26f, BackdropAlpha = 0.72f;

        private sealed class Notch
        {
            public RectTransform Host;
            public AvRoomFrame.NotchChrome Chrome;
            public AvButton Hit;
            public bool Active, Enabled;
        }

        private sealed class Backdrop : MonoBehaviour, IPointerClickHandler
        {
            public WmcRoom Room;

            public void OnPointerClick(PointerEventData e)
            {
                // A left-click on the dimmed game does nothing; a right-click closes (the OPS window's rule).
                if (e != null && e.button == PointerEventData.InputButton.Right) Room?.Close();
            }
        }

        public static WmcRoom Instance { get; private set; }
        public string Name => "WMC Room";

        private readonly IRoomPage[] pages = new IRoomPage[RoomNotches.Count];
        private readonly bool[] built = new bool[RoomNotches.Count];
        private readonly bool[] enabled = new bool[RoomNotches.Count];
        private readonly Notch[] notches = new Notch[RoomNotches.Count];
        private readonly RectTransform[] bodies = new RectTransform[RoomNotches.Count];
        private GameObject root;
        private Canvas canvas;
        private RectTransform canvasRect, frame;
        private Image topLeft, topRight, bottom, left, right;
        private Notch close;
        private TMP_Text title, telemetry, footer;
        private Rect window, body;
        private int page = -1;
        private float nextRefresh;
        private bool open, held;

        public WmcRoom()
        {
            Instance = this;
            Tactical = new RoomTactical();
            Register(RoomNotches.Tactical, Tactical);
        }

        /// <summary>The TACTICAL page (automation reads its counters).</summary>
        public RoomTactical Tactical { get; }

        /// <summary>One refresh of the open page now (automation: a change made in the same call shows in its report).</summary>
        public void RefreshNow()
        {
            if (!open) return;
            WmcContext c = Context();
            if (c == null) return;
            RefreshHeader(c);
            if (page >= 0) pages[page]?.Refresh(c);
        }

        public bool IsOpen => open;
        public int Page => page;
        public bool KeyboardHeld => held;

        /// <summary>A page for a notch; its notch lights up.</summary>
        public void Register(int notch, IRoomPage p)
        {
            if (notch < 0 || notch >= RoomNotches.Count) return;
            pages[notch] = p;
            enabled[notch] = p != null;
        }

        public void Activate() => Teardown();

        public void Deactivate() => Teardown();

        public void FixedTick(float dt)
        {
        }

        public void Toggle()
        {
            if (open) Close();
            else Open();
        }

        /// <summary>Opens the room (maximizing the map if needed) on <paramref name="notch"/>, the last page, or the first
        /// enabled one.</summary>
        public void Open(int notch = -1)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.AllowedToOpen)
            {
                WingToast.Show("The map cannot open now");
                return;
            }
            if (!DynamicMap.mapMaximized) map.Maximize();
            if (root == null) Build();
            WmcContext c = Context();
            bool was = open;
            open = true;
            canvas.enabled = true;
            Layout();
            if (!held && WingKeyboardGuard.Available)
            {
                WingKeyboardGuard.Capture();
                held = true;
            }
            int target = notch >= 0 && notch < RoomNotches.Count && enabled[notch] ? notch
                : page >= 0 && enabled[page] ? page : RoomNotches.Next(RoomNotches.Count - 1, +1, enabled);
            if (!enabled[target]) target = -1;
            Select(target, c);
            nextRefresh = 0f;
            AvButton.ClearTooltip();
            if (!was) AvUiSound.Play(AvUiCue.Engage);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (page >= 0) pages[page]?.Hide();
            if (canvas != null) canvas.enabled = false;
            AvButton.ClearTooltip();
            ReleaseKeyboard();
            AvUiSound.Play(AvUiCue.Release);
        }

        private void ReleaseKeyboard()
        {
            if (!held) return;
            held = false;
            WingKeyboardGuard.Release();
        }

        private void Teardown()
        {
            Close();
            ReleaseKeyboard();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            canvas = null;
            page = -1;
            for (int i = 0; i < built.Length; i++)
            {
                built[i] = false;
                bodies[i] = null;
            }
        }

        private static WmcContext Context()
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return null;
            panel.FillContext();
            return panel.Context;
        }

        public void Tick(float dt)
        {
            if (!open) return;
            if (!DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            WmcContext c = WmcPanel.Instance != null ? WmcPanel.Instance.Context : null;
            if (Chord(c) || c == null) return;
            IRoomPage p = page >= 0 ? pages[page] : null;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingFidelity.Interval(1f / 6f);
                c = Context();
                RefreshHeader(c);
                p?.Refresh(c);
                footer.text = p != null ? p.Hint : "Nothing here yet.";
            }
            p?.Tick(c);
        }

        private bool Chord(WmcContext c)
        {
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;
            for (int d = 1; d <= RoomNotches.Count; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + d)) continue;
                int i = RoomNotches.ForDigit(d, enabled);
                if (i >= 0) Select(i, c);
                return true;
            }
            if (!Input.GetKeyDown(KeyCode.Tab)) return false;
            bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Select(RoomNotches.Next(page < 0 ? 0 : page, back ? -1 : +1, enabled), c);
            return true;
        }

        private void Select(int notch, WmcContext c)
        {
            if (notch >= 0 && !built[notch])
            {
                BuildPage(notch);
                bodies[notch].gameObject.SetActive(false);
            }
            if (notch == page && notch >= 0)
            {
                bodies[notch].gameObject.SetActive(true);
                pages[notch].Show(c);
                Paint();
                return;
            }
            if (page >= 0)
            {
                pages[page]?.Hide();
                if (bodies[page] != null) bodies[page].gameObject.SetActive(false);
            }
            page = notch;
            if (page >= 0)
            {
                bodies[page].gameObject.SetActive(true);
                pages[page].Show(c);
                AvUiSound.Play(AvUiCue.Navigate);
            }
            nextRefresh = 0f;
            Paint();
        }

        private void BuildPage(int i)
        {
            var go = new GameObject("Page" + i, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(frame, false);
            AvKit.Place(rt, body);
            bodies[i] = rt;
            pages[i].Build(rt, new Rect(0f, 0f, body.width, body.height));
            built[i] = true;
        }

        // ---- Build ------------------------------------------------------------------------------

        private void Build()
        {
            root = new GameObject("WingCommandRoom", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.enabled = false;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Reference, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasRect = (RectTransform)root.transform;

            Image backdrop = AvRoomFrame.CreateBackdrop(canvasRect, BackdropAlpha);
            backdrop.gameObject.AddComponent<Backdrop>().Room = this;
            frame = AvRoomFrame.CreateFrame(canvasRect, "Frame", out _);
            Image ground = AvKit.Panel(frame, new Rect(0f, 0f, 10f, 10f), AvTheme.Ground.WithAlpha(0.97f));
            AvKit.Stretch(ground.rectTransform);
            ground.raycastTarget = true;   // clicks inside the frame never reach the backdrop
            Color edge = AvTheme.Frame;
            topLeft = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            topRight = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            bottom = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            left = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            right = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            title = AvStyled.Label(frame, new Rect(0f, 0f, 10f, 10f), "WING COMMAND", "section-title");
            telemetry = AvStyled.Label(frame, new Rect(0f, 0f, 10f, 10f), "", "row-sub", align: TextAlignmentOptions.MidlineRight);
            footer = AvStyled.Label(frame, new Rect(0f, 0f, 10f, 10f), "", "hint");
            footer.enableWordWrapping = telemetry.enableWordWrapping = false;
            footer.overflowMode = telemetry.overflowMode = TextOverflowModes.Ellipsis;
            for (int i = 0; i < notches.Length; i++)
            {
                int k = i;
                notches[i] = MakeNotch(RoomNotches.Labels[i], RoomNotches.Key(i), () => Select(k, Context()),
                    enabled[i] ? "Open " + RoomNotches.Labels[i] + " (" + RoomNotches.Key(i).Replace("CTRL ", "Ctrl+") + ", Ctrl+Tab cycles)."
                        : RoomNotches.Pending[i]);
            }
            close = MakeNotch("× CLOSE", "ESC", Close, "Close the room and return to the map (Esc, or right-click outside).");
            close.Enabled = true;
        }

        private Notch MakeNotch(string label, string key, Action click, string tip)
        {
            var go = new GameObject("Notch", typeof(RectTransform));
            var host = (RectTransform)go.transform;
            host.SetParent(frame, false);
            var n = new Notch { Host = host, Chrome = AvRoomFrame.CreateNotchChrome(host, label, key) };
            n.Hit = AvKit.HitButton(host, new Rect(0f, 0f, 10f, AvRoomFrame.NotchHeight), click);
            AvKit.Stretch((RectTransform)n.Hit.transform);
            n.Hit.WithTooltip(tip);
            return n;
        }

        /// <summary>The window for this canvas size (1280-1840 × 720-968, centred under the top margin).</summary>
        private void Layout()
        {
            Rect size = canvasRect.rect;
            window = AvRoomFrame.WindowRect(size.width > 0f ? size.width : Reference, size.height > 0f ? size.height : ReferenceHeight);
            float w = window.width, h = window.height;
            AvKit.Place(frame, new Rect(window.x, -window.y, w, h));
            AvKit.Place(bottom.rectTransform, new Rect(0f, -h + 1f, w, 1f));
            AvKit.Place(left.rectTransform, new Rect(0f, 0f, 1f, h));
            AvKit.Place(right.rectTransform, new Rect(w - 1f, 0f, 1f, h));
            AvKit.Place(title.rectTransform, new Rect(16f, -8f, 300f, HeaderHeight - 12f));
            AvKit.Place(telemetry.rectTransform, new Rect(320f, -8f, w - 336f, HeaderHeight - 12f));
            AvKit.Place(footer.rectTransform, new Rect(16f, -h + FooterHeight - 2f, w - 32f, FooterHeight - 4f));
            float inset = AvRoomFrame.NotchInset;
            float nw = RoomNotches.Width(w - inset * 2f - CloseWidth - NotchGap * 2f, RoomNotches.Count, NotchMax, NotchGap);
            float x = inset;
            for (int i = 0; i < notches.Length; i++)
            {
                AvKit.Place(notches[i].Host, new Rect(x, AvRoomFrame.NotchHeight, nw, AvRoomFrame.NotchHeight));
                AvRoomFrame.LayoutNotch(notches[i].Chrome, nw);
                x += nw + NotchGap;
            }
            AvKit.Place(close.Host, new Rect(w - inset - CloseWidth, AvRoomFrame.NotchHeight, CloseWidth, AvRoomFrame.NotchHeight));
            AvRoomFrame.LayoutNotch(close.Chrome, CloseWidth);
            Rect next = new Rect(1f, -HeaderHeight, w - 2f, h - HeaderHeight - FooterHeight);
            if (next != body)
            {
                // Pages built for another window size are rebuilt when next shown.
                body = next;
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i] != null)
                    {
                        pages[i]?.Hide();
                        UnityEngine.Object.Destroy(bodies[i].gameObject);
                        bodies[i] = null;
                        built[i] = false;
                    }
            }
            Paint();
        }

        private void Paint()
        {
            float w = window.width;
            float gapStart = w, gapEnd = w;
            for (int i = 0; i < notches.Length; i++)
            {
                Notch n = notches[i];
                n.Active = i == page;
                n.Enabled = enabled[i];
                n.Hit.SetEnabled(n.Enabled);
                PaintNotch(n);
                if (n.Active)
                {
                    gapStart = n.Host.anchoredPosition.x + 1f;
                    gapEnd = gapStart + n.Host.sizeDelta.x - 2f;
                }
            }
            PaintNotch(close);
            // The active notch opens into the frame: the top edge breaks under it.
            AvKit.Place(topLeft.rectTransform, new Rect(0f, 0f, Mathf.Min(gapStart, w), 1f));
            topRight.enabled = gapEnd < w;
            if (topRight.enabled) AvKit.Place(topRight.rectTransform, new Rect(gapEnd, 0f, w - gapEnd, 1f));
        }

        private static void PaintNotch(Notch n)
        {
            AvRoomFrame.NotchChrome c = n.Chrome;
            c.Fill.color = n.Active ? AvTheme.Surface : AvTheme.SurfaceInert.WithAlpha(0.92f);
            c.Left.color = c.Top.color = c.Right.color = n.Active ? AvTheme.RailInfo : AvTheme.Hairline;
            c.ActiveBar.color = n.Active ? AvTheme.RailReady : AvTheme.Hairline;
            c.Label.color = !n.Enabled ? AvTheme.Disabled : n.Active ? AvTheme.TextPrimary : AvTheme.Dim;
            c.Key.color = !n.Enabled ? AvTheme.Disabled : AvTheme.Dim;
        }

        private void RefreshHeader(WmcContext c)
        {
            if (c == null) return;
            WingSummary s = WingRows.Summary(c.Rows, c.Count);
            WingPlanner p = c.Wing != null ? c.Wing.Planner : null;
            telemetry.text = c.Count + (c.Count == 1 ? " WINGMAN" : " WINGMEN") + " · FUEL " + WmcText.Percent(s.MinFuel)
                + " · WING: " + (p != null && p.Active ? p.Current.Kind.ToString().ToUpperInvariant() : "FORM")
                + " · ORDERS TO " + c.ScopeLabel;
        }
    }
}
