using System;
using System.Collections.Generic;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// "WMC" — a native MFD screen on the maximised map, alongside BDF / MAP / HUD.
    ///
    /// The game's bezel columns each carry six buttons but only three configured screens,
    /// so the fourth slot is free. Registering an <see cref="MFDScreen"/> there and calling
    /// <c>VirtualMFD.SetupButtons()</c> lights the button up and labels it, and the game
    /// then drives show/hide exactly as it does for its own screens.
    ///
    /// The panel is built from scratch rather than cloned: the stock HUD OPTIONS hierarchy
    /// is not something this mod can safely dissect, whereas building known widgets and
    /// borrowing only the font and theme colours produces a predictable result that still
    /// matches the game's look.
    /// </summary>
    internal static class WmcScreen
    {
        private const float PanelWidth = 430f;
        private const float PanelHeight = 620f;
        private const float Pad = 12f;
        private const float RowHeight = 30f;
        private const float Gap = 4f;

        private static MFDScreen screen;
        private static RectTransform rosterArea;
        private static TMP_Text shapeLabel;
        private static TMP_Text summaryLabel;
        private static TMP_Text postureLabel;
        private static TMP_FontAsset font;

        private static readonly List<RosterRow> rosterRows = new List<RosterRow>();

        private static float nextAttempt;
        private static bool gaveUp;

        public static bool Installed => screen != null;

        // ------------------------------------------------------------------- lifecycle

        /// <summary>
        /// Called each frame from the manager. Installs lazily rather than patching
        /// <c>VirtualMFD.Start</c>, so it does not depend on plugin/scene ordering.
        /// </summary>
        public static void Tick(WingRegistry wing)
        {
            if (gaveUp || !GameAccess.MfdAvailable || !Plugin.Config2.UseMfdPanel.Value) return;

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (screen.isActive) Refresh(wing);
        }

        /// <summary>Forget the screen when the mission ends; a new one is built next time.</summary>
        public static void Reset()
        {
            screen = null;
            rosterArea = null;
            shapeLabel = null;
            summaryLabel = null;
            rosterRows.Clear();
            postureLabel = null;
            gaveUp = false;
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                List<Button> buttons = GameAccess.GetLeftButtons(mfd);
                List<MFDScreen> screens = GameAccess.GetLeftScreens(mfd);
                bool left = true;

                if (!TryClaimSlot(buttons, screens, out int slot))
                {
                    // Fall back to the right column if the left one is fully configured.
                    buttons = GameAccess.GetRightButtons(mfd);
                    screens = GameAccess.GetRightScreens(mfd);
                    left = false;

                    if (!TryClaimSlot(buttons, screens, out slot))
                    {
                        Fail("no free bezel button on either column");
                        return;
                    }
                }

                MFDScreen template = FindTemplate(screens) ??
                                     FindTemplate(GameAccess.GetLeftScreens(mfd)) ??
                                     FindTemplate(GameAccess.GetRightScreens(mfd));
                if (template == null) return;

                screen = Build(template, buttons[slot]);
                if (screen == null) return;

                // Free slots are null entries in the list, not indices past its end.
                while (screens.Count <= slot) screens.Add(null);
                screens[slot] = screen;

                mfd.SetupButtons();

                // SetupButtons only ever disables buttons — it never re-enables one it
                // turned off on the first pass, so the newly claimed button needs it back.
                Button bezel = buttons[slot];
                bezel.enabled = true;
                bezel.interactable = true;

                // An unused bezel button may have no handler wired in the scene at all.
                // If this one is bare, route it to the same method the configured ones use.
                if (bezel.onClick.GetPersistentEventCount() == 0)
                {
                    VirtualMFD owner = mfd;
                    bool onLeft = left;
                    bezel.onClick.AddListener(() =>
                    {
                        if (onLeft) owner.PressLeftButton(bezel);
                        else owner.PressRightButton(bezel);
                    });
                }

                // The game only shows bezel buttons while the map is up; match that.
                screen.CloseScreen(Screen.width * (left ? Vector3.left : Vector3.right));

                Plugin.Logger.LogInfo("WMC screen installed on " + (left ? "left" : "right") +
                                      " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("WMC screen install failed: " + e);
            }
        }

        private static void Fail(string reason)
        {
            gaveUp = true;
            screen = null;
            Plugin.Logger.LogWarning(
                "Could not install the WMC MFD screen (" + reason +
                "). Falling back to the map overlay panel.");
        }

        /// <summary>
        /// Find a bezel button with no screen behind it. The stock lists are the same
        /// length as the button lists, with unused entries left null — those are the "-"
        /// buttons the game disables during setup.
        /// </summary>
        private static bool TryClaimSlot(List<Button> buttons, List<MFDScreen> screens, out int slot)
        {
            slot = -1;
            if (buttons == null || screens == null) return false;

            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == null) continue;
                if (i >= screens.Count || screens[i] == null)
                {
                    slot = i;
                    return true;
                }
            }
            return false;
        }

        private static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            foreach (MFDScreen s in screens)
            {
                if (s != null && s.transform.parent != null) return s;
            }
            return null;
        }

        // --------------------------------------------------------------------- building

        private static MFDScreen Build(MFDScreen template, Button bezelButton)
        {
            font = FindFont(template);

            var root = new GameObject("WMC_Screen", typeof(RectTransform), typeof(Image));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(template.transform.parent, worldPositionStays: false);

            // Inherit placement from a working screen so the panel lands where the game
            // expects, then let VirtualMFD drive localPosition for show/hide.
            var templateRt = (RectTransform)template.transform;
            rt.anchorMin = templateRt.anchorMin;
            rt.anchorMax = templateRt.anchorMax;
            rt.pivot = templateRt.pivot;
            rt.localScale = templateRt.localScale;
            rt.anchoredPosition = templateRt.anchoredPosition;

            Image bg = root.GetComponent<Image>();
            bg.color = PanelBackground();
            bg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.SetParent(rt, worldPositionStays: false);
            Stretch(contentRt);

            float y = -Pad;

            y = AddTitle(contentRt, y);
            y = AddShapeSelector(contentRt, y);
            y = AddPostureSelector(contentRt, y);
            y = AddSummary(contentRt, y);
            y = AddRosterArea(contentRt, y);
            y = AddActions(contentRt, y);
            y = AddDebug(contentRt, y);

            // Size the panel to its content instead of leaving a large dead area below,
            // which is how the stock screens read.
            rt.sizeDelta = new Vector2(PanelWidth, Mathf.Abs(y) + Pad);

            // Outline last so it sits above the fills, matching the stock panels.
            Outline(contentRt, new Rect(0f, 0f, PanelWidth, rt.sizeDelta.y), FrameColor());

            MFDScreen s = root.AddComponent<MFDScreen>();
            s.shortName = "WMC";
            s.displayPanel = content;
            s.aircraftOnly = true;
            s.label = FindLabel(bezelButton);
            s.highlight = FindHighlight(bezelButton, template);

            if (s.label == null)
            {
                UnityEngine.Object.Destroy(root);
                Fail("could not find the bezel button label");
                return null;
            }

            return s;
        }

        /// <summary>Centred green title over a rule, as on BOSCALI / TARGET SELECTION / HUD OPTIONS.</summary>
        private static float AddTitle(RectTransform parent, float y)
        {
            Label(parent, "WING COMMAND", new Rect(Pad, y, PanelWidth - Pad * 2f, 26f),
                  Green(), 18f, FontStyles.Normal, TextAlignmentOptions.Center);
            y -= 30f;

            Rule(parent, new Rect(Pad, y, PanelWidth - Pad * 2f, 1f));
            return y - 8f;
        }

        /// <summary>Small caps section heading, matching the stock left-aligned group labels.</summary>
        private static float Heading(RectTransform parent, float y, string text)
        {
            Label(parent, text, new Rect(Pad, y, PanelWidth - Pad * 2f, 18f),
                  Green(), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 20f;
        }

        private static float AddShapeSelector(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;

            Panel(parent, new Rect(Pad, y, w, RowHeight), RowColor());

            Button(parent, "<", new Rect(Pad + 4f, y + 3f, 30f, RowHeight - 6f),
                   () => CycleShape(-1));
            Button(parent, ">", new Rect(Pad + w - 34f, y + 3f, 30f, RowHeight - 6f),
                   () => CycleShape(1));

            shapeLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 76f, RowHeight),
                               Friendly(), 13f, FontStyles.Normal, TextAlignmentOptions.Center);

            return y - (RowHeight + Gap);
        }

        private static float AddPostureSelector(RectTransform parent, float y)
        {
            float w = (PanelWidth - Pad * 2f - Gap) * 0.5f;

            Button(parent, "Defensive", new Rect(Pad, y, w, RowHeight),
                   () => SetPosture(WingPosture.Defensive));
            Button(parent, "Aggressive", new Rect(Pad + w + Gap, y, w, RowHeight),
                   () => SetPosture(WingPosture.Aggressive));

            y -= RowHeight + Gap;

            postureLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 18f),
                                 Green(), 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 22f;
        }

        private static void SetPosture(WingPosture posture)
        {
            WingRegistry wing = Wing();
            if (wing == null) return;

            wing.Posture = posture;
            WingCommandManager.Instance?.Toast("Posture: " + posture.ToString().ToUpperInvariant());
        }

        private static float AddSummary(RectTransform parent, float y)
        {
            summaryLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 20f),
                                 Dim(), 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 24f;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            float h = RowHeight * Plugin.Config2.MaxWingSize.Value + Gap * 2f;

            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, PanelWidth - Pad * 2f, h));

            return y - (h + Gap);
        }

        private static float AddActions(RectTransform parent, float y)
        {
            float w = (PanelWidth - Pad * 2f - Gap) * 0.5f;

            y = Pair(parent, y, w,
                "Add Selected", () => WingCommandManager.Instance?.AddSelectedFromMap(),
                "Recruit Near", () => WingCommandManager.Instance?.Execute(WingAction.RecruitNearest));

            y = Pair(parent, y, w,
                "Rejoin", () => WingCommandManager.Instance?.Execute(WingAction.Rejoin),
                "Engage", () => WingCommandManager.Instance?.Execute(WingAction.Engage));

            y = Pair(parent, y, w,
                "Return To Base", () => WingCommandManager.Instance?.Execute(WingAction.ReturnToBase),
                "Disband", () => WingCommandManager.Instance?.Execute(WingAction.Disband));

            return y;
        }

        private static float AddDebug(RectTransform parent, float y)
        {
            y -= 6f;
            Rule(parent, new Rect(Pad, y, PanelWidth - Pad * 2f, 1f), FrameColor());
            y -= 8f;

            y = Heading(parent, y, "DEBUG");

            float w = PanelWidth - Pad * 2f;

            Button(parent, "Teleport Wing To Formation", new Rect(Pad, y, w, RowHeight),
                   () => WingDebugActions.TeleportWingToFormation(Wing()));
            y -= RowHeight + Gap;

            Button(parent, "Spawn Wing Of My Aircraft", new Rect(Pad, y, w, RowHeight),
                   () => WingDebugActions.SpawnWingLikePlayer(Wing()));

            return y - RowHeight;
        }

        private static float Pair(RectTransform parent, float y, float w,
                                  string leftText, Action leftAction,
                                  string rightText, Action rightAction)
        {
            Button(parent, leftText, new Rect(Pad, y, w, RowHeight), leftAction);
            Button(parent, rightText, new Rect(Pad + w + Gap, y, w, RowHeight), rightAction);
            return y - (RowHeight + Gap);
        }

        // -------------------------------------------------------------------- refreshing

        private static void Refresh(WingRegistry wing)
        {
            if (shapeLabel != null)
                shapeLabel.text = Pretty(Plugin.Config2.Shape.Value);

            if (summaryLabel != null)
                summaryLabel.text = wing.Count + " of " + Plugin.Config2.MaxWingSize.Value + " assigned";

            if (postureLabel != null)
                postureLabel.text = "ROE: " + wing.Posture.ToString().ToUpperInvariant() + PostureHint(wing.Posture);

            SyncRosterRows(wing.Count);

            for (int i = 0; i < rosterRows.Count; i++)
            {
                if (i < wing.Count) rosterRows[i].Bind(wing.Members[i]);
                else rosterRows[i].Hide();
            }
        }

        private static void SyncRosterRows(int needed)
        {
            while (rosterRows.Count < needed && rosterArea != null)
            {
                int index = rosterRows.Count;
                rosterRows.Add(new RosterRow(rosterArea, index));
            }
        }

        /// <summary>One line of the roster: slot, name, order, slot error, release button.</summary>
        private sealed class RosterRow
        {
            private readonly GameObject go;
            private readonly TMP_Text slot, name, order, error;
            private WingMember bound;

            public RosterRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * (RowHeight + 2f);

                go = new GameObject("Row" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                Panel(rt, new Rect(0f, 0f, width, RowHeight), RowColor());

                slot  = Label(rt, "", new Rect(6f, 0f, 18f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                name  = Label(rt, "", new Rect(26f, 0f, 150f, RowHeight), Friendly(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(180f, 0f, 74f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                error = Label(rt, "", new Rect(256f, 0f, 78f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Right);

                Button(rt, "X", new Rect(width - 32f, 3f, 26f, RowHeight - 6f), () =>
                {
                    if (bound != null) WingCommandManager.Instance?.RemoveMember(bound);
                });
            }

            public void Bind(WingMember m)
            {
                bound = m;
                if (!go.activeSelf) go.SetActive(true);

                slot.text = m.Slot.ToString();
                name.text = Truncate(m.Name, 16);
                order.text = ShortOrder(m.Order);

                error.text = ErrorText(m);
                error.color = m.Order == WingOrder.Formation && m.SlotError > 0f && m.SlotError < 250f
                    ? Accent()
                    : Dim();
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }

            private static string ErrorText(WingMember m)
            {
                if (m.Order != WingOrder.Formation) return "-";
                if (m.SlotError <= 0f) return "...";
                return m.SlotError < 10000f
                    ? m.SlotError.ToString("F0") + " m"
                    : (m.SlotError / 1000f).ToString("F1") + " km";
            }
        }

        // ------------------------------------------------------------------ UI helpers

        private static WingRegistry Wing() => WingCommandManager.Instance?.Wing;

        private static string PostureHint(WingPosture posture)
        {
            return posture == WingPosture.Aggressive
                ? "  - breaks for air, leashed"
                : "  - holds slot, mirrors your ground fire";
        }

        private static void CycleShape(int direction)
        {
            var values = (FormationShape[])Enum.GetValues(typeof(FormationShape));
            int index = Array.IndexOf(values, Plugin.Config2.Shape.Value);
            Plugin.Config2.Shape.Value = values[(index + direction + values.Length) % values.Length];
        }

        private static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                      Color color, float size, FontStyles style,
                                      TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.color = color;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>
        /// A framed box. Uses the sprite lifted from a HUD OPTIONS row when available, so
        /// the corners and edge weight match the stock panels exactly; otherwise it falls
        /// back to four hairlines, which reads the same at this size.
        /// </summary>
        private static Image Panel(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);
            rt.SetAsFirstSibling();

            // Interior stays near-transparent and the edges carry the colour — the stock
            // panels are outlined boxes, not filled blocks.
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0f, 0f, 0f, 0.25f);

            Outline(parent, rect, color);
            return img;
        }

        /// <summary>Four hairline edges forming a box.</summary>
        private static Image[] Outline(RectTransform parent, Rect rect, Color color)
        {
            const float t = 1f;
            return new[]
            {
                Rule(parent, new Rect(rect.x, rect.y, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y - rect.height + t, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y, t, rect.height), color),
                Rule(parent, new Rect(rect.x + rect.width - t, rect.y, t, rect.height), color),
            };
        }

        private static Image Rule(RectTransform parent, Rect rect) => Rule(parent, rect, Green());

        private static Image Rule(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// An outlined button in the stock idiom: grey frame and text at rest, green when
        /// hovered — the same on/off treatment HUD OPTIONS gives its MAXIMIZE buttons.
        /// </summary>
        private static void Button(RectTransform parent, string text, Rect rect, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            // A near-transparent fill keeps the whole rect clickable while the frame does
            // the drawing, so the button reads as an outline like the stock controls.
            Image img = go.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0f, 0f, 0f, 0.30f);

            Image[] frame = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), Grey());

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                                   Grey(), 12f, FontStyles.Normal,
                                   TextAlignmentOptions.Center);

            WmcButton behaviour = go.AddComponent<WmcButton>();
            behaviour.Initialise(frame, label, onClick);
        }

        /// <summary>Anchor a rect to the parent's top-left and place it in pixels.</summary>
        private static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.localScale = Vector3.one;
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

        private static TMP_FontAsset FindFont(MFDScreen template)
        {
            TMP_Text any = template.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (any != null) return any.font;

            TMP_Text anywhere = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            return anywhere != null ? anywhere.font : null;
        }

        private static TextMeshProUGUI FindLabel(Button button)
        {
            return button == null
                ? null
                : button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        }

        /// <summary>
        /// Locate the button's highlight image by mirroring the path a working screen uses
        /// on its own button, falling back to any non-Button image on the target.
        /// </summary>
        private static Image FindHighlight(Button button, MFDScreen template)
        {
            if (button == null) return null;

            if (template != null && template.highlight != null)
            {
                string path = PathUnder(template.highlight.transform, out Transform root);
                if (root != null && !string.IsNullOrEmpty(path))
                {
                    Transform found = button.transform.Find(path);
                    if (found != null)
                    {
                        Image img = found.GetComponent<Image>();
                        if (img != null) return img;
                    }
                }
            }

            foreach (Image img in button.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (img.gameObject != button.gameObject) return img;
            }
            return button.GetComponent<Image>();
        }

        private static string PathUnder(Transform t, out Transform root)
        {
            root = null;
            var parts = new List<string>();

            Transform cursor = t;
            while (cursor != null && cursor.GetComponent<Button>() == null)
            {
                parts.Insert(0, cursor.name);
                cursor = cursor.parent;
            }

            root = cursor;
            return string.Join("/", parts.ToArray());
        }

        // ------------------------------------------------------------------- styling

                // ---------------------------------------------------------------------- colours

        /// <summary>The stock "on" colour: what HUD OPTIONS uses for an active control.</summary>
        internal static Color Green()
        {
            try { return ThemeManager.Active.ColorTheme.AllClear; }
            catch { return new Color(0.30f, 1f, 0.35f); }
        }

        /// <summary>The stock "off" colour.</summary>
        internal static Color Grey() => Color.grey;

        private static Color Accent() => Green();

        private static Color Friendly()
        {
            try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
            catch { return new Color(0.45f, 0.95f, 0.55f); }
        }

        private static Color Dim() => Grey();

        private static Color RowColor() => Grey();
        private static Color FrameColor() => new Color(Grey().r, Grey().g, Grey().b, 0.75f);
        private static Color PanelBackground() => new Color(0.05f, 0.07f, 0.09f, 0.93f);

        // ----------------------------------------------------------------------- text

        private static string Pretty(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "Echelon Right";
                case FormationShape.EchelonLeft: return "Echelon Left";
                case FormationShape.LineAbreast: return "Line Abreast";
                case FormationShape.Trail: return "Trail";
                case FormationShape.CombatSpread: return "Combat Spread";
                default: return shape.ToString();
            }
        }

        private static string ShortOrder(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation: return "FORM";
                case WingOrder.Engage: return "ENGAGE";
                case WingOrder.ReturnToBase: return "RTB";
                default: return order.ToString();
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }

    /// <summary>Minimal clickable button with a hover tint, so no stock UI script is reused.</summary>
    internal class WmcButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image[] frame;
        private TMP_Text label;
        private Action onClick;

        public void Initialise(Image[] frame, TMP_Text label, Action onClick)
        {
            this.frame = frame;
            this.label = label;
            this.onClick = onClick;
        }

        private void Tint(Color color)
        {
            if (frame != null)
            {
                foreach (Image edge in frame)
                {
                    if (edge != null) edge.color = color;
                }
            }
            if (label != null) label.color = color;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            try { onClick?.Invoke(); }
            catch (Exception e) { Plugin.Logger.LogError("WMC button failed: " + e); }
        }

        public void OnPointerEnter(PointerEventData eventData) => Tint(WmcScreen.Green());

        public void OnPointerExit(PointerEventData eventData) => Tint(WmcScreen.Grey());
    }
}
