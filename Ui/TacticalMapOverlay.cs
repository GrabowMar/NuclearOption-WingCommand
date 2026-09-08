using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Draws commanded point markers and per-member route chains on the maximised map, showing
    /// both destinations and assigned aircraft.</summary>
    internal static class TacticalMapOverlay
    {
        /// <summary>Screen-space route line width in pixels, independent of zoom.</summary>
        private const float LineThickness = 1.6f;

        /// <summary>Opacity for queued, inactive route legs.</summary>
        private const float QueuedAlpha = 0.45f;

        /// <summary>Queued-point dot radius in screen pixels.</summary>
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

        /// <summary>Rendered leg endpoints, colour, and active status.</summary>
        private struct Leg
        {
            public GlobalPosition From;
            public GlobalPosition To;
            public Color Color;
            public bool Node;
        }

        private struct RunwayInfo
        {
            public GlobalPosition Start;
            public GlobalPosition End;
            public Vector3 ApproachDir;
        }

        private static readonly List<Group> groups = new List<Group>();

        /// <summary>Cache native landing destinations on the marker timer to limit reflection; per-frame
        /// route drawing reuses the cache.</summary>
        private static readonly Dictionary<WingMember, GlobalPosition> rtbDestinations =
            new Dictionary<WingMember, GlobalPosition>();
        private static readonly Dictionary<WingMember, RunwayInfo> rtbRunways =
            new Dictionary<WingMember, RunwayInfo>();
        private struct LineState
        {
            public Vector3 From;
            public Vector3 To;
            public float Thickness;
        }

        private struct NodeState
        {
            public Vector3 Position;
            public float Size;
        }

        private static readonly List<Marker> markers = new List<Marker>();
        private static readonly List<Leg> legs = new List<Leg>();
        private static readonly List<Image> lines = new List<Image>();
        private static readonly List<Image> nodes = new List<Image>();
        private static readonly List<LineState> lineStates = new List<LineState>();
        private static readonly List<NodeState> nodeStates = new List<NodeState>();
        private static float lastInverseScale = -1f;
        private static float lastDisplayFactor = -1f;
        private static bool markersDirty = true;
        private static Sprite nodeSprite;
        private static float nextRefresh;

        public static void Tick(WingRegistry wing)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (!Plugin.Settings.MapCommandEnabled.Value || map == null || !DynamicMap.mapMaximized)
            {
                SetVisible(false);
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
                Collect(wing);
                Sync(map);
            }

            // Rebuild legs each frame so their aircraft endpoints do not lag moving wingmen.
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
            lineStates.Clear();
            nodeStates.Clear();
            groups.Clear();
            legs.Clear();
            rtbDestinations.Clear();
            rtbRunways.Clear();
            lastInverseScale = -1f;
            lastDisplayFactor = -1f;
            markersDirty = true;
            nextRefresh = 0f;
        }

        /// <summary>Refresh marker collection immediately after directive changes.</summary>
        public static void Invalidate()
        {
            markersDirty = true;
            nextRefresh = 0f;
        }

        private static void Collect(WingRegistry wing)
        {
            groups.Clear();
            rtbDestinations.Clear();
            rtbRunways.Clear();
            if (wing == null) return;

            foreach (WingMember member in wing.Members)
            {
                if (!member.Alive) continue;

                // Read RTB destination from the native landing state, which chooses the base instead of
                // carrying a directive point.
                if (member.Order == WingOrder.ReturnToBase)
                {
                    if (GameAccess.TryGetLandingDestination(member.Pilot,
                                                            out GlobalPosition home))
                    {
                        rtbDestinations[member] = home;
                        Add(WingOrder.ReturnToBase, home);
                    }

                    if (GameAccess.TryGetLandingRunway(member.Pilot, out GlobalPosition rwStart,
                                                       out GlobalPosition rwEnd, out Vector3 approachDir))
                    {
                        rtbRunways[member] = new RunwayInfo
                        {
                            Start = rwStart,
                            End = rwEnd,
                            ApproachDir = approachDir,
                        };
                    }
                    continue;
                }

                IReadOnlyList<WingDirective> route = member.Route;
                if (route.Count > 0)
                {
                    for (int i = 0; i < route.Count; i++)
                    {
                        WingDirective task = route[i];
                        if (task.HasPoint && IsMarkerOrder(task.Order))
                            Add(task.Order, task.Point);
                    }
                    continue;
                }

                WingDirective directive = member.Directive;
                if (!directive.HasPoint || !IsMarkerOrder(directive.Order)) continue;

                Add(directive.Order, directive.Point);
            }
        }

        /// <summary>Merge nearby commanded points into shared marker groups.</summary>
        private static void Add(WingOrder order, GlobalPosition point)
        {
            foreach (Group group in groups)
            {
                if (group.Order != order) continue;
                Vector3 delta = group.Point - point;
                delta.y = 0f;
                if (delta.sqrMagnitude <= 2500f) { group.Count++; return; }
            }

            groups.Add(new Group { Order = order, Point = point, Count = 1 });
        }

        /// <summary>Build each member's route from aircraft to current destination and queued
        /// follow-ons.</summary>
        private static void CollectLegs(WingRegistry wing)
        {
            legs.Clear();
            if (wing == null || !Plugin.Settings.MapCommandEnabled.Value) return;

            WingCommandManager manager = WingCommandManager.Instance;

            foreach (WingMember member in wing.Members)
            {
                Aircraft aircraft = member.Aircraft;
                if (!member.Alive || aircraft == null) continue;

                bool selected = manager?.Selection.Contains(member) ?? true;
                Color color = WingMarkers.ColorFor(WingMarkers.Role.Member, selected);
                GlobalPosition from = aircraft.GlobalPosition();

                // Draw queued tasks as a connected route in execution order.
                IReadOnlyList<WingDirective> route = member.Route;
                if (route.Count > 0)
                {
                    for (int i = 0; i < route.Count; i++)
                    {
                        if (!TryRoutePoint(route[i], out GlobalPosition to)) continue;
                        legs.Add(new Leg
                        {
                            From = from,
                            To = to,
                            Color = i == 0 ? color : color.WithAlpha(color.a * QueuedAlpha),
                            Node = i < route.Count - 1,
                        });
                        from = to;
                    }
                    continue;
                }

                // Draw the actual native RTB destination with lower emphasis than active point tasking.
                if (member.Order == WingOrder.ReturnToBase &&
                    rtbDestinations.TryGetValue(member, out GlobalPosition home))
                {
                    legs.Add(new Leg
                    {
                        From = from,
                        To = home,
                        Color = color.WithAlpha(color.a * QueuedAlpha),
                    });

                    // Add runway and final-approach guidance when available.
                    if (rtbRunways.TryGetValue(member, out RunwayInfo rw))
                    {
                        // Runway centreline.
                        legs.Add(new Leg
                        {
                            From = rw.Start,
                            To = rw.End,
                            Color = new Color(0.3f, 0.95f, 1f, 0.85f),
                            Node = true,
                        });

                        // Extend final approach 3.5 km before the threshold.
                        GlobalPosition approachExt = rw.Start - rw.ApproachDir * 3500f;
                        legs.Add(new Leg
                        {
                            From = approachExt,
                            To = rw.Start,
                            Color = new Color(0.2f, 0.8f, 1f, 0.45f),
                            Node = true,
                        });
                    }
                    continue;
                }

                WingDirective directive = member.Directive;
                if (directive.HasPoint && IsMarkerOrder(directive.Order))
                {
                    legs.Add(new Leg { From = from, To = directive.Point, Color = color });
                    continue;
                }

                // Use amber target symbology for unit-directed attack legs.
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
            markersDirty = true;
            while (markers.Count < groups.Count) markers.Add(Create(map));
            for (int i = 0; i < markers.Count; i++)
            {
                bool active = i < groups.Count;
                markers[i].Root.SetActive(active);
                if (!active) continue;

                Group group = groups[i];
                markers[i].Icon.sprite = IconFactory.Get(
                    group.Order == WingOrder.LandHere ? "land" :
                    group.Order == WingOrder.MoveToPoint ? "move" :
                    group.Order == WingOrder.SeekAndDestroy ? "engage" :
                    group.Order == WingOrder.DeliverCargo ? "cargo" :
                    group.Order == WingOrder.ReturnToBase ? "rtb" : "orbit");
                string label = WingOrderCatalog.Label(group.Order).ToUpperInvariant();
                if (group.Order == WingOrder.MoveToPoint)
                {
                    WingCommandManager mgr = WingCommandManager.Instance;
                    label += " · " + Mathf.RoundToInt(
                        MapOrderPolicy.StepMoveAltitude(mgr != null ? mgr.MapMoveAltitude : 0f, 0,
                            WingRegistry.IsRotary(mgr?.Wing.Leader))) + "M";
                }
                markers[i].Label.text = label + (group.Count > 1 ? " · " + group.Count : "");
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
            order == WingOrder.MoveToPoint || order == WingOrder.SeekAndDestroy ||
            order == WingOrder.DeliverCargo || order == WingOrder.StandDown;

        private static bool TryRoutePoint(WingDirective task, out GlobalPosition point)
        {
            if (task.HasPoint)
            {
                point = task.Point;
                return true;
            }

            Unit target = task.Target;
            if (target != null && !target.disabled)
            {
                point = target.GlobalPosition();
                return true;
            }

            point = default;
            return false;
        }

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

        /// <summary>Render a leg as a stretched quad pivoted at its starting point.</summary>
        private static Image CreateLine(DynamicMap map)
        {
            var go = new GameObject("WingCommand_OrderLine", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(map.iconLayer.transform, worldPositionStays: false);

            // Anchor the quad at its left-centre so local X length and Z rotation place the leg.
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.localScale = Vector3.one;

            // Keep route lines behind sibling icons and markers.
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

        /// <summary>Cached filled-disc sprite for queued points.</summary>
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

        /// <summary>Convert a world point into icon-layer map coordinates.</summary>
        private static Vector3 ToMap(GlobalPosition point, float displayFactor)
        {
            Vector3 p = point.AsVector3() * displayFactor;
            return new Vector3(p.x, p.z, 0f);
        }

        private static void Position(DynamicMap map)
        {
            float inverseScale = 1f / Mathf.Max(0.01f, map.mapImage.transform.localScale.x);
            float displayFactor = map.mapDisplayFactor;

            bool zoomChanged = !Mathf.Approximately(inverseScale, lastInverseScale) ||
                               !Mathf.Approximately(displayFactor, lastDisplayFactor);

            if (markersDirty || zoomChanged)
            {
                for (int i = 0; i < groups.Count && i < markers.Count; i++)
                {
                    markers[i].Rect.localPosition = ToMap(groups[i].Point, displayFactor);
                    markers[i].Rect.localScale = Vector3.one * inverseScale;
                }
                markersDirty = false;
                lastInverseScale = inverseScale;
                lastDisplayFactor = displayFactor;
            }

            // Scale leg length with the map and width inversely so zoom preserves screen-pixel
            // thickness.
            int node = 0;
            float lineThickness = LineThickness * inverseScale;
            float nodeSize = NodeRadius * 2f * inverseScale;

            for (int i = 0; i < legs.Count && i < lines.Count; i++)
            {
                Leg leg = legs[i];
                Vector3 from = ToMap(leg.From, displayFactor);
                Vector3 to = ToMap(leg.To, displayFactor);
                Vector3 delta = to - from;

                while (lineStates.Count <= i) lineStates.Add(new LineState { Thickness = -1f });
                LineState prev = lineStates[i];

                if (prev.From != from || prev.To != to || !Mathf.Approximately(prev.Thickness, lineThickness))
                {
                    lineStates[i] = new LineState { From = from, To = to, Thickness = lineThickness };
                    RectTransform rect = lines[i].rectTransform;
                    rect.localPosition = from;
                    rect.localRotation = Quaternion.Euler(
                        0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                    rect.sizeDelta = new Vector2(delta.magnitude, lineThickness);
                }

                if (lines[i].color != leg.Color)
                {
                    lines[i].color = leg.Color;
                }

                if (!leg.Node || node >= nodes.Count) continue;

                while (nodeStates.Count <= node) nodeStates.Add(new NodeState { Size = -1f });
                NodeState prevNode = nodeStates[node];

                if (prevNode.Position != to || !Mathf.Approximately(prevNode.Size, nodeSize))
                {
                    nodeStates[node] = new NodeState { Position = to, Size = nodeSize };
                    RectTransform nodeRect = nodes[node].rectTransform;
                    nodeRect.localPosition = to;
                    nodeRect.sizeDelta = Vector2.one * nodeSize;
                }

                if (nodes[node].color != leg.Color)
                {
                    nodes[node].color = leg.Color;
                }
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
