using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>The common flight-deck shell for full-screen operations rooms.</summary>
    internal static class AvRoomFrame
    {
        internal sealed class NotchChrome
        {
            internal Image Fill, Left, Top, Right, ActiveBar, Icon;
            internal TMP_Text Label, Key;
        }

        internal const float SideMargin = 48f;
        internal const float TopMargin = 72f;
        internal const float BottomMargin = 40f;
        internal const float NotchHeight = 32f;
        internal const float NotchInset = 24f;

        internal static Rect WindowRect(float canvasWidth, float canvasHeight)
        {
            if (float.IsNaN(canvasWidth) || float.IsInfinity(canvasWidth) || canvasWidth <= 0f ||
                float.IsNaN(canvasHeight) || float.IsInfinity(canvasHeight) || canvasHeight <= 0f)
                return new Rect(48f, 72f, 1824f, 968f);

            float width = Mathf.Min(canvasWidth, Mathf.Clamp(canvasWidth - SideMargin * 2f, 1280f, 1840f));
            float height = Mathf.Min(canvasHeight,
                Mathf.Clamp(canvasHeight - TopMargin - BottomMargin, 720f, 968f));
            float x = (canvasWidth - width) * 0.5f;
            float available = canvasHeight - TopMargin - BottomMargin;
            float y = available >= height
                ? TopMargin + (available - height) * 0.5f
                : (canvasHeight - height) * 0.5f;
            return new Rect(x, y, width, height);
        }

        internal static Image CreateBackdrop(RectTransform parent, float alpha)
        {
            Image image = AvKit.Panel(parent, new Rect(0f, 0f, 10f, 10f), AvTheme.Ground.WithAlpha(alpha));
            AvKit.Stretch(image.rectTransform);
            image.raycastTarget = true;
            return image;
        }

        internal static RectTransform CreateFrame(RectTransform parent, string name, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var frame = (RectTransform)go.transform;
            frame.SetParent(parent, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            group = go.GetComponent<CanvasGroup>();
            return frame;
        }

        internal static Image CreateEdge(RectTransform frame, Rect area, Color color) =>
            AvKit.Rule(frame, area, color);

        /// <summary>Shared visual notch; the owning room supplies its own click and input behavior.</summary>
        internal static NotchChrome CreateNotchChrome(RectTransform host, string label, string key,
            Sprite plate = null, Sprite glyph = null)
        {
            var notch = new NotchChrome();
            notch.Fill = AvKit.Panel(host, new Rect(0f, 0f, 10f, 10f), AvTheme.SurfaceInert,
                plate != null ? plate : AvSprites.Panel);
            AvKit.Stretch(notch.Fill.rectTransform);
            notch.Fill.raycastTarget = false;
            notch.Left = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.Top = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.Right = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.ActiveBar = CreateEdge(host, new Rect(0f, 0f, 10f, 2f), AvTheme.RailInfo);
            if (glyph != null)
            {
                notch.Icon = AvKit.Panel(host, new Rect(10f, -7f, 18f, 18f), AvTheme.RailInfo, glyph);
                notch.Icon.raycastTarget = false;
            }
            notch.Label = AvKit.Label(host, label, new Rect(glyph != null ? 34f : 12f, -2f, 10f,
                NotchHeight - 2f), AvTheme.Dim, AvTokens.FontLead, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);
            notch.Label.characterSpacing = 1.2f;
            notch.Key = AvKit.Label(host, key, new Rect(0f, -2f, 10f, NotchHeight - 2f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            return notch;
        }

        internal static void LayoutNotch(NotchChrome notch, float width, float cut = 7f)
        {
            float height = NotchHeight;
            AvKit.Place(notch.Left.rectTransform, new Rect(0f, -cut, 1f, height - cut));
            AvKit.Place(notch.Top.rectTransform, new Rect(cut, 0f, width - cut * 2f, 1f));
            AvKit.Place(notch.Right.rectTransform, new Rect(width - 1f, -cut, 1f, height - cut));
            AvKit.Place(notch.ActiveBar.rectTransform, new Rect(4f, -height + 2f, width - 8f, 2f));
            if (notch.Icon != null)
                AvKit.Place(notch.Icon.rectTransform, new Rect(10f, -(height - 18f) * 0.5f, 18f, 18f));
            AvKit.Place(notch.Label.rectTransform, new Rect(notch.Icon != null ? 34f : 12f, -1f,
                width - (notch.Icon != null ? 46f : 24f), height - 1f));
            AvKit.Place(notch.Key.rectTransform, new Rect(12f, -1f, width - 22f, height - 1f));
        }
    }
}
