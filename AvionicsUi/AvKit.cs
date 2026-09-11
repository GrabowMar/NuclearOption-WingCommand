using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Core widget construction toolkit for Nuclear Option cockpit avionics MFDs.
    /// Provides chamfered panels, tactile cards, segmented tabs, buttons, steppers,
    /// meters, and input-guarded controls.
    /// </summary>
    public static class AvKit
    {
        // ---------------------------------------------------------------- Placement
        public static void Place(RectTransform target, Rect area)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(area.x, area.y);
            target.sizeDelta = new Vector2(area.width, area.height);
            target.localScale = Vector3.one;
        }

        public static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.pivot = new Vector2(0.5f, 0.5f);
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.localScale = Vector3.one;
        }

        /// <summary>
        /// Nudge a built panel until it lies wholly inside its canvas.
        ///
        /// An MFD screen inherits its position from the stock template it was cloned beside,
        /// and that position was chosen for the stock panel's width. Widening the mod panels
        /// to 470 pushed the left-bezel screens off the left edge and the right-bezel ones
        /// off the right, clipping the first and last column of every page. Rather than
        /// hand-tuning an offset per screen — which would be wrong again at the next size or
        /// resolution — measure where the panel actually landed and move it back inside.
        ///
        /// Call this once, after <c>sizeDelta</c> is final and before <c>MFDScreen.Start</c>
        /// captures the screen's home position, or the correction is captured as the hidden
        /// position instead.
        /// </summary>
        public static void ClampIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;

            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(corners[i]);
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }

            Rect bounds = canvasRt.rect;

            // Correct the overhanging edge. When the panel is larger than the canvas on an
            // axis, pin the leading edge rather than oscillating between the two.
            float dx = 0f;
            if (minX < bounds.xMin + margin) dx = bounds.xMin + margin - minX;
            else if (maxX > bounds.xMax - margin) dx = bounds.xMax - margin - maxX;

            float dy = 0f;
            if (maxY > bounds.yMax - margin) dy = bounds.yMax - margin - maxY;
            else if (minY < bounds.yMin + margin) dy = bounds.yMin + margin - minY;

            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            // The delta is in canvas space; the panel is positioned in its parent's, which
            // may carry a different scale.
            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }

        // ---------------------------------------------------------------- Primitives
        public static TMP_Text Label(
            RectTransform parent, string text, Rect area, Color color,
            float size = AvTokens.FontBody, FontStyles style = FontStyles.Normal,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            bool wrap = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            var label = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = AvFont.Font;
            if (font != null) label.font = font;

            label.text = text;
            label.color = color;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.enableWordWrapping = wrap;
            label.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Image Panel(RectTransform parent, Rect area, Color color, Sprite sprite = null)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            Image img = go.GetComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Rule(RectTransform parent, Rect area, Color color) =>
            Panel(parent, area, color);

        public static Image[] Outline(RectTransform parent, Rect area, Color color)
        {
            const float t = 1f;
            return new[]
            {
                Rule(parent, new Rect(area.x, area.y, area.width, t), color),
                Rule(parent, new Rect(area.x, area.y - area.height + t, area.width, t), color),
                Rule(parent, new Rect(area.x, area.y, t, area.height), color),
                Rule(parent, new Rect(area.x + area.width - t, area.y, t, area.height), color),
            };
        }

        /// <summary>
        /// Renders four 6x1px L-brackets at the card corners in hairline color.
        /// </summary>
        public static void CornerTicks(RectTransform parent, Rect area, Color color, float len = 6f)
        {
            const float t = 1f;
            // Top-Left
            Rule(parent, new Rect(area.x, area.y, len, t), color);
            Rule(parent, new Rect(area.x, area.y, t, len), color);

            // Top-Right
            Rule(parent, new Rect(area.x + area.width - len, area.y, len, t), color);
            Rule(parent, new Rect(area.x + area.width - t, area.y, t, len), color);

            // Bottom-Left
            Rule(parent, new Rect(area.x, area.y - area.height + t, len, t), color);
            Rule(parent, new Rect(area.x, area.y - area.height + len, t, len), color);

            // Bottom-Right
            Rule(parent, new Rect(area.x + area.width - len, area.y - area.height + t, len, t), color);
            Rule(parent, new Rect(area.x + area.width - t, area.y - area.height + len, t, len), color);
        }

        // ---------------------------------------------------------------- Components
        public static (Image CardFill, Image Rail) TacticalCard(
            RectTransform parent, Rect area, Color railColor, bool hasRail = true)
        {
            Image bg = Panel(parent, area, AvTheme.Surface, AvSprites.Card);
            Outline(parent, area, AvTheme.Hairline);
            CornerTicks(parent, area, AvTheme.Hairline);

            Image rail = null;
            if (hasRail)
            {
                rail = Rule(parent, new Rect(area.x, area.y, 3f, area.height), railColor);
            }

            return (bg, rail);
        }

        public static (Image Background, TMP_Text Label) Chip(
            RectTransform parent, string text, Rect area, Color railColor, Color textColor,
            float fontSize = AvTokens.FontMicro)
        {
            Image bg = Panel(parent, area, new Color(railColor.r * 0.15f, railColor.g * 0.15f, railColor.b * 0.15f, 0.85f), AvSprites.Control);
            Outline(parent, area, new Color(railColor.r, railColor.g, railColor.b, 0.45f));

            TMP_Text lbl = Label(parent, text, area, textColor, fontSize, FontStyles.Bold, TextAlignmentOptions.Center);
            return (bg, lbl);
        }

        public static (Image Background, TMP_Text Label) StatusChip(
            RectTransform parent, string text, Rect area, Color railColor, Color textColor,
            float fontSize = AvTokens.FontMicro) => Chip(parent, text, area, railColor, textColor, fontSize);

        public static float Heading(
            RectTransform parent, float y, string text, float width,
            Color? textColor = null, Color? ruleColor = null)
        {
            Color tCol = textColor ?? AvTheme.Dim;
            Color rCol = ruleColor ?? AvTheme.Frame;

            TMP_Text label = Label(parent, text, new Rect(AvTokens.Pad, y, width - AvTokens.Pad * 2f, AvTokens.Space4),
                                   tCol, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Left);
            float labelWidth = Mathf.Ceil(label.GetPreferredValues(text).x);

            float ruleX = AvTokens.Pad + labelWidth + AvTokens.Space2;
            Rule(parent, new Rect(ruleX, y - AvTokens.Space2, Mathf.Max(0f, width - AvTokens.Pad - ruleX), 1f), rCol);

            return y - AvTokens.Space5;
        }

        public static AvButton Button(
            RectTransform parent, string text, Rect area, Action onClick,
            float fontSize = AvTokens.FontBody, AvButtonStyle style = AvButtonStyle.Default)
        {
            var go = new GameObject("AvButton", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            Image fill = go.GetComponent<Image>();
            fill.sprite = AvSprites.Control;
            fill.type = Image.Type.Sliced;
            fill.raycastTarget = true;

            Image[] frame = Outline(rt, new Rect(0f, 0f, area.width, area.height), AvTheme.Frame);

            Image underline = style == AvButtonStyle.Tab
                ? Rule(rt, new Rect(0f, -(area.height - 2f), area.width, 2f), Color.clear)
                : null;

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, area.width, area.height),
                                   AvTheme.Accent, fontSize, FontStyles.Bold, TextAlignmentOptions.Center);

            AvButton btn = go.AddComponent<AvButton>();
            btn.Initialise(style, fill, frame, underline, label, onClick);
            return btn;
        }

        public static AvButton HitButton(RectTransform parent, Rect area, Action onClick)
        {
            var go = new GameObject("HitTarget", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            AvButton btn = go.AddComponent<AvButton>();
            btn.InitialiseHit(onClick);
            return btn;
        }

        public static AvButton Tab(RectTransform parent, string text, Rect area, Action onClick) =>
            Button(parent, text, area, onClick, AvTokens.FontSmall, AvButtonStyle.Tab);

        public static AvButton[] Stepper(
            RectTransform parent, float x, float y, float w, out TMP_Text valueLabel,
            Action onPrev, Action onNext, string tooltip = null)
        {
            Panel(parent, new Rect(x, y, w, AvTokens.RowHeight), AvTheme.SurfaceInert, AvSprites.Control);
            Outline(parent, new Rect(x, y, w, AvTokens.RowHeight), AvTheme.Frame);

            const float arrowWidth = AvTokens.Space6 + AvTokens.Space1;
            AvButton prev = Button(parent, "<", new Rect(x + 1f, y - 1f, arrowWidth, AvTokens.RowHeight - 2f),
                                   onPrev, AvTokens.FontBody, AvButtonStyle.Quiet);
            AvButton next = Button(parent, ">", new Rect(x + w - arrowWidth - 1f, y - 1f, arrowWidth, AvTokens.RowHeight - 2f),
                                   onNext, AvTokens.FontBody, AvButtonStyle.Quiet);

            valueLabel = Label(parent, "", new Rect(x + arrowWidth + AvTokens.Space2, y, w - (arrowWidth + AvTokens.Space2) * 2f, AvTokens.RowHeight),
                               AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);

            if (!string.IsNullOrEmpty(tooltip))
            {
                prev.WithTooltip(tooltip);
                next.WithTooltip(tooltip);
            }

            return new[] { prev, next };
        }

        public static AvButton Pager(
            RectTransform parent, float y, string glyph, float panelWidth, Action onClick, string tooltip = null)
        {
            const float arrowWidth = 34f;
            float x = glyph == "<" ? AvTokens.Pad : panelWidth - AvTokens.Pad - arrowWidth;
            AvButton btn = Button(parent, glyph, new Rect(x, y, arrowWidth, AvTokens.RowHeight),
                                  onClick, AvTokens.FontBody, AvButtonStyle.Quiet);
            if (!string.IsNullOrEmpty(tooltip)) btn.WithTooltip(tooltip);
            return btn;
        }

        public static TMP_Text PagerLabel(RectTransform parent, float y, float panelWidth, float arrowWidth = 34f) =>
            Label(parent, "", new Rect(AvTokens.Pad + arrowWidth + AvTokens.Gap, y,
                                       panelWidth - AvTokens.Pad * 2f - (arrowWidth + AvTokens.Gap) * 2f, AvTokens.RowHeight),
                  AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);


        public static float Heading(RectTransform parent, float y, string text, float width)
        {
            TMP_Text label = Label(parent, text, new Rect(AvTokens.Pad, y, width - AvTokens.Pad * 2f, AvTokens.Space4),
                AvTheme.Friendly, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            float labelWidth = Mathf.Ceil(label.GetPreferredValues(text).x);
            float ruleX = AvTokens.Pad + labelWidth + AvTokens.Space2;
            float ruleWidth = width - AvTokens.Pad - ruleX;
            if (ruleWidth > 0f)
            {
                Rule(parent, new Rect(ruleX, y - AvTokens.Space2, ruleWidth, 1f), AvTheme.Frame);
            }
            return y - AvTokens.Space4 - AvTokens.Space1;
        }

        public struct ColumnDef
        {
            public string Text;
            public float X;
            public float Width;
            public bool RightAligned;

            public ColumnDef(string text, float x, float width, bool rightAligned = false)
            {
                Text = text;
                X = x;
                Width = width;
                RightAligned = rightAligned;
            }
        }

        public static float ColumnHeaders(RectTransform parent, float y, ColumnDef[] columns, float pad = AvTokens.Pad)
        {
            foreach (ColumnDef col in columns)
            {
                Label(parent, col.Text, new Rect(pad + col.X, y, col.Width, AvTokens.Space4),
                      AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal,
                      col.RightAligned ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            }
            return y - AvTokens.Space4;
        }

        public static Image ProgressBar(RectTransform parent, Rect area, float percent, Color fillCol)
        {
            Panel(parent, area, AvTheme.SurfaceInert);
            Outline(parent, area, AvTheme.Frame);

            var fillObject = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.SetParent(parent, false);
            Place(fillRect, new Rect(area.x + 1f, area.y - 1f, area.width - 2f, area.height - 2f));
            Image fill = fillObject.GetComponent<Image>();
            fill.color = fillCol;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = Mathf.Clamp01(percent);
            return fill;
        }

        public static void PipMeter(
            RectTransform parent, Rect area, int filled, int total, Color activeColor, Color emptyColor)
        {
            const float pipSize = 8f;
            const float gap = 3f;
            float startX = area.x;

            for (int i = 0; i < total; i++)
            {
                float x = startX + i * (pipSize + gap);
                var pipRect = new Rect(x, area.y, pipSize, pipSize);
                if (i < filled)
                {
                    Panel(parent, pipRect, activeColor);
                }
                else
                {
                    Panel(parent, pipRect, Color.clear);
                    Outline(parent, pipRect, emptyColor);
                }
            }
        }

        public static TMP_Text StatusStrip(RectTransform parent, Rect area, Color? railColor = null)
        {
            Color rail = railColor ?? AvTheme.RailReady;
            TacticalCard(parent, area, rail);

            TMP_Text label = Label(parent, "> READY",
                                   new Rect(area.x + AvTokens.Space3, area.y - 2f,
                                            area.width - AvTokens.Space4 - AvTokens.Space2, area.height - 4f),
                                   AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal,
                                   TextAlignmentOptions.MidlineLeft, wrap: true);
            return label;
        }

        public static TMP_InputField InputField(
            RectTransform parent, Rect area, int characterLimit,
            Action<string> onChanged, Action onFocus = null, Action onBlur = null,
            string tooltip = null, string placeholderText = "NAME")
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            Image bg = go.GetComponent<Image>();
            bg.color = AvTheme.Surface;
            bg.raycastTarget = true;
            Outline(rt, new Rect(0f, 0f, area.width, area.height), AvTheme.Frame);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(rt, worldPositionStays: false);
            Place(viewport, new Rect(AvTokens.Space2, 0f, area.width - AvTokens.Space2 * 2f, area.height));

            TMP_Text text = Label(viewport, "", new Rect(0f, 0f, area.width - AvTokens.Space2 * 2f, area.height),
                                  AvTheme.Friendly, AvTokens.FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
            text.raycastTarget = false;

            TMP_Text placeholder = Label(viewport, placeholderText, new Rect(0f, 0f, area.width - AvTokens.Space2 * 2f, area.height),
                                         AvTheme.Disabled, AvTokens.FontBody, FontStyles.Italic, TextAlignmentOptions.Left);
            placeholder.raycastTarget = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            AvInput.StripNavigation(field);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.richText = false;
            field.restoreOriginalTextOnEscape = true;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = AvTheme.Accent;
            field.selectionColor = new Color(AvTheme.Accent.r, AvTheme.Accent.g, AvTheme.Accent.b, 0.35f);

            if (onChanged != null) field.onEndEdit.AddListener(v => onChanged(v));
            if (onFocus != null) field.onSelect.AddListener(_ => onFocus());
            if (onBlur != null) field.onDeselect.AddListener(_ => onBlur());

            return field;
        }

        // ---------------------------------------------------------------- Popup Menu
        public class Popup
        {
            private readonly GameObject root;
            private readonly RectTransform listRect;
            private readonly Image listGround;
            private readonly List<PopupRow> rows = new List<PopupRow>();
            private static Popup open;

            public const int MaxRows = 7;
            private const int PagedRows = MaxRows - 1;
            private int page;

            public Popup(RectTransform pageRoot, float panelWidth)
            {
                root = new GameObject("AvPopup", typeof(RectTransform));
                var rt = root.GetComponent<RectTransform>();
                rt.SetParent(pageRoot, worldPositionStays: false);
                Stretch(rt);

                HitButton(rt, new Rect(0f, 0f, panelWidth, 4000f), Close);

                var listGo = new GameObject("PopupList", typeof(RectTransform), typeof(Image));
                listRect = listGo.GetComponent<RectTransform>();
                listRect.SetParent(rt, worldPositionStays: false);

                listGround = listGo.GetComponent<Image>();
                listGround.sprite = AvSprites.Panel;
                listGround.type = Image.Type.Sliced;
                listGround.color = Color.white;
                listGround.raycastTarget = true;

                root.SetActive(false);
            }

            public static void CloseAny() => open?.Close();

            public void Show(Rect area, IReadOnlyList<PopupEntry> entries, Action<int> onPick)
            {
                page = 0;
                Render(area, entries, onPick);
            }

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

                float height = Mathf.Max(AvTokens.RowPitch, AvTokens.RowPitch * used) + AvTokens.Space1 * 2f;
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
                        rows[i].Bind(new PopupEntry("MORE...", "page " + (page + 1) + " of " + pages), area.width, () =>
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
                root.transform.SetAsLastSibling();
            }

            public void Close()
            {
                if (root == null) return;
                root.SetActive(false);
                if (ReferenceEquals(open, this)) open = null;
            }
        }

        public readonly struct PopupEntry
        {
            public readonly string Text;
            public readonly string Detail;
            public readonly bool Selected;
            public readonly bool Enabled;

            public PopupEntry(string text, string detail = null, bool selected = false, bool enabled = true)
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
            private readonly AvButton hit;
            private readonly int index;

            public PopupRow(RectTransform parent, int index)
            {
                this.index = index;
                go = new GameObject("PopupRow" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);

                fill = go.GetComponent<Image>();
                fill.color = AvTheme.Ground;
                fill.raycastTarget = true;

                label = Label(rt, "", new Rect(AvTokens.Space2, 0f, 10f, AvTokens.RowHeight),
                              AvTheme.Friendly, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, "", new Rect(0f, 0f, 10f, AvTokens.RowHeight),
                               AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
                hit = HitButton(rt, new Rect(0f, 0f, 10f, AvTokens.RowHeight), null);
                go.SetActive(false);
            }

            public void Bind(PopupEntry entry, float width, Action onPick)
            {
                Place((RectTransform)go.transform, new Rect(0f, -(AvTokens.RowPitch * index) - AvTokens.Space1, width, AvTokens.RowHeight));

                float detailWidth = string.IsNullOrEmpty(entry.Detail) ? 0f : 96f;
                Place((RectTransform)label.transform, new Rect(AvTokens.Space2, 0f, width - AvTokens.Space2 * 2f - detailWidth, AvTokens.RowHeight));
                Place((RectTransform)detail.transform, new Rect(width - AvTokens.Space2 - detailWidth, 0f, detailWidth, AvTokens.RowHeight));
                Place((RectTransform)hit.transform, new Rect(0f, 0f, width, AvTokens.RowHeight));

                label.text = entry.Text ?? "";
                label.color = !entry.Enabled ? AvTheme.Disabled : entry.Selected ? AvTheme.Accent : AvTheme.Friendly;
                detail.text = entry.Detail ?? "";

                Color rest = entry.Selected ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha)) : AvTheme.Ground;
                Color hover = AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha));
                hit.SetRowHighlight(fill, rest, hover);
                hit.SetAction(entry.Enabled ? onPick : null);
                hit.SetEnabled(entry.Enabled);

                if (!go.activeSelf) go.SetActive(true);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }
    }
}
