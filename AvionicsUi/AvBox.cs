using System;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// The Unity half of the layout engine: real text measurement, conversion into the
    /// <c>Rect</c>s <see cref="AvKit"/> already consumes, and a debug overlay.
    ///
    /// The arithmetic lives in <see cref="AvNode"/>, which is compiled into the test
    /// projects and has no game install to reference. This file is the only part that
    /// needs TextMeshPro, and it exists so that a row can be as tall as the sentence
    /// inside it.
    /// </summary>
    public static class AvBox
    {
        /// <summary>A scratch label used only for measurement; never parented, never drawn.</summary>
        private static TMP_Text ruler;

        // ------------------------------------------------------------------ builders

        public static AvNode Column(string name) => AvLayout.Column(name);
        public static AvNode Row(string name) => AvLayout.Row(name);
        public static AvNode Stack(string name) => AvLayout.Stack(name);
        public static AvNode Cell(string name) => AvLayout.Cell(name);
        public static AvNode Grid(string name, int columns) => AvLayout.Grid(name, columns);
        public static AvNode Spacer(float px) => AvLayout.Spacer(px);
        public static AvNode Filler() => AvLayout.Filler();

        /// <summary>
        /// A leaf that carries a string, so <see cref="AvSize.Auto"/> can measure it.
        ///
        /// The class set decides the font, so the measured height matches what the label
        /// will actually be drawn at — the two used to be independent guesses.
        /// </summary>
        public static AvNode Text(string name, string content, string classes, bool wrap = true)
        {
            AvNode node = AvLayout.Cell(name).Class(classes);
            node.Tag = new TextContent { Value = content ?? "", Wrap = wrap };
            return node;
        }

        private sealed class TextContent
        {
            public string Value;
            public bool Wrap;
        }

        // ---------------------------------------------------------------- measurement

        /// <summary>
        /// Measure a node against the space it will get.
        ///
        /// Two questions share one delegate, distinguished by the sign of
        /// <c>available</c>: a positive value asks "how tall, wrapped to this width",
        /// zero or less asks "how wide, unconstrained". That is the shape
        /// <see cref="AvNode"/> needs, and collapsing them keeps one callback rather than
        /// two parallel trees.
        /// </summary>
        public static float Measure(AvNode node, float available)
        {
            var content = node.Tag as TextContent;
            if (content == null || string.IsNullOrEmpty(content.Value)) return node.Content;

            AvStyle style = AvStyleHost.Style(node.Classes);
            float size = style.HasFont ? style.FontSize : AvTokens.FontBody;

            TMP_Text r = Ruler();
            if (r == null)
            {
                // No text engine yet (the game's UI has not initialised). Fall back to a
                // line-count estimate rather than collapsing the row to nothing.
                float lines = available > 0f && content.Wrap
                    ? Mathf.Max(1f, Mathf.Ceil(content.Value.Length * size * 0.5f / Mathf.Max(1f, available)))
                    : 1f;
                return available > 0f ? lines * (size * 1.35f) : content.Value.Length * size * 0.5f;
            }

            r.fontSize = size;
            r.fontStyle = style.Bold ? FontStyles.Bold : FontStyles.Normal;
            r.characterSpacing = style.Tracking;
            r.enableWordWrapping = content.Wrap && available > 0f;

            Vector2 preferred = r.GetPreferredValues(
                content.Value,
                available > 0f ? available : 100000f,
                0f);

            return available > 0f ? Mathf.Ceil(preferred.y) : Mathf.Ceil(preferred.x);
        }

        private static TMP_Text Ruler()
        {
            if (ruler != null) return ruler;

            TMP_FontAsset font = AvFont.Font;
            if (font == null) return null;

            var go = new GameObject("AvBoxRuler", typeof(RectTransform), typeof(TextMeshProUGUI))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.SetActive(false);

            ruler = go.GetComponent<TextMeshProUGUI>();
            ruler.font = font;
            return ruler;
        }

        /// <summary>Drop the measuring label when a mission ends; the next scene makes its own.</summary>
        public static void Reset()
        {
            if (ruler != null)
            {
                UnityEngine.Object.Destroy(ruler.gameObject);
                ruler = null;
            }
        }

        // ------------------------------------------------------------------ arranging

        /// <summary>Resolve every rectangle in the tree inside a panel-space area.</summary>
        public static AvNode Arrange(this AvNode root, Rect area) =>
            root.Arrange(ToAv(area), Measure);

        /// <summary>Resolve every rectangle in the tree inside a sized RectTransform.</summary>
        public static AvNode ArrangeIn(this AvNode root, RectTransform target)
        {
            Vector2 size = target.rect.size;
            return root.Arrange(new AvRect(0f, 0f, size.x, size.y), Measure);
        }

        public static Rect ToUnity(this AvRect r) => new Rect(r.X, r.Y, r.Width, r.Height);

        public static AvRect ToAv(Rect r) => new AvRect(r.x, r.y, r.width, r.height);

        /// <summary>The arranged rectangle of a descendant, ready for <see cref="AvKit.Place"/>.</summary>
        public static Rect At(this AvNode root, string path) => root[path].ToUnity();

        /// <summary>The text a <see cref="Text"/> node was built with, for the draw pass.</summary>
        public static string TextOf(this AvNode node)
        {
            var content = node.Tag as TextContent;
            return content == null ? null : content.Value;
        }

        public static bool WrapsText(this AvNode node)
        {
            var content = node.Tag as TextContent;
            return content != null && content.Wrap;
        }

        // ---------------------------------------------------------------------- debug

        /// <summary>
        /// Outline every computed box, so a layout can be inspected in game rather than
        /// inferred from the source. Gated by the caller; never on in a normal session.
        /// </summary>
        public static void DrawDebug(RectTransform parent, AvNode root)
        {
            var tint = new[]
            {
                new Color(1f, 0f, 1f, 0.55f),
                new Color(0f, 1f, 1f, 0.45f),
                new Color(1f, 1f, 0f, 0.40f),
                new Color(1f, 0.5f, 0f, 0.35f),
            };

            root.Walk((node, depth) =>
            {
                if (node.Rect.Width <= 0f || node.Rect.Height <= 0f) return;
                AvKit.Outline(parent, node.Rect.ToUnity(), tint[depth % tint.Length]);
            });
        }
    }
}
