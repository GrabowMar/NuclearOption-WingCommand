using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// The ordered instrument strip below the maximised map.
    ///
    /// <para>Vanilla draws the clock/speed/altitude/attitude group above the map and the
    /// airport or spectator controls on a separate lower canvas. The layout already reserves
    /// the bottom 120 pixels for those controls; this class turns that reserve into one real
    /// avionics panel and temporarily hosts the native objects in two centred rows.</para>
    ///
    /// <para>The native objects remain authoritative. Their scripts keep updating their text,
    /// active state and button actions; only their parent and RectTransform presentation are
    /// snapshotted. Minimise restores every value and sibling index exactly.</para>
    /// </summary>
    internal static class MfdMapFooter
    {
        private const string FooterName = "NOAvionics.MapFooter";
        private const string ChromeName = "Chrome";
        private const string InstrumentsSlotName = "InstrumentsSlot";
        private const string ContextSlotName = "ContextSlot";
        private const float FooterInset = 8f;
        private const float RowGap = 4f;

        private sealed class RectSnapshot
        {
            public RectTransform Target;
            public Transform Parent;
            public int SiblingIndex;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 SizeDelta;
            public Vector2 AnchoredPosition;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;

            public static RectSnapshot Capture(RectTransform target)
            {
                if (target == null) return null;
                return new RectSnapshot
                {
                    Target = target,
                    Parent = target.parent,
                    SiblingIndex = target.GetSiblingIndex(),
                    AnchorMin = target.anchorMin,
                    AnchorMax = target.anchorMax,
                    Pivot = target.pivot,
                    SizeDelta = target.sizeDelta,
                    AnchoredPosition = target.anchoredPosition,
                    LocalPosition = target.localPosition,
                    LocalRotation = target.localRotation,
                    LocalScale = target.localScale,
                };
            }

            public void Restore()
            {
                if (Target == null || Parent == null) return;

                Target.SetParent(Parent, worldPositionStays: false);
                Target.SetSiblingIndex(Mathf.Clamp(SiblingIndex, 0, Parent.childCount - 1));
                Target.anchorMin = AnchorMin;
                Target.anchorMax = AnchorMax;
                Target.pivot = Pivot;
                Target.sizeDelta = SizeDelta;
                Target.anchoredPosition = AnchoredPosition;
                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
                Target.localScale = LocalScale;
            }
        }

        private static RectTransform footer;
        private static RectTransform chrome;
        private static RectTransform instrumentsSlot;
        private static RectTransform contextSlot;
        private static Vector2 chromeSize;

        private static RectSnapshot instruments;
        private static RectSnapshot airbase;
        private static RectSnapshot spectator;

        public static void Ensure(Canvas canvas, MfdLayout.Columns columns, VirtualMFD mfd)
        {
            if (canvas == null || mfd == null || !GameAccess.MfdFooterAvailable) return;

            EnsureRoot(canvas);
            if (footer == null) return;

            Rect footerArea = ResolveArea(columns);
            if (footerArea.width <= 1f || footerArea.height <= 1f) return;

            PlaceFooter(footerArea);
            BuildChrome(footerArea.size);
            PlaceSlots(footerArea.size);
            AdoptNativeSurfaces(mfd);

            // This panel contains live native buttons, so it belongs above the passive deck,
            // map tray and map viewport. Its rectangle is entirely inside BottomReserve and
            // cannot intercept map gestures.
            footer.SetAsLastSibling();
        }

        public static void Restore()
        {
            // Restore children before destroying their temporary slots.
            spectator?.Restore();
            airbase?.Restore();
            instruments?.Restore();

            spectator = null;
            airbase = null;
            instruments = null;

            if (footer != null) Object.Destroy(footer.gameObject);
            footer = null;
            chrome = null;
            instrumentsSlot = null;
            contextSlot = null;
            chromeSize = Vector2.zero;
        }

        public static void Reset() => Restore();

        private static void EnsureRoot(Canvas canvas)
        {
            if (footer != null && footer.parent != canvas.transform)
            {
                Restore();
            }

            if (footer == null)
            {
                Transform existing = canvas.transform.Find(FooterName);
                footer = existing as RectTransform;
            }

            if (footer == null)
            {
                var go = new GameObject(FooterName, typeof(RectTransform), typeof(Image));
                footer = go.GetComponent<RectTransform>();
                footer.SetParent(canvas.transform, worldPositionStays: false);
            }

            Image background = footer.GetComponent<Image>();
            if (background == null) background = footer.gameObject.AddComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            chrome = FindOrCreateLayer(footer, ChromeName);
            instrumentsSlot = FindOrCreateLayer(footer, InstrumentsSlotName);
            contextSlot = FindOrCreateLayer(footer, ContextSlotName);
        }

        private static Rect ResolveArea(MfdLayout.Columns columns)
        {
            float top = columns.Map.y - columns.Map.height - MfdLayout.Gutter;
            float bottom = -columns.Canvas.y * 0.5f + MfdLayout.Margin;
            return new Rect(columns.Map.x, top, columns.Map.width, Mathf.Max(0f, top - bottom));
        }

        private static void PlaceFooter(Rect area)
        {
            footer.anchorMin = footer.anchorMax = new Vector2(0.5f, 0.5f);
            footer.pivot = new Vector2(0f, 1f);
            footer.sizeDelta = area.size;
            footer.anchoredPosition = area.position;
            footer.localScale = Vector3.one;
        }

        private static void BuildChrome(Vector2 size)
        {
            if (Approximately(chromeSize, size) && chrome.childCount > 0) return;

            for (int i = chrome.childCount - 1; i >= 0; i--)
                Object.Destroy(chrome.GetChild(i).gameObject);

            chromeSize = size;
            var area = new Rect(0f, 0f, size.x, size.y);
            AvKit.Outline(chrome, area, AvTheme.Hairline);
            AvKit.CornerTicks(chrome, area, AvTheme.Hairline, 8f);

            float innerHeight = Mathf.Max(0f, size.y - FooterInset * 2f);
            float contextHeight = Mathf.Min(48f, innerHeight * 0.52f);
            float dividerY = -(FooterInset + Mathf.Max(0f, innerHeight - contextHeight) + RowGap * 0.5f);
            AvKit.Rule(chrome,
                new Rect(FooterInset, dividerY, Mathf.Max(0f, size.x - FooterInset * 2f), 1f),
                AvTheme.Frame.WithAlpha(0.55f));
        }

        private static void PlaceSlots(Vector2 size)
        {
            float innerWidth = Mathf.Max(0f, size.x - FooterInset * 2f);
            float innerHeight = Mathf.Max(0f, size.y - FooterInset * 2f);
            float contextHeight = Mathf.Min(48f, innerHeight * 0.52f);
            float instrumentsHeight = Mathf.Max(0f, innerHeight - contextHeight - RowGap);

            AvKit.Place(instrumentsSlot,
                new Rect(FooterInset, -FooterInset, innerWidth, instrumentsHeight));
            AvKit.Place(contextSlot,
                new Rect(FooterInset, -(FooterInset + instrumentsHeight + RowGap), innerWidth, contextHeight));
        }

        private static void AdoptNativeSurfaces(VirtualMFD mfd)
        {
            RectTransform top = GameAccess.GetMfdTopInstruments(mfd);
            GameplayUI gameplay = Object.FindObjectOfType<GameplayUI>();
            RectTransform airbasePanel = GameAccess.GetSelectAirbasePanel(gameplay)?.transform as RectTransform;
            RectTransform spectatorPanel = GameAccess.GetSpectatorPanel(gameplay)?.transform as RectTransform;

            Adopt(ref instruments, top, instrumentsSlot, new Vector2(1000f, 60f));
            Adopt(ref airbase, airbasePanel, contextSlot, null);
            Adopt(ref spectator, spectatorPanel, contextSlot, null);
        }

        private static void Adopt(
            ref RectSnapshot snapshot,
            RectTransform target,
            RectTransform slot,
            Vector2? hostedSize)
        {
            if (target == null || slot == null) return;

            if (snapshot == null || snapshot.Target != target)
                snapshot = RectSnapshot.Capture(target);

            if (target.parent != slot) target.SetParent(slot, worldPositionStays: false);
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = Vector2.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;

            if (hostedSize.HasValue)
            {
                Vector2 requested = hostedSize.Value;
                target.sizeDelta = new Vector2(
                    Mathf.Min(requested.x, Mathf.Max(0f, slot.rect.width)),
                    requested.y);
            }
        }

        private static RectTransform FindOrCreateLayer(RectTransform parent, string name)
        {
            RectTransform layer = parent.Find(name) as RectTransform;
            if (layer == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                layer = go.GetComponent<RectTransform>();
                layer.SetParent(parent, worldPositionStays: false);
            }

            AvKit.Stretch(layer);
            return layer;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
    }
}
