using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL (spec WMC program §6 notch 1): the theatre's own terrain map — zoom about the cursor, drag to pan,
    /// FIT — with the wing, its elements' routes and the draft, known contacts and friendly fields; the same right-click
    /// orders as the map layer (§5); element cards on the left and the deep member card on the right.</summary>
    internal sealed partial class RoomTactical : IRoomPage
    {
        private const float Board = 300f, Card = 320f, Pad = 12f, StripHeight = 30f, PromptHeight = 18f, PickPixels = 12f;
        private const int MaxContacts = 64;
        private static readonly MapMode[] Modes =
            { MapMode.Move, MapMode.Route, MapMode.Orbit, MapMode.Hold, MapMode.Attack, MapMode.Cargo, MapMode.Off };
        private static readonly Color PlayerColor = new Color(1f, 1f, 1f, 0.95f), FieldColor = new Color(0.62f, 0.66f, 0.70f, 0.9f);
        private static readonly Color DraftColor = new Color(1f, 1f, 1f, 0.85f);

        private enum Kind : byte { Player, Member, Contact, Field }

        private struct Mark
        {
            public float X, Z, Size;
            public Color Color;
            public string Label;
            public Kind Kind;
            public uint Id;
            public Unit Unit;
        }

        private readonly MapView view = new MapView();
        private readonly List<Mark> marks = new List<Mark>();
        private readonly List<RouteLeg> legs = new List<RouteLeg>();
        private readonly List<Color> legColors = new List<Color>();
        private readonly List<string> legLabels = new List<string>();
        private readonly List<RouteRing> rings = new List<RouteRing>();
        private readonly List<Color> ringColors = new List<Color>();
        private readonly List<Mark> contacts = new List<Mark>();
        private readonly AvButton[] modeButtons = new AvButton[7];
        private readonly Dictionary<string, AvButton> ids = new Dictionary<string, AvButton>();
        private RectTransform viewRect;
        private RoomMapLayer layer;
        private Image terrain;
        private TMP_Text prompt, empty;
        private WmcContext last;
        private Vector2 terrainSize;
        private float nextTerrain, lastFit;
        private bool framed, viewChanged;

        public IReadOnlyDictionary<string, AvButton> Controls => ids;
        public int Markers => marks.Count;
        public int LegCount => legs.Count;
        public int ContactCount { get; private set; }
        public int FieldCount { get; private set; }
        public float MetresPerPixel => view.MetresPerPixel;

        public string Hint => "Scroll to zoom, drag to pan, click a wingman to select (shift adds), right-click to order " +
                              (last != null ? last.ScopeLabel : "WING") + ". FIT frames the wing; FIT twice the theatre.";

        public void Build(RectTransform body, Rect area)
        {
            framed = false;
            float mapX = Board + Pad * 2f, mapW = area.width - Board - Card - Pad * 4f;
            float top = -Pad;
            // The strip: the same map-order modes as ORDERS, and FIT.
            float bw = (mapW - WmcUi.Gap * 7f) / 8f;
            for (int i = 0; i < Modes.Length; i++)
            {
                MapMode m = Modes[i];
                modeButtons[i] = AvStyled.Button(body, new Rect(mapX + i * (bw + WmcUi.Gap), top, bw, StripHeight), MapOrders.Label(m), "btn",
                    () => Arm(m), AvButtonStyle.Toggle);
                ids["room.map." + MapOrders.Label(m).ToLowerInvariant()] = modeButtons[i];
            }
            AvButton fit = AvStyled.Button(body, new Rect(mapX + 7 * (bw + WmcUi.Gap), top, bw, StripHeight), "FIT", "btn", Fit);
            fit.WithTooltip("Frame the wing and its routes; press again to frame the whole theatre.");
            ids["room.fit"] = fit;
            prompt = AvStyled.Label(body, new Rect(mapX, top - StripHeight - 4f, mapW, PromptHeight), "", "hint");
            prompt.enableWordWrapping = false;
            prompt.overflowMode = TextOverflowModes.Ellipsis;
            float mapTop = top - StripHeight - 4f - PromptHeight - 4f;
            float mapH = area.height + mapTop - Pad;

            // The map: ground, terrain, symbols, then the input layer, clipped to its rectangle.
            var mapGo = new GameObject("TacticalMap", typeof(RectTransform), typeof(RectMask2D));
            var mapRect = (RectTransform)mapGo.transform;
            mapRect.SetParent(body, false);
            AvKit.Place(mapRect, new Rect(mapX, mapTop, mapW, mapH));
            Image ground = AvKit.Panel(mapRect, new Rect(0f, 0f, mapW, mapH), AvTheme.SurfaceInert);
            ground.raycastTarget = false;
            AvKit.Rule(mapRect, new Rect(0f, 0f, mapW, 1f), AvTheme.Frame);
            var viewGo = new GameObject("View", typeof(RectTransform));
            viewRect = (RectTransform)viewGo.transform;
            viewRect.SetParent(mapRect, false);
            AvKit.Stretch(viewRect);
            terrain = AvKit.Panel(viewRect, new Rect(0f, 0f, 1f, 1f), Color.white);
            terrain.type = Image.Type.Simple;
            terrain.raycastTarget = false;
            terrain.enabled = false;
            RectTransform tr = terrain.rectTransform;
            tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0.5f, 0.5f);
            layer = new RoomMapLayer(viewRect, view);
            empty = AvStyled.Label(mapRect, new Rect(0f, -mapH * 0.5f + 10f, mapW, 20f), "No theatre map for this mission.", "hint",
                align: TextAlignmentOptions.Center);
            empty.gameObject.SetActive(false);
            Image catcher = AvKit.Panel(mapRect, new Rect(0f, 0f, mapW, mapH), new Color(0f, 0f, 0f, 0f));
            catcher.raycastTarget = true;
            AvKit.Stretch(catcher.rectTransform);
            catcher.gameObject.AddComponent<MapInput>().Page = this;
            view.Resize(mapW, mapH);

            BuildBoards(body, area);
        }

        public void Show(WmcContext c)
        {
            last = c;
            ResolveTerrain(true);
            if (!framed)
            {
                framed = true;
                Collect(c);
                FitWing();
            }
            Draw();
        }

        public void Hide()
        {
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            ResolveTerrain(false);
            Collect(c);
            Draw();
            MapMode mode = c.Map.Mode;
            for (int i = 0; i < Modes.Length; i++)
            {
                modeButtons[i].SetLatched(Modes[i] == mode);
                modeButtons[i].SetEnabled(Modes[i] == MapMode.Off || c.CanOrder);
            }
            prompt.text = c.Map.Prompt(c.ScopeLabel) ?? (c.Selection.Count > 0 ? "Right-click the map to MOVE " + c.ScopeLabel + "."
                : "Arm a mode, then right-click the map; or select wingmen and right-click to move them.");
            RefreshBoards(c);
        }

        public void Tick(WmcContext c)
        {
            if (!viewChanged) return;
            viewChanged = false;
            Draw();
        }

        // ---- Data (6 Hz) ------------------------------------------------------------------------

        private void ResolveTerrain(bool now)
        {
            if (!now && Time.unscaledTime < nextTerrain) return;
            nextTerrain = Time.unscaledTime + 1f;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings settings = level != null ? level.LoadedMapSettings : null;
            Vector2 size = settings != null ? settings.MapSize : Vector2.zero;
            bool ok = settings != null && settings.MapImage != null && size.x > 0f && size.y > 0f && !float.IsNaN(size.x) && !float.IsNaN(size.y)
                      && !float.IsInfinity(size.x) && !float.IsInfinity(size.y);
            // Borrowed, never copied or destroyed (spec §6).
            terrain.sprite = ok ? settings.MapImage : null;
            terrain.enabled = ok;
            empty.gameObject.SetActive(!ok);
            terrainSize = ok ? size : Vector2.zero;
            if (ok) view.SetMapHalf(Mathf.Max(size.x, size.y) * 0.5f);
        }

        private void Collect(WmcContext c)
        {
            marks.Clear();
            legs.Clear();
            legColors.Clear();
            legLabels.Clear();
            rings.Clear();
            ringColors.Clear();
            ContactCount = FieldCount = 0;
            WingService w = c?.Wing;
            if (w == null) return;
            Aircraft player = w.Player;
            FactionHQ hq = player != null ? player.NetworkHQ : null;
            if (hq != null)
            {
                foreach (Airbase a in hq.GetAirbases())
                {
                    if (a == null || a.center == null) continue;
                    GlobalPosition g = a.center.position.ToGlobalPosition();
                    marks.Add(new Mark { X = g.x, Z = g.z, Size = 8f, Color = FieldColor, Kind = Kind.Field,
                        Label = view.MetresPerPixel < 60f ? a.SavedAirbase?.DisplayName : null });
                    FieldCount++;
                }
                CollectContacts(hq);
            }
            if (!c.Client)
                for (int e = 0; e < ElementRoster.MaxElements; e++)
                {
                    if (!w.Roster.InUse(e)) continue;
                    WingPlanner p = w.PlannerOf(e);
                    if (!p.Active || p.Lead == null) continue;
                    int from = legs.Count;
                    RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, legs, rings);
                    Color col = WmcMapOverlay.ElementColor(e);
                    for (int i = from; i < legs.Count; i++) AddLegStyle(col, legs[i]);
                    while (ringColors.Count < rings.Count) ringColors.Add(col);
                }
            if (c.Draft.Count > 0)
            {
                int from = legs.Count;
                Vec3 origin = WmcMapInput.From(c, out float speed);
                RouteView.Draft(c.Draft, origin, speed, legs);
                for (int i = from; i < legs.Count; i++) AddLegStyle(DraftColor, legs[i]);
            }
            foreach (WingMember m in w.Members)
            {
                if ((object)m.Aircraft == null || !m.Alive || m.Released) continue;
                int e = w.ElementOf(m);
                marks.Add(new Mark { X = m.Last.Pos.X, Z = m.Last.Pos.Z, Size = 10f, Color = WmcMapOverlay.ElementColor(e), Kind = Kind.Member,
                    Label = WingMarkers.Badge(e, m.Number), Id = m.Aircraft.persistentID.Id, Unit = m.Aircraft });
            }
            if (player != null)
            {
                GlobalPosition g = player.GlobalPosition();
                marks.Add(new Mark { X = g.x, Z = g.z, Size = 11f, Color = PlayerColor, Kind = Kind.Player, Label = "YOU" });
            }
        }

        private void AddLegStyle(Color c, in RouteLeg l)
        {
            legColors.Add(l.Closing ? new Color(c.r, c.g, c.b, c.a * 0.5f) : c);
            legLabels.Add(l.Number > 0 ? RouteView.Label(l) : null);
        }

        /// <summary>Enemy units the faction knows (their tracked, not true, positions), nearest the view centre first, capped.</summary>
        private void CollectContacts(FactionHQ hq)
        {
            contacts.Clear();
            if (hq.trackingDatabase == null) return;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !t.TryGetUnit(out Unit u) || u == null || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq) continue;
                if (u is Missile || u is PilotDismounted) continue;
                GlobalPosition g = t.GetPosition();
                contacts.Add(new Mark { X = g.x, Z = g.z, Size = u is Aircraft ? 8f : 7f, Color = WingMarkers.TargetColor, Kind = Kind.Contact, Unit = u });
            }
            if (contacts.Count > MaxContacts) contacts.Sort(ByViewCentre);
            for (int i = 0; i < contacts.Count && i < MaxContacts; i++) marks.Add(contacts[i]);
            ContactCount = Math.Min(contacts.Count, MaxContacts);
        }

        private int ByViewCentre(Mark a, Mark b)
        {
            float da = (a.X - view.CentreX) * (a.X - view.CentreX) + (a.Z - view.CentreZ) * (a.Z - view.CentreZ);
            float db = (b.X - view.CentreX) * (b.X - view.CentreX) + (b.Z - view.CentreZ) * (b.Z - view.CentreZ);
            return da.CompareTo(db);
        }

        // ---- Draw (6 Hz and on zoom/pan) ----------------------------------------------------------

        private void Draw()
        {
            if (layer == null) return;
            if (terrain.enabled)
            {
                view.Project(0f, 0f, out float cx, out float cy);
                terrain.rectTransform.localPosition = new Vector3(cx, cy, 0f);
                terrain.rectTransform.sizeDelta = new Vector2(terrainSize.x / view.MetresPerPixel, terrainSize.y / view.MetresPerPixel);
            }
            layer.Begin();
            for (int i = 0; i < rings.Count; i++) layer.Ring(rings[i].X, rings[i].Z, rings[i].Radius, ringColors[i]);
            for (int i = 0; i < legs.Count; i++)
            {
                RouteLeg l = legs[i];
                layer.Line(l.FromX, l.FromZ, l.ToX, l.ToZ, legColors[i], 2f);
                if (legLabels[i] != null) layer.Label(l.ToX, l.ToZ, legLabels[i], legColors[i]);
            }
            for (int i = 0; i < marks.Count; i++)
            {
                Mark m = marks[i];
                if (m.Kind == Kind.Member && last != null && last.Selection.Contains(m.Id)) layer.Halo(m.X, m.Z, 22f, m.Color);
                layer.Dot(m.X, m.Z, m.Color, m.Size, m.Label);
            }
            layer.End();
        }

        // ---- View and input -------------------------------------------------------------------

        private void FitWing()
        {
            int n = 0;
            foreach (Mark m in marks)
                if (m.Kind == Kind.Member || m.Kind == Kind.Player) n++;
            foreach (RouteLeg l in legs) n += 1;
            if (n == 0)
            {
                view.FitMap();
                return;
            }
            var xs = new float[n];
            var zs = new float[n];
            int k = 0;
            foreach (Mark m in marks)
                if (m.Kind == Kind.Member || m.Kind == Kind.Player)
                {
                    xs[k] = m.X;
                    zs[k++] = m.Z;
                }
            foreach (RouteLeg l in legs)
            {
                xs[k] = l.ToX;
                zs[k++] = l.ToZ;
            }
            view.Fit(xs, zs, k, 8000f, 40f);
        }

        private void Fit()
        {
            // A second FIT within a second frames the whole theatre.
            if (Time.unscaledTime - lastFit < 1f) view.FitMap();
            else FitWing();
            lastFit = Time.unscaledTime;
            viewChanged = true;
        }

        private void Arm(MapMode m)
        {
            if (last == null) return;
            last.Map.Arm(last, m == last.Map.Mode ? MapMode.Off : m);
        }

        /// <summary>The nearest mark of <paramref name="kind"/> within the pick radius of a view point, or -1.</summary>
        private int Pick(Vector2 local, Kind kind)
        {
            int best = -1;
            float bestSq = PickPixels * PickPixels;
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].Kind != kind) continue;
                view.Project(marks[i].X, marks[i].Z, out float px, out float py);
                float d = (px - local.x) * (px - local.x) + (py - local.y) * (py - local.y);
                if (d >= bestSq) continue;
                bestSq = d;
                best = i;
            }
            return best;
        }

        private void Click(Vector2 local, PointerEventData.InputButton button, bool shift)
        {
            WmcContext c = last;
            if (c == null) return;
            if (button == PointerEventData.InputButton.Left)
            {
                int i = Pick(local, Kind.Member);
                if (i < 0) return;
                if (shift) c.Selection.Toggle(marks[i].Id);
                else c.Selection.SelectOnly(marks[i].Id);
                c.Rescope();
                viewChanged = true;
                return;
            }
            if (button != PointerEventData.InputButton.Right) return;
            view.Unproject(local.x, local.y, out float x, out float z);
            int hit = Pick(local, Kind.Contact);
            c.Map.Place(c, new GlobalPosition(x, 0f, z), hit >= 0 ? marks[hit].Unit : null, shift);
        }

        /// <summary>Zoom and pan by automation (the scenario's checks).</summary>
        public void ZoomCentre(float factor)
        {
            view.ZoomAt(0f, 0f, factor);
            viewChanged = true;
        }

        public void FitNow() => Fit();

        /// <summary>A click at a world point as the mouse would make it (automation).</summary>
        public void ClickWorld(float x, float z, bool right, bool shift)
        {
            view.Project(x, z, out float px, out float py);
            Click(new Vector2(px, py), right ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left, shift);
        }

        private sealed class MapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
        {
            public RoomTactical Page;
            private bool dragged;

            private bool Local(PointerEventData e, out Vector2 local) =>
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out local);

            public void OnScroll(PointerEventData e)
            {
                if (Page == null || !Local(e, out Vector2 local)) return;
                Page.view.ZoomAt(local.x, local.y, Mathf.Pow(1.25f, e.scrollDelta.y));
                Page.viewChanged = true;
            }

            public void OnBeginDrag(PointerEventData e) => dragged = e.button == PointerEventData.InputButton.Left;

            public void OnDrag(PointerEventData e)
            {
                if (Page == null || e.button != PointerEventData.InputButton.Left) return;
                Canvas canvas = GetComponentInParent<Canvas>();
                float scale = canvas != null && canvas.rootCanvas.scaleFactor > 0f ? canvas.rootCanvas.scaleFactor : 1f;
                Page.view.PanBy(e.delta.x / scale, e.delta.y / scale);
                Page.viewChanged = true;
            }

            public void OnEndDrag(PointerEventData e)
            {
            }

            public void OnPointerClick(PointerEventData e)
            {
                // The release that ends a drag is not a click.
                if (dragged)
                {
                    dragged = false;
                    return;
                }
                if (Page == null || !Local(e, out Vector2 local)) return;
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                Page.Click(local, e.button, shift);
            }
        }
    }
}
