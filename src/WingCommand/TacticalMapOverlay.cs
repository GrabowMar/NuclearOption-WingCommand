using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Small persistent markers for map-positioned wing directives.</summary>
    internal static class TacticalMapOverlay
    {
        private sealed class Group
        {
            public WingOrder Order;
            public GlobalPosition Point;
            public int Count;
        }

        private sealed class Marker
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Icon;
            public TMP_Text Label;
        }

        private static readonly List<Group> groups = new List<Group>();
        private static readonly List<Marker> markers = new List<Marker>();
        private static float nextRefresh;

        public static void Tick(WingRegistry wing)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized)
            {
                SetVisible(false);
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                Collect(wing);
                Sync(map);
            }

            Position(map);
            SetVisible(true);
        }

        public static void Reset()
        {
            foreach (Marker marker in markers)
            {
                if (marker.Root != null) Object.Destroy(marker.Root);
            }
            markers.Clear();
            groups.Clear();
            nextRefresh = 0f;
        }

        /// <summary>Request an immediate collection after a directive changes.</summary>
        public static void Invalidate() => nextRefresh = 0f;

        private static void Collect(WingRegistry wing)
        {
            groups.Clear();
            if (wing == null) return;

            foreach (WingMember member in wing.Members)
            {
                WingDirective directive = member.Directive;
                if (!member.Alive || !directive.HasPoint || !IsMarkerOrder(directive.Order))
                    continue;

                Group found = null;
                foreach (Group group in groups)
                {
                    if (group.Order != directive.Order) continue;
                    Vector3 delta = group.Point - directive.Point;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= 2500f) { found = group; break; }
                }

                if (found == null)
                {
                    found = new Group { Order = directive.Order, Point = directive.Point };
                    groups.Add(found);
                }
                found.Count++;
            }
        }

        private static void Sync(DynamicMap map)
        {
            while (markers.Count < groups.Count) markers.Add(Create(map));
            for (int i = 0; i < markers.Count; i++)
            {
                bool active = i < groups.Count;
                markers[i].Root.SetActive(active);
                if (!active) continue;

                Group group = groups[i];
                markers[i].Icon.sprite = IconFactory.Get(
                    group.Order == WingOrder.LandHere ? "land" :
                    group.Order == WingOrder.MoveToPoint ? "move" : "orbit");
                markers[i].Label.text = WingOrderCatalog.Label(group.Order).ToUpperInvariant() +
                                        (group.Count > 1 ? " · " + group.Count : "");
            }
        }

        private static bool IsMarkerOrder(WingOrder order) =>
            order == WingOrder.OrbitHere || order == WingOrder.LandHere ||
            order == WingOrder.MoveToPoint;

        private static Marker Create(DynamicMap map)
        {
            var root = new GameObject("WingCommand_OrderMarker", typeof(RectTransform), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(map.iconLayer.transform, worldPositionStays: false);
            rect.sizeDelta = new Vector2(24f, 24f);

            Image icon = root.GetComponent<Image>();
            icon.color = WingMarkers.MemberColor;
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, worldPositionStays: false);
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 5f);
            labelRect.sizeDelta = new Vector2(130f, 20f);

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.fontSize = 11f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = WingMarkers.MemberColor;
            label.raycastTarget = false;

            return new Marker { Root = root, Rect = rect, Icon = icon, Label = label };
        }

        private static void Position(DynamicMap map)
        {
            float inverseScale = 1f / Mathf.Max(0.01f, map.mapImage.transform.localScale.x);
            for (int i = 0; i < groups.Count && i < markers.Count; i++)
            {
                Vector3 point = groups[i].Point.AsVector3() * map.mapDisplayFactor;
                markers[i].Rect.localPosition = new Vector3(point.x, point.z, 0f);
                markers[i].Rect.localScale = Vector3.one * inverseScale;
            }
        }

        private static void SetVisible(bool visible)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                Marker marker = markers[i];
                bool active = visible && i < groups.Count;
                if (marker.Root != null && marker.Root.activeSelf != active)
                    marker.Root.SetActive(active);
            }
        }
    }
}
