using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// The widget vocabulary the mod's panels are drawn with: framed boxes, hairline rules,
    /// outlined buttons, and the sliced sprite that reproduces the stock panel card.
    ///
    /// All of this used to live as private statics inside <see cref="WmcScreen"/>, which is
    /// why the WMC page looked native and the aircraft-recovery prompt did not — the second
    /// surface could not reach the first one's drawing code, so it was written in IMGUI with
    /// a palette of its own invention. Nothing here is WMC-specific; it is simply where the
    /// stock look is defined.
    ///
    /// Colours come from <see cref="UiTheme"/>, which reads the game's live theme and falls
    /// back safely before the UI has initialised. The spacing and type constants below are
    /// the panel's whole scale: a four-pixel rhythm and five text sizes. Anything laid out
    /// with a number that is not one of these has drifted off the grid.
    /// </summary>
    internal static class WingUi
    {
        // ------------------------------------------------------------------- spacing

        /// <summary>The spacing rhythm. Every inset and gap on the panel is one of these.</summary>
        public const float Space1 = 4f;
        public const float Space2 = 8f;
        public const float Space3 = 12f;
        public const float Space4 = 16f;
        public const float Space5 = 20f;
        public const float Space6 = 24f;

        /// <summary>Standard control height, matching the stock panel rows.</summary>
        public const float RowHeight = 30f;

        /// <summary>Height of a page selector, which sits above the content it switches.</summary>
        public const float TabHeight = 32f;

        /// <summary>Standard gap between adjacent controls.</summary>
        public const float Gap = Space1;

        /// <summary>Standard inset from a panel edge.</summary>
        public const float Pad = Space3;

        /// <summary>Vertical pitch between stacked list rows.</summary>
        public const float RowPitch = RowHeight + 2f;

        // ---------------------------------------------------------------------- type

        /// <summary>
        /// Five sizes, not the six near-identical ones this panel used to mix.
        ///
        /// The old set ran 9/10/11/12/13/18, which is close enough to continuous that no
        /// step in it read as a step: a 9px column header and an 11px value looked like the
        /// same rank of information. Nine pixels of grey on a dark panel is also below what
        /// is comfortably readable, so the floor is ten.
        /// </summary>
        public const float FontMicro = 10f;

        /// <summary>Secondary values and dense table cells.</summary>
        public const float FontSmall = 11f;

        /// <summary>The default: control labels and roster values.</summary>
        public const float FontBody = 12f;

        /// <summary>A line that should be read first inside its own block.</summary>
        public const float FontLead = 14f;

        /// <summary>The panel title.</summary>
        public const float FontTitle = 18f;

        private static TMP_FontAsset font;
        private static Sprite panelSprite;

        /// <summary>
        /// The font every label is built with.
        ///
        /// Resolved from whatever TextMeshPro text the game already has on screen, so panels
        /// inherit the game's typeface rather than Unity's fallback. A caller that has a
        /// better template — the WMC page has an actual MFD screen to copy — assigns it.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;

                TMP_Text any = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
                font = any != null ? any.font : null;
                return font;
            }
            set { if (value != null) font = value; }
        }

        /// <summary>Drop the cached font when a mission ends; the next scene resolves its own.</summary>
        public static void Reset() => font = null;

        // ---------------------------------------------------------------------- colours

        /// <summary>The stock "on" colour: what HUD OPTIONS uses for an active control.</summary>
        public static Color Green => UiTheme.Green;

        /// <summary>The stock "off" colour.</summary>
        public static Color Grey => Color.grey;

        public static Color Friendly => UiTheme.Friendly;

        public static Color Warning => UiTheme.Warning;

        public static Color Alert => UiTheme.Alert;

        /// <summary>
        /// Secondary text: present, but not asking to be read.
        ///
        /// Lightened off flat <c>Color.grey</c>. Half-grey on the panel ground clears about
        /// four to one, and the panel spends most of this colour on ten- and eleven-pixel
        /// text — hint lines, column headers, table cells — which is exactly the case the
        /// 4.5:1 floor exists for. Deepening the panel did part of the work; this is the
        /// rest of it.
        /// </summary>
        public static Color Dim => new Color(0.64f, 0.68f, 0.70f);

        /// <summary>Text and edges of a control that is present but currently inert.</summary>
        public static Color Disabled => new Color(0.42f, 0.45f, 0.46f, 0.75f);

        /// <summary>A frame that should recede behind its contents.</summary>
        public static Color FrameColor => new Color(0.52f, 0.55f, 0.56f, 0.55f);

        /// <summary>
        /// Section headings, one rank below the panel title.
        ///
        /// Headings used to be drawn in the same full-strength accent as the title, so a
        /// page opened with half a dozen things all shouting at the same volume and no way
        /// to tell the name of the panel from the name of a block inside it.
        /// </summary>
        public static Color HeadingColor
        {
            get
            {
                Color c = Friendly;
                return new Color(c.r * 0.88f, c.g * 0.88f, c.b * 0.88f, 1f);
            }
        }

        /// <summary>
        /// The panel ground.
        ///
        /// Darker and more opaque than it was. The map underneath is busy, and at the old
        /// 84% the terrain read straight through the panel and competed with the text on top
        /// of it; deepening the fill also buys back the contrast the small grey text needs.
        /// </summary>
        private static Color PanelBackground => Unity(UiPalette.PanelGround);
        private static Color PanelEdge => new Color(0.30f, 0.34f, 0.36f, 1f);
        private static Color PanelShadow => new Color(0.06f, 0.07f, 0.08f, 1f);

        /// <summary>The interior of a framed box: a hole punched in the panel, not a block.</summary>
        public static Color CardFill => new Color(0f, 0f, 0f, UiPalette.RowRestShade);

        /// <summary>The same interior, lifted for a row the pointer is over.</summary>
        public static Color CardFillHover =>
            Unity(UiPalette.Wash(Rgba(Green), UiPalette.RowHoverScale, UiPalette.RowHoverAlpha));

        /// <summary>The same interior for a row that is currently selected.</summary>
        public static Color CardFillSelected =>
            Unity(UiPalette.Wash(Rgba(Green), UiPalette.RowSelectedScale,
                                 UiPalette.RowSelectedAlpha));

        // ---------------------------------------------------------------------- widgets

        public static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                     Color color, float size, FontStyles style,
                                     TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset resolved = Font;
            if (resolved != null) t.font = resolved;
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
        /// A framed box: a near-transparent interior with the colour carried on the edges,
        /// because the stock panels are outlined boxes rather than filled blocks.
        /// </summary>
        public static Image Panel(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);
            rt.SetAsFirstSibling();

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = CardFill;

            Outline(parent, rect, color);
            return img;
        }

        /// <summary>Four hairline edges forming a box.</summary>
        public static Image[] Outline(RectTransform parent, Rect rect, Color color)
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

        public static Image Rule(RectTransform parent, Rect rect) => Rule(parent, rect, Green);

        public static Image Rule(RectTransform parent, Rect rect, Color color)
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
        /// An outlined button in the stock idiom: a frame and a label, the frame carrying
        /// the state.
        /// </summary>
        public static WingButton Button(RectTransform parent, string text, Rect rect, Action onClick) =>
            Button(parent, text, rect, FontBody, UiButtonStyle.Default, onClick);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        float fontSize, Action onClick) =>
            Button(parent, text, rect, fontSize, UiButtonStyle.Default, onClick);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        UiButtonStyle style, Action onClick) =>
            Button(parent, text, rect, FontBody, style, onClick);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        float fontSize, UiButtonStyle style, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            // The fill is what separates the four states from one another. At rest it is
            // near-black and the frame does all the drawing, so the control still reads as
            // an outline like the stock ones; selected and pressed wash it with the accent,
            // which is a cue hover cannot be confused with because hover never fills.
            Image fill = go.GetComponent<Image>();
            fill.raycastTarget = true;

            Image[] frame = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), Grey);

            // A page selector is underlined rather than boxed on its active edge, so the
            // lit tab reads as attached to the page below it.
            Image underline = style == UiButtonStyle.Tab
                ? Rule(rt, new Rect(0f, -(rect.height - 3f), rect.width, 3f), Color.clear)
                : null;

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                                   Grey, fontSize, FontStyles.Normal,
                                   TextAlignmentOptions.Center);

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.Initialise(style, fill, frame, underline, label, onClick);
            return behaviour;
        }

        /// <summary>An invisible click target over an area that draws itself.</summary>
        public static WingButton HitButton(RectTransform parent, Rect rect, Action onClick)
        {
            var go = new GameObject("HitTarget", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image hit = go.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.InitialiseHit(onClick);
            return behaviour;
        }

        // ------------------------------------------------------------------ text entry

        /// <summary>
        /// A single-line text field in the panel's idiom.
        ///
        /// The field carries a real risk the rest of this file does not: while it has focus
        /// every keystroke is also a flight control, so typing a template name would roll the
        /// aircraft and fire its weapons. <see cref="WingKeyboardGuard"/> holds the game's
        /// keyboard off for exactly as long as the field is focused, the same way the game's
        /// own chat box does.
        /// </summary>
        public static TMP_InputField InputField(RectTransform parent, Rect rect,
                                                int characterLimit, Action<string> onChanged,
                                                string tooltip = null)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image background = go.GetComponent<Image>();
            background.color = CardFill;
            background.raycastTarget = true;
            Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor);

            // The viewport clips the text; TMP_InputField insists on one and will build a
            // broken field without it.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(rt, worldPositionStays: false);
            Place(viewport, new Rect(Space2, 0f, rect.width - Space2 * 2f, rect.height));

            TMP_Text text = Label(viewport, "", new Rect(0f, 0f, rect.width - Space2 * 2f,
                                                         rect.height),
                                  Friendly, FontBody, FontStyles.Normal,
                                  TextAlignmentOptions.Left);
            text.raycastTarget = false;

            TMP_Text placeholder = Label(viewport, "NAME",
                                         new Rect(0f, 0f, rect.width - Space2 * 2f, rect.height),
                                         Disabled, FontBody, FontStyles.Italic,
                                         TextAlignmentOptions.Left);
            placeholder.raycastTarget = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.richText = false;
            field.restoreOriginalTextOnEscape = true;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = Green;
            field.selectionColor = new Color(Green.r, Green.g, Green.b, 0.35f);

            // Commit on end-edit rather than per keystroke: the value is written to a config
            // file, and saving it once per character typed is a file write per character.
            if (onChanged != null) field.onEndEdit.AddListener(v => onChanged(v));

            field.onSelect.AddListener(_ => WingKeyboardGuard.Capture());
            field.onDeselect.AddListener(_ => WingKeyboardGuard.Release());

            if (!string.IsNullOrEmpty(tooltip))
                go.AddComponent<WingHoverNote>().Note = tooltip;

            return field;
        }

        // ---------------------------------------------------------------------- popup

        /// <summary>
        /// An overlay list anchored under a control: the panel's dropdown.
        ///
        /// It exists because the two lists this rework needs — the stores that fit a pylon,
        /// and the templates saved for an airframe — are both far too tall to give permanent
        /// room to on a panel already carrying four pages. Drawn as a late sibling of the
        /// page root so it covers the content beneath it, behind a full-page scrim that
        /// swallows the click that dismisses it.
        ///
        /// One popup is open at a time, tracked statically, because there is one pointer and
        /// two open lists would be two things claiming the same clicks.
        /// </summary>
        public sealed class Popup
        {
            private readonly GameObject root;
            private readonly RectTransform listRect;
            private readonly Image listGround;
            private readonly List<PopupRow> rows = new List<PopupRow>();

            private static Popup open;

            /// <summary>
            /// Rows the list draws at once.
            ///
            /// Seven, which is as much as can hang off a control without the list running
            /// past the bottom of the panel from a row near the foot of the page. A longer
            /// list pages: the last row becomes the page turn.
            /// </summary>
            public const int MaxRows = 7;

            /// <summary>Content rows on a list long enough to need the page-turn row.</summary>
            private const int PagedRows = MaxRows - 1;

            public Popup(RectTransform pageRoot, float panelWidth)
            {
                root = new GameObject("Popup", typeof(RectTransform));
                var rt = root.GetComponent<RectTransform>();
                rt.SetParent(pageRoot, worldPositionStays: false);
                Stretch(rt);

                // The scrim is the whole page, transparent, and eats any click that is not
                // on a row. Without it a click meant to dismiss the list lands on whatever
                // control happens to be underneath and does that instead.
                HitButton(rt, new Rect(0f, 0f, panelWidth, 4000f), Close);

                // The list gets an opaque ground and a frame of its own. It is drawn over
                // live content rather than over the panel card, so without both it reads as
                // rows floating on top of the controls they are covering.
                var listGo = new GameObject("PopupList", typeof(RectTransform), typeof(Image));
                listRect = listGo.GetComponent<RectTransform>();
                listRect.SetParent(rt, worldPositionStays: false);

                // The stock card sprite rather than a hand-built outline: it is sliced, so it
                // keeps its edge and corners at whatever height the list turns out to be,
                // and a hairline Outline would have to be rebuilt on every open.
                listGround = listGo.GetComponent<Image>();
                listGround.sprite = PanelSprite();
                listGround.type = Image.Type.Sliced;
                listGround.color = Color.white;
                listGround.raycastTarget = true;

                root.SetActive(false);
            }

            public bool IsOpen => root != null && root.activeSelf;

            /// <summary>Close whatever list is open, wherever it is.</summary>
            public static void CloseAny() => open?.Close();

            /// <summary>
            /// Show the list at a position, bound to a set of entries.
            ///
            /// The caller owns the entries and what selecting one means; this only draws
            /// them and reports the index back.
            /// </summary>
            public void Show(Rect area, IReadOnlyList<PopupEntry> entries, Action<int> onPick)
            {
                page = 0;
                Render(area, entries, onPick);
            }

            /// <summary>
            /// Draw one page of the list.
            ///
            /// Separate from <see cref="Show"/> so the page-turn row can call it again with
            /// the same arguments. A list that fits needs none of this; a pylon offering a
            /// dozen stores does, and silently showing the first six of them would be a menu
            /// that lies about what the aircraft can carry.
            /// </summary>
            private void Render(Rect area, IReadOnlyList<PopupEntry> entries, Action<int> onPick)
            {
                if (root == null) return;

                open?.Close();
                open = this;

                int total = entries?.Count ?? 0;
                bool paged = total > MaxRows;
                int perPage = paged ? PagedRows : MaxRows;
                int pages = paged ? Mathf.CeilToInt(total / (float)perPage) : 1;

                page = pages > 0 ? ((page % pages) + pages) % pages : 0;

                int first = page * perPage;
                int shown = Mathf.Min(perPage, total - first);
                int used = shown + (paged ? 1 : 0);

                float height = Mathf.Max(RowPitch, RowPitch * used) + Space1 * 2f;
                Place(listRect, new Rect(area.x, area.y, area.width, height));

                while (rows.Count < MaxRows) rows.Add(new PopupRow(listRect, rows.Count));

                for (int i = 0; i < rows.Count; i++)
                {
                    if (i < shown)
                    {
                        int index = first + i;
                        rows[i].Bind(entries[index], area.width, () =>
                        {
                            Close();
                            onPick?.Invoke(index);
                        });
                    }
                    else if (paged && i == shown)
                    {
                        // The page turn is a row of the list rather than an arrow beside it:
                        // the list is already a floating thing over live content, and giving
                        // it furniture of its own would make it a second panel.
                        rows[i].Bind(
                            new WingUi.PopupEntry(
                                "MORE...", "page " + (page + 1) + " of " + pages),
                            area.width,
                            () =>
                            {
                                page++;
                                Render(area, entries, onPick);
                            });
                    }
                    else
                    {
                        rows[i].Hide();
                    }
                }

                root.SetActive(true);

                // Last sibling so it draws over the page it is covering. Re-asserted on every
                // open because anything built after it would otherwise sit on top.
                root.transform.SetAsLastSibling();
            }

            /// <summary>Which page of a long list is showing.</summary>
            private int page;

            public void Close()
            {
                if (root == null) return;
                root.SetActive(false);
                if (ReferenceEquals(open, this)) open = null;
            }
        }

        /// <summary>One line of a <see cref="Popup"/>: what it says and whether it is current.</summary>
        public readonly struct PopupEntry
        {
            public readonly string Text;
            public readonly string Detail;
            public readonly bool Selected;
            public readonly bool Enabled;

            public PopupEntry(string text, string detail = null, bool selected = false,
                              bool enabled = true)
            {
                Text = text;
                Detail = detail;
                Selected = selected;
                Enabled = enabled;
            }
        }

        private sealed class PopupRow
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly TMP_Text label;
            private readonly TMP_Text detail;
            private readonly WingButton hit;
            private readonly int index;

            public PopupRow(RectTransform parent, int index)
            {
                this.index = index;
                go = new GameObject("PopupRow" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);

                fill = go.GetComponent<Image>();
                fill.color = PanelBackground;
                fill.raycastTarget = true;

                label = Label(rt, "", new Rect(Space2, 0f, 10f, RowHeight), Friendly, FontSmall,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, "", new Rect(0f, 0f, 10f, RowHeight), Dim, FontMicro,
                               FontStyles.Normal, TextAlignmentOptions.Right);

                hit = HitButton(rt, new Rect(0f, 0f, 10f, RowHeight), null);
                go.SetActive(false);
            }

            public void Bind(PopupEntry entry, float width, Action onPick)
            {
                Place((RectTransform)go.transform,
                      new Rect(0f, -(RowPitch * index) - Space1, width, RowHeight));

                float detailWidth = string.IsNullOrEmpty(entry.Detail) ? 0f : 96f;
                Place((RectTransform)label.transform,
                      new Rect(Space2, 0f, width - Space2 * 2f - detailWidth, RowHeight));
                Place((RectTransform)detail.transform,
                      new Rect(width - Space2 - detailWidth, 0f, detailWidth, RowHeight));
                Place((RectTransform)hit.transform, new Rect(0f, 0f, width, RowHeight));

                label.text = entry.Text ?? "";
                label.color = !entry.Enabled ? Disabled : entry.Selected ? Green : Friendly;
                detail.text = entry.Detail ?? "";

                Color rest = entry.Selected ? CardFillSelected : PanelBackground;
                hit.SetRowHighlight(fill, rest, CardFillHover);
                hit.SetAction(entry.Enabled ? onPick : null);
                hit.SetEnabled(entry.Enabled);

                if (!go.activeSelf) go.SetActive(true);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }

        /// <summary>
        /// Section heading with a rule running out to the right of it, which is how the stock
        /// panels separate their groups.
        /// </summary>
        public static float Heading(RectTransform parent, float y, string text, float width)
        {
            float labelWidth = 8f * text.Length + 10f;

            Label(parent, text, new Rect(Pad, y, labelWidth, Space4),
                  HeadingColor, FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);

            float ruleX = Pad + labelWidth + 6f;
            Rule(parent, new Rect(ruleX, y - Space2, width - Pad - ruleX, 1f), FrameColor);

            return y - Space5;
        }

        /// <summary>Anchor a rect to the parent's top-left and place it in pixels.</summary>
        public static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.localScale = Vector3.one;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // -------------------------------------------------------------- button palette

        /// <summary>The live theme, in the Unity-free form <see cref="UiPalette"/> works in.</summary>
        private static UiPaletteInputs PaletteInputs => new UiPaletteInputs
        {
            Accent = Rgba(Green),
            Alert = Rgba(Alert),
            Frame = Rgba(FrameColor),
            Dim = Rgba(Dim),
            Disabled = Rgba(Disabled),
        };

        /// <summary>
        /// Resolve a button's colours from everything that is currently true about it.
        ///
        /// One function rather than a tint call at each event, because the states overlap —
        /// a latched button can also be hovered, and a disabled one must ignore both — and
        /// the old code answered that by letting whichever handler fired last win. Hovering
        /// a selected button and moving away used to leave it looking unselected.
        ///
        /// The decision itself lives in <see cref="UiPalette"/>, which knows nothing about
        /// Unity and so can be measured in the test project rather than by squinting at a
        /// running mission.
        /// </summary>
        internal static UiButtonPaint Paint(UiButtonStyle style, bool enabled,
                                            bool latched, bool hover, bool pressed) =>
            UiPalette.Paint(style, PaletteInputs, enabled, latched, hover, pressed);

        private static Rgba Rgba(Color c) => new Rgba(c.r, c.g, c.b, c.a);

        public static Color Unity(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        // ----------------------------------------------------------------- panel sprite

        /// <summary>
        /// Reproduce the stock mission/menu card: a translucent slate fill, a soft grey
        /// two-pixel edge, and small rounded corners. A sliced sprite keeps the treatment
        /// consistent at any panel size or resolution.
        /// </summary>
        public static Sprite PanelSprite()
        {
            if (panelSprite != null) return panelSprite;

            const int size = 32;
            const float radius = 5f;
            const float edge = 3f;
            const float shadow = 1f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_VanillaPanel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = size * 0.5f;
            float half = size * 0.5f;
            Color fill = PanelBackground;
            Color frame = PanelEdge;
            Color shadowColor = PanelShadow;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, half + shadow, radius + shadow);
                    float coverage = Mathf.Clamp01(0.5f - outer);
                    if (coverage <= 0f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float actual = RoundedDistance(x + 0.5f, y + 0.5f, centre, half, radius);
                    float innerHalf = half - edge;
                    float inner = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, innerHalf, Mathf.Max(1f, radius - edge));
                    Color pixel = actual > 0.5f
                        ? shadowColor
                        : inner <= -0.5f ? fill : frame;
                    pixel.a *= coverage;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            panelSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));
            panelSprite.name = "WingCommand_VanillaPanelSprite";
            panelSprite.hideFlags = HideFlags.HideAndDontSave;
            return panelSprite;
        }

        private static float RoundedDistance(float x, float y, float centre,
                                             float half, float radius)
        {
            float qx = Mathf.Abs(x - centre) - (half - radius);
            float qy = Mathf.Abs(y - centre) - (half - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }
    }

    /// <summary>
    /// Minimal clickable button, so no stock UI script is reused.
    ///
    /// It carries four independent facts — enabled, latched, hovered, pressed — and repaints
    /// from all four at once through <see cref="WingUi.Paint"/>. It used to tint from
    /// whichever event fired last, which is why hovering a selected control and moving away
    /// left it looking unselected, and why a selected control and a hovered one were the
    /// same shade of white to begin with.
    /// </summary>
    internal class WingButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
                                IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private UiButtonStyle style;
        private Image fill;
        private Image[] frame;
        private Image underline;
        private TMP_Text label;
        private Action onClick;

        private bool latched;
        private bool interactable = true;
        private bool hovered;
        private bool pressed;
        private bool decorated;

        // A hit target laid over something that draws itself — a roster row — still owes
        // the reader a hover cue, so it tints the row's own fill instead of its own.
        private Image rowFill;
        private Color rowRest;
        private Color rowHover;

        private string tooltip;

        /// <summary>
        /// What the control under the pointer says about itself, or null when the pointer
        /// is over nothing that explains itself.
        ///
        /// A static rather than an event, because there is exactly one pointer and exactly
        /// one place on the panel that reports what it is over. Cleared by whichever button
        /// the pointer leaves, so a control destroyed while hovered — a roster row being
        /// hidden, a page being switched away from — cannot leave its description stranded
        /// on screen.
        /// </summary>
        public static string HoveredTooltip { get; private set; }

        public static void ClearTooltip() => HoveredTooltip = null;

        /// <summary>Publish a note from something that is not a button. See <see cref="WingHoverNote"/>.</summary>
        internal static void PublishExternal(string text, bool entering)
        {
            if (entering)
            {
                if (!string.IsNullOrEmpty(text)) HoveredTooltip = text;
            }
            else if (!string.IsNullOrEmpty(text) && HoveredTooltip == text)
            {
                HoveredTooltip = null;
            }
        }

        /// <summary>Describe this control for the panel's status line. Null disables it.</summary>
        public WingButton WithTooltip(string text)
        {
            tooltip = text;
            return this;
        }

        private void PublishTooltip(bool entering)
        {
            if (entering)
            {
                if (!string.IsNullOrEmpty(tooltip)) HoveredTooltip = tooltip;
            }
            else if (!string.IsNullOrEmpty(tooltip) && HoveredTooltip == tooltip)
            {
                HoveredTooltip = null;
            }
        }

        public void Initialise(UiButtonStyle style, Image fill, Image[] frame,
                               Image underline, TMP_Text label, Action onClick)
        {
            this.style = style;
            this.fill = fill;
            this.frame = frame;
            this.underline = underline;
            this.label = label;
            this.onClick = onClick;
            decorated = true;
            Apply();
        }

        /// <summary>A bare click target with nothing of its own to paint.</summary>
        public void InitialiseHit(Action onClick)
        {
            this.onClick = onClick;
            decorated = false;
        }

        /// <summary>
        /// Give a bare hit target a row background to light up while the pointer is on it.
        ///
        /// Called again whenever the row's resting colour changes, so hover and selection do
        /// not fight over the same image: the row owns what it looks like at rest, and this
        /// only ever adds the pointer's contribution on top.
        /// </summary>
        public void SetRowHighlight(Image target, Color rest, Color hover)
        {
            rowFill = target;
            rowRest = rest;
            rowHover = hover;
            if (rowFill != null) rowFill.color = hovered && interactable ? rowHover : rowRest;
        }

        /// <summary>Hold this button lit, for a selected option in a group.</summary>
        public void SetLatched(bool on)
        {
            if (latched == on) return;
            latched = on;
            Apply();
        }

        public void SetEnabled(bool on)
        {
            if (interactable == on) return;
            interactable = on;
            if (!interactable) { hovered = false; pressed = false; }
            Apply();
        }

        /// <summary>
        /// Point this control at a different action without rebuilding it.
        ///
        /// For rows that are rebound rather than recreated — a popup line stands for a
        /// different store every time the list opens, and a row still wired to the previous
        /// list's action is the worst kind of bug a menu can have. A null action leaves the
        /// row inert.
        /// </summary>
        public void SetAction(Action action) => onClick = action;

        /// <summary>Set the label without rebuilding the button.</summary>
        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        private void Apply()
        {
            if (rowFill != null) rowFill.color = hovered && interactable ? rowHover : rowRest;
            if (!decorated) return;

            UiButtonPaint paint =
                WingUi.Paint(style, interactable, latched, hovered, pressed);

            if (fill != null) fill.color = WingUi.Unity(paint.Fill);
            if (label != null) label.color = WingUi.Unity(paint.Text);

            if (frame != null)
            {
                Color edgeColor = WingUi.Unity(paint.Frame);
                foreach (Image edge in frame)
                {
                    if (edge != null) edge.color = edgeColor;
                }
            }

            // The tab's underline is the cue that survives at a glance: it is the only mark
            // on the strip that says which page you are on without reading four words.
            if (underline != null)
                underline.color = latched && interactable ? WingUi.Unity(paint.Frame) : Color.clear;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!interactable) return;

            try { onClick?.Invoke(); }
            catch (Exception e) { Plugin.Logger.LogError("Wing UI button failed: " + e); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            PublishTooltip(entering: true);
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            PublishTooltip(entering: false);
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            pressed = false;
            Apply();
        }

        /// <summary>
        /// A roster row is hidden by deactivating it, which never delivers the pointer-exit
        /// the hover state is waiting for. Without this a row left mid-hover comes back lit.
        /// </summary>
        private void OnDisable()
        {
            if (!hovered && !pressed) return;
            hovered = false;
            pressed = false;
            PublishTooltip(entering: false);
            Apply();
        }
    }
}
