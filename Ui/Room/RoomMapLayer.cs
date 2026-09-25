using System.Collections.Generic;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Pooled map symbols for the room's TACTICAL map, drawn through a <see cref="MapView"/>: rings under lines under
    /// dots under labels, each kind in its own layer. <see cref="Begin"/>, draw, <see cref="End"/> hides what a draw did not
    /// use; nothing outside the view (plus a margin) is placed.</summary>
    internal sealed class RoomMapLayer
    {
        private const float Margin = 24f;
        private readonly MapView view;
        private readonly RectTransform rings, lines, dots, labels;
        private readonly List<Image> ringPool = new List<Image>(), linePool = new List<Image>(), dotPool = new List<Image>();
        private readonly List<TMP_Text> labelPool = new List<TMP_Text>();
        private int ring, line, dot, label;

        public RoomMapLayer(RectTransform parent, MapView view)
        {
            this.view = view;
            rings = Layer(parent, "Rings");
            lines = Layer(parent, "Lines");
            dots = Layer(parent, "Dots");
            labels = Layer(parent, "Labels");
        }

        public int Count => dot;

        private static RectTransform Layer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Stretch(rt);
            return rt;
        }

        public void Begin() => ring = line = dot = label = 0;

        private bool Visible(float px, float py, float pad) =>
            px > -view.Width * 0.5f - pad && px < view.Width * 0.5f + pad && py > -view.Height * 0.5f - pad && py < view.Height * 0.5f + pad;

        /// <summary>A dot of <paramref name="size"/> px with an optional label; false (and nothing placed) off the view.</summary>
        public bool Dot(float x, float z, Color c, float size, string text)
        {
            view.Project(x, z, out float px, out float py);
            if (!Visible(px, py, Margin)) return false;
            Image img = Take(dotPool, dots, dot++, WmcMapOverlay.Disc());
            img.rectTransform.localPosition = new Vector3(px, py, 0f);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            if (img.color != c) img.color = c;
            if (text != null) Text(px + size * 0.5f + 3f, py + size * 0.5f, text, c);
            return true;
        }

        /// <summary>A ring of a fixed <paramref name="pixels"/> diameter (a selection halo).</summary>
        public void Halo(float x, float z, float pixels, Color c)
        {
            view.Project(x, z, out float px, out float py);
            if (!Visible(px, py, Margin)) return;
            Image img = Take(ringPool, rings, ring++, WmcMapOverlay.Ring());
            img.rectTransform.localPosition = new Vector3(px, py, 0f);
            img.rectTransform.sizeDelta = new Vector2(pixels, pixels);
            if (img.color != c) img.color = c;
        }

        /// <summary>A ring of <paramref name="radiusMetres"/> on the ground (an orbit).</summary>
        public void Ring(float x, float z, float radiusMetres, Color c)
        {
            float d = Mathf.Max(8f, 2f * radiusMetres / view.MetresPerPixel);
            view.Project(x, z, out float px, out float py);
            if (!Visible(px, py, d * 0.5f + Margin)) return;
            Image img = Take(ringPool, rings, ring++, WmcMapOverlay.Ring());
            img.rectTransform.localPosition = new Vector3(px, py, 0f);
            img.rectTransform.sizeDelta = new Vector2(d, d);
            if (img.color != c) img.color = c;
        }

        public void Line(float x0, float z0, float x1, float z1, Color c, float width)
        {
            view.Project(x0, z0, out float ax, out float ay);
            view.Project(x1, z1, out float bx, out float by);
            float hw = view.Width * 0.5f + Margin, hh = view.Height * 0.5f + Margin;
            if ((ax < -hw && bx < -hw) || (ax > hw && bx > hw) || (ay < -hh && by < -hh) || (ay > hh && by > hh)) return;
            Image img = Take(linePool, lines, line++, null);
            RectTransform rt = img.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localPosition = new Vector3(ax, ay, 0f);
            float dx = bx - ax, dy = by - ay;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
            rt.sizeDelta = new Vector2(Mathf.Sqrt(dx * dx + dy * dy), width);
            if (img.color != c) img.color = c;
        }

        /// <summary>A label at a world point (a route point's distance and ETA).</summary>
        public void Label(float x, float z, string text, Color c)
        {
            view.Project(x, z, out float px, out float py);
            if (Visible(px, py, Margin)) Text(px + 7f, py + 3f, text, c);
        }

        private void Text(float px, float py, string text, Color c)
        {
            while (labelPool.Count <= label)
            {
                TMP_Text t = AvStyled.Label(labels, new Rect(0f, 0f, 220f, 16f), "", "row-sub");
                RectTransform r = t.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = Vector2.zero;
                t.raycastTarget = false;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                t.gameObject.SetActive(false);
                labelPool.Add(t);
            }
            TMP_Text l = labelPool[label++];
            l.rectTransform.localPosition = new Vector3(px, py, 0f);
            if (!ReferenceEquals(l.text, text)) l.text = text;
            if (l.color != c) l.color = c;
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        }

        private static Image Take(List<Image> pool, RectTransform parent, int i, Sprite sprite)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject("Symbol", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                Image img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.preserveAspect = sprite != null;
                go.SetActive(false);
                pool.Add(img);
            }
            Image result = pool[i];
            if (!result.gameObject.activeSelf) result.gameObject.SetActive(true);
            return result;
        }

        /// <summary>Hides every symbol this draw did not use (review focus 5).</summary>
        public void End()
        {
            Hide(ringPool, ring);
            Hide(linePool, line);
            Hide(dotPool, dot);
            for (int i = label; i < labelPool.Count; i++)
                if (labelPool[i].gameObject.activeSelf) labelPool[i].gameObject.SetActive(false);
        }

        private static void Hide(List<Image> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }
    }
}
