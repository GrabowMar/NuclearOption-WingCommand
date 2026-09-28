using System.Collections.Generic;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>The map's route layer (spec WMC program §5): every element's task path in the element's colour, the route
    /// draft while WMC is open, numbered points with distance and ETA, orbit rings and an order ping — pooled objects in the
    /// map's icon layer, moved at 5 Hz and when the zoom changes, hidden while the map is minimized. The plan layer (spec bezel v2
    /// §6), while PLAN › ELEMENTS shows or a plan runs: each lane's steps chained from its element, pending ones faded, finished
    /// ones gone, each anchored "B2 CAP · AFTER B1", the selected step wide with its legs numbered; and a CAP or SWEEP drag's live
    /// ring with its radius.</summary>
    internal sealed class WmcMapOverlay
    {
        private const float LineWidth = 2f, NodeSize = 9f, LabelWidth = 190f, LabelHeight = 16f, PingSeconds = 1.2f;
        private static readonly Color DraftColor = new Color(1f, 1f, 1f, 0.85f);
        // Element C is violet (spec bezel v2 §6): amber read as a caution rail. A letter badge always goes with the colour.
        private static readonly Color[] Elements =
        {
            new Color(0.22f, 1f, 0.40f), new Color(0.30f, 0.85f, 1f), new Color(0.72f, 0.58f, 1f), new Color(1f, 0.45f, 0.85f),
        };

        private readonly List<RouteLeg> legs = new List<RouteLeg>();
        private readonly List<RouteRing> rings = new List<RouteRing>();
        private readonly List<Color> legColors = new List<Color>();
        private readonly List<Color> ringColors = new List<Color>();
        private readonly List<Image> lines = new List<Image>();
        private readonly List<Image> nodes = new List<Image>();
        private readonly List<Image> ringImages = new List<Image>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private readonly List<long> labelKeys = new List<long>();
        private Transform layer;
        private Image ping, ghost;
        private TMP_Text ghostLabel;
        private float ghostRadius = -1f;
        private GlobalPosition pingAt;
        private float pingStart = -1f, nextRefresh, drawnInverse = -1f;
        private bool dirty, shown;
        private static Sprite disc, ring;

        public int LegCount => legs.Count;
        public int RingCount => rings.Count;

        public static Color ElementColor(int e) => Elements[e >= 0 && e < Elements.Length ? e : 0];

        /// <summary>The map moved or zoomed (DynamicMap.onMapChanged); scales are checked on the next tick.</summary>
        public void Dirty() => dirty = true;

        public void Ping(GlobalPosition at)
        {
            pingAt = at;
            pingStart = Time.unscaledTime;
        }

        /// <summary>Every frame while the panel is installed.</summary>
        public void Tick(WmcContext c, bool wmcVisible)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || map.iconLayer == null)
            {
                Hide();
                return;
            }
            if (layer != map.iconLayer.transform) Rebind(map);
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
                Collect(c, wmcVisible);
                Draw(map);
            }
            else if (dirty && !Mathf.Approximately(Inverse(map), drawnInverse)) Draw(map);
            dirty = false;
            AnimatePing(map);
            DrawGhost(map, c.Map);
        }

        /// <summary>A CAP or SWEEP right-drag: the ring it would guard and its radius, every frame.</summary>
        private void DrawGhost(DynamicMap map, WmcMapInput input)
        {
            if (!input.Dragging)
            {
                HideObject(ghost);
                HideObject(ghostLabel);
                return;
            }
            if (ghost == null) ghost = MakeImage("WmcAreaGhost", Ring());
            if (ghostLabel == null)
            {
                ghostLabel = AvStyled.Label((RectTransform)layer, new Rect(0f, 0f, LabelWidth, LabelHeight), "", "row-sub");
                RectTransform lt = ghostLabel.rectTransform;
                lt.anchorMin = lt.anchorMax = new Vector2(0.5f, 0.5f);
                lt.pivot = Vector2.zero;
                ghostLabel.raycastTarget = false;
                ghostLabel.enableWordWrapping = false;
                ghostLabel.overflowMode = TextOverflowModes.Overflow;
            }
            float factor = map.mapDisplayFactor, inverse = Inverse(map);
            Vector3 at = new Vector3(input.DragX * factor, input.DragZ * factor, 0f);
            ghost.rectTransform.localPosition = at;
            ghost.rectTransform.sizeDelta = Vector2.one * Mathf.Max(2f * input.DragRadius * factor, NodeSize * 2f * inverse);
            ghost.color = new Color(1f, 1f, 1f, 0.9f);
            ghostLabel.rectTransform.localPosition = at + new Vector3(7f, 3f, 0f) * inverse;
            ghostLabel.rectTransform.localScale = Vector3.one * inverse;
            if (!Mathf.Approximately(ghostRadius, input.DragRadius))
            {
                ghostRadius = input.DragRadius;
                ghostLabel.text = "R " + (input.DragRadius / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM";
            }
            Show(ghost.gameObject);
            Show(ghostLabel.gameObject);
        }

        private void Collect(WmcContext c, bool wmcVisible)
        {
            legs.Clear();
            rings.Clear();
            legColors.Clear();
            ringColors.Clear();
            WingService w = WingService.Instance;
            if (w != null && w.Selection != null && !c.Client)
                for (int e = 0; e < ElementRoster.MaxElements; e++)
                {
                    if (!w.Roster.InUse(e)) continue;
                    WingPlanner p = w.PlannerOf(e);
                    if (!p.Active || p.Lead == null) continue;
                    RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, legs, rings);
                    while (legColors.Count < legs.Count) legColors.Add(ElementColor(e));
                    while (ringColors.Count < rings.Count) ringColors.Add(ElementColor(e));
                }
            if (wmcVisible && c.Draft.Count > 0)
            {
                Vec3 from = WmcMapInput.From(c, out float speed);
                RouteView.Draft(c.Draft, from, speed, legs);
                while (legColors.Count < legs.Count) legColors.Add(DraftColor);
            }
            WingPlans plans = WingPlans.Instance;
            WmcPanel panel = WmcPanel.Instance;
            bool planShowing = wmcVisible && panel != null && panel.PlanShowing;
            if (w != null && plans != null && !c.Client && (planShowing || plans.Running))
                CollectPlan(w, plans, planShowing ? panel.Plan.SelectedLane : -1, planShowing ? panel.Plan.SelectedStep : -1);
        }

        /// <summary>The plan layer: each lane's steps from where its element is, in its colour.</summary>
        private void CollectPlan(WingService w, WingPlans plans, int selLane, int selStep)
        {
            WingPlan plan = plans.Plan;
            PlanRunner r = plans.Runner;
            bool live = r != null && (r.Running || plans.Completed);
            for (int e = 0; e < WingPlan.Lanes; e++)
            {
                var steps = plan.Steps[e];
                if (steps.Count == 0) continue;
                if (!WmcPlan.From(w, e, out float x0, out float z0, out float speed) && !PlanEdit.EndPoint(steps[0], out x0, out z0)) continue;
                Color color = ElementColor(e);
                for (int i = 0; i < steps.Count; i++)
                {
                    PlanStep p = steps[i];
                    StepState st = r != null ? r.State(e, i) : StepState.Pending;
                    bool gone = live && (st == StepState.Done || st == StepState.Skipped), selected = e == selLane && i == selStep;
                    if (!gone)
                    {
                        int first = legs.Count, firstRing = rings.Count;
                        if (p.Kind == PlanKind.Attack)
                        {
                            foreach (uint id in p.Targets ?? new uint[0])
                            {
                                Unit u = WmcContext.UnitOf(id);
                                if (u == null) continue;
                                GlobalPosition t = u.GlobalPosition();
                                legs.Add(new RouteLeg { FromX = x0, FromZ = z0, ToX = t.x, ToZ = t.z, Number = 1, Km = float.NaN, Eta = float.NaN });
                                rings.Add(new RouteRing { X = t.x, Z = t.z, Radius = 400f });
                            }
                        }
                        else if (p.Points != null && p.Points.Length > 0)
                            RouteView.Task(PlanCompile.Order(p, e).Task, 0, new Vec3(x0, 0f, z0), speed, legs, rings);
                        string tag = PlanRules.Name(e, i) + " " + PlanWords.Kind(p.Kind) + " · " + PlanWords.Start(plan, e, i)
                                     + (st == StepState.Held ? " · HELD" : st == StepState.Blocked ? " · BLOCKED" : "");
                        float alpha = selected || st == StepState.Running ? 1f : 0.55f;
                        for (int j = first; j < legs.Count; j++)
                        {
                            RouteLeg l = legs[j];
                            // Anchored once, at the step's last point; the selected step keeps its numbered legs.
                            if (j == legs.Count - 1) l.Text = tag;
                            else if (!selected) l.Number = 0;
                            l.Wide = selected;
                            legs[j] = l;
                            Color lc = color;
                            lc.a *= alpha;
                            legColors.Add(lc);
                        }
                        for (int j = firstRing; j < rings.Count; j++)
                        {
                            Color rc = color;
                            rc.a *= alpha;
                            ringColors.Add(rc);
                        }
                    }
                    if (PlanEdit.EndPoint(p, out float ex, out float ez))
                    {
                        x0 = ex;
                        z0 = ez;
                    }
                }
            }
        }

        private static float Inverse(DynamicMap map) => 1f / Mathf.Max(0.01f, map.mapImage.transform.localScale.x);

        private void Draw(DynamicMap map)
        {
            float inverse = Inverse(map), factor = map.mapDisplayFactor;
            drawnInverse = inverse;
            int node = 0;
            for (int i = 0; i < legs.Count; i++)
            {
                RouteLeg l = legs[i];
                Color color = legColors[i];
                if (l.Closing) color.a *= 0.5f;
                Vector3 from = new Vector3(l.FromX * factor, l.FromZ * factor, 0f), to = new Vector3(l.ToX * factor, l.ToZ * factor, 0f);
                Vector3 d = to - from;
                Image line = Line(i);
                RectTransform rt = line.rectTransform;
                rt.localPosition = from;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                rt.sizeDelta = new Vector2(d.magnitude, (l.Wide ? 1.5f * LineWidth : LineWidth) * inverse);
                if (line.color != color) line.color = color;
                Show(line.gameObject);
                if (l.Number <= 0) continue;
                Image n = Node(node);
                n.rectTransform.localPosition = to;
                n.rectTransform.sizeDelta = Vector2.one * NodeSize * inverse;
                if (n.color != color) n.color = color;
                Show(n.gameObject);
                TMP_Text label = Label(node);
                label.rectTransform.localPosition = to + new Vector3(7f, 3f, 0f) * inverse;
                label.rectTransform.localScale = Vector3.one * inverse;
                long key = Key(l);
                if (labelKeys[node] != key)
                {
                    labelKeys[node] = key;
                    label.text = l.Text != null ? l.Text
                        : l.Action == ArrivalAction.None || l.Action == ArrivalAction.Orbit
                        ? RouteView.Label(l) : RouteView.Label(l) + " · " + (l.Action == ArrivalAction.Land ? "LAND" : "CARGO");
                }
                if (label.color != color) label.color = color;
                Show(label.gameObject);
                node++;
            }
            for (int i = 0; i < rings.Count; i++)
            {
                Image r = RingImage(i);
                r.rectTransform.localPosition = new Vector3(rings[i].X * factor, rings[i].Z * factor, 0f);
                r.rectTransform.sizeDelta = Vector2.one * Mathf.Max(2f * rings[i].Radius * factor, NodeSize * 2f * inverse);
                Color color = ringColors[i];
                color.a *= 0.8f;
                if (r.color != color) r.color = color;
                Show(r.gameObject);
            }
            // Review focus 5: anything past this refresh's counts goes (a merged or finished element leaves nothing).
            for (int i = legs.Count; i < lines.Count; i++) HideObject(lines[i]);
            for (int i = node; i < nodes.Count; i++)
            {
                HideObject(nodes[i]);
                HideObject(labels[i]);
            }
            for (int i = rings.Count; i < ringImages.Count; i++) HideObject(ringImages[i]);
            shown = true;
        }

        /// <summary>A label's content: rebuilt only when point, tenth of a km, whole second or action change.</summary>
        private static long Key(in RouteLeg l)
        {
            long km = float.IsNaN(l.Km) ? 0xFFFFF : (long)(l.Km * 10f) & 0xFFFFF;
            long eta = float.IsNaN(l.Eta) ? 0xFFFFF : (long)l.Eta & 0xFFFFF;
            return (((long)l.Number << 44) | (km << 24) | (eta << 4) | (long)l.Action) ^ ((long)(l.Text?.GetHashCode() ?? 0) << 12);
        }

        private void AnimatePing(DynamicMap map)
        {
            if (pingStart < 0f) return;
            float t = (Time.unscaledTime - pingStart) / PingSeconds;
            if (ping == null) ping = MakeImage("WmcPing", Ring());
            if (t >= 1f)
            {
                pingStart = -1f;
                HideObject(ping);
                return;
            }
            float factor = map.mapDisplayFactor, inverse = Inverse(map);
            ping.rectTransform.localPosition = new Vector3(pingAt.x * factor, pingAt.z * factor, 0f);
            ping.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(24f, 96f, t) * inverse;
            ping.color = new Color(1f, 1f, 1f, 1f - t);
            Show(ping.gameObject);
        }

        public void Hide()
        {
            if (!shown && pingStart < 0f) return;
            foreach (Image i in lines) HideObject(i);
            foreach (Image i in nodes) HideObject(i);
            foreach (TMP_Text t in labels) HideObject(t);
            foreach (Image i in ringImages) HideObject(i);
            HideObject(ping);
            HideObject(ghost);
            HideObject(ghostLabel);
            pingStart = -1f;
            shown = false;
        }

        /// <summary>Drops the pools (a new map, or the panel reset); the objects go with their layer.</summary>
        public void Destroy()
        {
            foreach (Image i in lines) if (i != null) Object.Destroy(i.gameObject);
            foreach (Image i in nodes) if (i != null) Object.Destroy(i.gameObject);
            foreach (TMP_Text t in labels) if (t != null) Object.Destroy(t.gameObject);
            foreach (Image i in ringImages) if (i != null) Object.Destroy(i.gameObject);
            if (ping != null) Object.Destroy(ping.gameObject);
            if (ghost != null) Object.Destroy(ghost.gameObject);
            if (ghostLabel != null) Object.Destroy(ghostLabel.gameObject);
            ghost = null;
            ghostLabel = null;
            ghostRadius = -1f;
            lines.Clear();
            nodes.Clear();
            labels.Clear();
            labelKeys.Clear();
            ringImages.Clear();
            ping = null;
            layer = null;
            pingStart = -1f;
            shown = false;
        }

        private void Rebind(DynamicMap map)
        {
            Destroy();
            layer = map.iconLayer.transform;
        }

        private Image Line(int i)
        {
            while (lines.Count <= i)
            {
                Image img = MakeImage("WmcRouteLine", null);
                img.rectTransform.pivot = new Vector2(0f, 0.5f);
                // Lines under the icons.
                img.rectTransform.SetAsFirstSibling();
                lines.Add(img);
            }
            return lines[i];
        }

        private Image Node(int i)
        {
            while (nodes.Count <= i) nodes.Add(MakeImage("WmcRoutePoint", Disc()));
            return nodes[i];
        }

        private TMP_Text Label(int i)
        {
            while (labels.Count <= i)
            {
                TMP_Text t = AvStyled.Label((RectTransform)layer, new Rect(0f, 0f, LabelWidth, LabelHeight), "", "row-sub");
                RectTransform rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = Vector2.zero;
                t.raycastTarget = false;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                labels.Add(t);
                labelKeys.Add(-1L);
            }
            return labels[i];
        }

        private Image RingImage(int i)
        {
            while (ringImages.Count <= i)
            {
                Image img = MakeImage("WmcOrbitRing", Ring());
                img.rectTransform.SetAsFirstSibling();
                ringImages.Add(img);
            }
            return ringImages[i];
        }

        private Image MakeImage(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(layer, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = sprite != null;
            go.SetActive(false);
            return img;
        }

        private static void Show(GameObject go)
        {
            if (!go.activeSelf) go.SetActive(true);
        }

        private static void HideObject(Component c)
        {
            if (c != null && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        internal static Sprite Disc() => disc != null ? disc : disc = Circle("WmcDisc", 16, 0f);

        internal static Sprite Ring() => ring != null ? ring : ring = Circle("WmcRing", 64, 2f);

        /// <summary>An anti-aliased white disc (<paramref name="stroke"/> 0) or ring, made once.</summary>
        private static Sprite Circle(string name, int size, float stroke)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            float c = size * 0.5f, outer = c - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(outer - d + 0.5f);
                    if (stroke > 0f) a *= Mathf.Clamp01(d - (outer - stroke) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
    }
}
