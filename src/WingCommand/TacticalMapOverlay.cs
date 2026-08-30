using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// What the wing's orders look like on the maximised map: a marker at each commanded
    /// point, and a line from every wingman to the point it is flying to.
    ///
    /// The markers alone said where the orders were but never who had them, which with more
    /// than one wingman tasked is the question actually being asked of the map. The lines
    /// answer it, and carry the queue: a Shift-clicked route is drawn as the chain it is.
    /// </summary>
    internal static class TacticalMapOverlay
    {
        /// <summary>Line thickness on screen, in pixels, held constant against map zoom.</summary>
        private const float LineThickness = 1.6f;

        /// <summary>Alpha of a leg that is not the one currently being flown.</summary>
        private const float QueuedAlpha = 0.45f;

        /// <summary>Radius of the dot marking a queued point, in screen pixels.</summary>
        private const float NodeRadius = 3f;

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

        /// <summary>One drawn leg: two map points, a colour, and whether it is the live one.</summary>
        private struct Leg
        {
            public GlobalPosition From;
            public GlobalPosition To;
            public Color Color;
            public bool Node;
        }

        private static readonly List<Group> groups = new List<Group>();
        private static readonly List<Marker> markers = new List<Marker>();
        private static readonly List<Leg> legs = new List<Leg>();
        private static readonly List<Image> lines = new List<Image>();
        private static readonly List<Image> nodes = new List<Image>();
        private static Sprite nodeSprite;
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

            // Legs are rebuilt every frame, not on the refresh timer: one end of each is an
            // aircraft, and a line lagging a fifth of a second behind its own wingman reads
            // as a bug rather than as a route.
            CollectLegs(wing);
            SyncLines(map);

            Position(map);
            SetVisible(true);
        }

        public static void Reset()
        {
            foreach (Marker marker in markers)
            {
                if (marker.Root != null) Object.Destroy(marker.Root);
            }
            foreach (Image line in lines)
            {
                if (line != null) Object.Destroy(line.gameObject);
            }
            foreach (Image node in nodes)
            {
                if (node != null) Object.Destroy(node.gameObject);
            }
            markers.Clear();
            lines.Clear();
            nodes.Clear();
            groups.Clear();
            legs.Clear();
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

        /// <summary>
        /// Build the polyline for every tasked wingman: aircraft to its current point, then
        /// on through whatever remains of its route.
        /// </summary>
        private static void CollectLegs(WingRegistry wing)
        {
            legs.Clear();
            if (wing == null || !Plugin.Config2.MapCommandEnabled.Value) return;

            WingCommandManager manager = WingCommandManager.Instance;

            foreach (WingMember member in wing.Members)
            {
                Aircraft aircraft = member.Aircraft;
                if (!member.Alive || aircraft == null) continue;

                bool selected = manager?.Selection.Contains(member) ?? true;
                Color color = WingMarkers.ColorFor(WingMarkers.Role.Member, selected);
                GlobalPosition from = aircraft.GlobalPosition();

                // A route is drawn as a chain, so a Shift-queued sequence reads as an order
                // of march rather than as several unrelated destinations.
                if (member.Order == WingOrder.MoveToPoint && member.WaypointCount > 0)
                {
                    IReadOnlyList<GlobalPosition> route = member.Route;
                    for (int i = 0; i < route.Count; i++)
                    {
                        legs.Add(new Leg
                        {
                            From = from,
                            To = route[i],
                            // Only the leg being flown is at full strength; the rest of the
                            // queue is visibly pending.
                            Color = i == 0 ? color : color.WithAlpha(color.a * QueuedAlpha),
                            Node = i < route.Count - 1,
                        });
                        from = route[i];
                    }
                    continue;
                }

                WingDirective directive = member.Directive;
                if (directive.HasPoint && IsMarkerOrder(directive.Order))
                {
                    legs.Add(new Leg { From = from, To = directive.Point, Color = color });
                    continue;
                }

                // An attack runs to a unit rather than to a point, and takes the amber the
                // rest of the wing's target symbology already uses.
                Unit target = member.AssignedTarget;
                if (target != null && !target.disabled)
                {
                    legs.Add(new Leg
                    {
                        From = from,
                        To = target.GlobalPosition(),
                        Color = WingMarkers.TargetColor.WithAlpha(QueuedAlpha + 0.25f),
                    });
                }
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

        private static void SyncLines(DynamicMap map)
        {
            int nodeCount = 0;
            for (int i = 0; i < legs.Count; i++)
            {
                if (legs[i].Node) nodeCount++;
            }

            while (lines.Count < legs.Count) lines.Add(CreateLine(map));
            while (nodes.Count < nodeCount) nodes.Add(CreateNode(map));
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

        /// <summary>
        /// A leg is one stretched quad pivoted at its start, so it can be positioned and
        /// rotated without any geometry of its own.
        /// </summary>
        private static Image CreateLine(DynamicMap map)
        {
            var go = new GameObject("WingCommand_OrderLine", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(map.iconLayer.transform, worldPositionStays: false);

            // Pivot at the left edge, centred vertically: the rect then runs from the
            // aircraft along its own local +x, and rotating it about z aims it at the point.
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.localScale = Vector3.one;

            // Behind the icons and markers, which are created into the same layer.
            rect.SetAsFirstSibling();

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateNode(DynamicMap map)
        {
            var go = new GameObject("WingCommand_RoutePoint", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(map.iconLayer.transform, worldPositionStays: false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = NodeSprite();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>A small filled disc for a queued route point, drawn once.</summary>
        private static Sprite NodeSprite()
        {
            if (nodeSprite != null) return nodeSprite;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_RoutePoint",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = size * 0.5f;
            float radius = centre - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - centre) * (x + 0.5f - centre) +
                                         (y + 0.5f - centre) * (y + 0.5f - centre));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f)));
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            nodeSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                                       new Vector2(0.5f, 0.5f), 100f);
            nodeSprite.name = "WingCommand_RoutePointSprite";
            nodeSprite.hideFlags = HideFlags.HideAndDontSave;
            return nodeSprite;
        }

        /// <summary>Map-space position of a world point, in the icon layer's own coordinates.</summary>
        private static Vector3 ToMap(GlobalPosition point, float displayFactor)
        {
            Vector3 p = point.AsVector3() * displayFactor;
            return new Vector3(p.x, p.z, 0f);
        }

        private static void Position(DynamicMap map)
        {
            float inverseScale = 1f / Mathf.Max(0.01f, map.mapImage.transform.localScale.x);
            float displayFactor = map.mapDisplayFactor;

            for (int i = 0; i < groups.Count && i < markers.Count; i++)
            {
                markers[i].Rect.localPosition = ToMap(groups[i].Point, displayFactor);
                markers[i].Rect.localScale = Vector3.one * inverseScale;
            }

            // A leg's length is a distance on the map and scales with it; its width is a
            // screen quantity and must not. Only the thickness takes the inverse scale, so
            // the line stays a hairline at every zoom instead of thickening into a slab.
            int node = 0;
            for (int i = 0; i < legs.Count && i < lines.Count; i++)
            {
                Leg leg = legs[i];
                Vector3 from = ToMap(leg.From, displayFactor);
                Vector3 to = ToMap(leg.To, displayFactor);
                Vector3 delta = to - from;

                RectTransform rect = lines[i].rectTransform;
                rect.localPosition = from;
                rect.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                rect.sizeDelta = new Vector2(delta.magnitude, LineThickness * inverseScale);
                lines[i].color = leg.Color;

                if (!leg.Node || node >= nodes.Count) continue;

                RectTransform nodeRect = nodes[node].rectTransform;
                nodeRect.localPosition = to;
                nodeRect.sizeDelta = Vector2.one * (NodeRadius * 2f * inverseScale);
                nodes[node].color = leg.Color;
                node++;
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

            for (int i = 0; i < lines.Count; i++)
            {
                bool active = visible && i < legs.Count;
                if (lines[i] != null && lines[i].gameObject.activeSelf != active)
                    lines[i].gameObject.SetActive(active);
            }

            int nodeCount = 0;
            if (visible)
            {
                for (int i = 0; i < legs.Count; i++)
                {
                    if (legs[i].Node) nodeCount++;
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                bool active = i < nodeCount;
                if (nodes[i] != null && nodes[i].gameObject.activeSelf != active)
                    nodes[i].gameObject.SetActive(active);
            }
        }
    }
}
