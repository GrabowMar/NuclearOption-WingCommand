using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL › ROUTE (spec WMC rebuild §ROUTE; the user merged ROUTE and AP): the scope's quick route — draw points
    /// on the map, set each one's altitude, speed and arrival action, send it, loop it, skip a leg, save it — then MY
    /// AUTOPILOT: your own holds, and NAV, which flies the route you drew.</summary>
    internal sealed partial class WmcTactical
    {
        private const int DraftRows = 7;
        private const float ListRow = 24f;
        private static readonly ApField[] ApFields = { ApField.Heading, ApField.Altitude, ApField.VerticalSpeed, ApField.Speed };
        private static readonly string[] ApNames = { "HDG", "ALT", "VS", "SPD" };

        private WmcScroll routeScroll;
        private TMP_Text routeHead, draftEmpty, apNote;
        private readonly GameObject[] draftRoots = new GameObject[DraftRows];
        private readonly TMP_Text[] draftLabels = new TMP_Text[DraftRows];
        private readonly Image[] draftRails = new Image[DraftRows];
        private readonly long[] draftKeys = new long[DraftRows];
        private readonly List<RouteLeg> draftLegs = new List<RouteLeg>(RouteDraft.MaxPoints + 1);
        private readonly TMP_Text[] apValues = new TMP_Text[4];
        private readonly AvButton[] apModes = new AvButton[7];
        private readonly ConfirmGate routeDeleteGate = new ConfirmGate();
        private readonly List<AvKit.PopupEntry> routeEntries = new List<AvKit.PopupEntry>(RouteStore.Max);
        private AvButton draw, action, del, skip, send, undo, clear, loop, save, savedPicker, savedDelete;
        private TMP_Text altValue, spdValue;
        private int draftFirst, savedSelected = -1, routeHeadKey = int.MinValue, apKey = int.MinValue;
        private float altShown = -1f, spdShown = -1f;
        private RouteLoop loopShown = (RouteLoop)255;

        private void BuildRoute(RectTransform root)
        {
            routeScroll = WmcScroll.Build(root, new Rect(x, -BannerTop, width + 8f, 100f), "RouteScroll");
            RectTransform s = routeScroll.Content;
            float w = routeScroll.Width, y = 0f;

            routeHead = WmcKit.Text(s, new Rect(0f, y, w - 84f, 18f), "section-title");
            draw = AvStyled.Button(s, new Rect(w - 80f, y, 80f, 20f), "DRAW", "btn", ToggleDraw, AvButtonStyle.Toggle);
            draw.WithTooltip("Right-click the map to add route points while DRAW is lit.");
            ids["tac.route.draw"] = draw;
            y -= 24f;
            for (int i = 0; i < DraftRows; i++)
            {
                int k = i;
                RectTransform row = Container(s, "RoutePoint" + i, new Rect(0f, y - i * ListRow, w, ListRow - 2f));
                AvButton hit = WmcUi.Card(row, new Rect(0f, 0f, w, ListRow - 2f), () => PickPoint(k), out _, out draftRails[i]);
                hit.WithTooltip("Select this point to change its altitude, speed or arrival action.");
                ids["tac.route.row" + i] = hit;
                draftLabels[i] = WmcKit.Text(row, new Rect(10f, -2f, w - 16f, ListRow - 6f), "row-sub");
                draftRoots[i] = row.gameObject;
                row.gameObject.SetActive(false);
                draftKeys[i] = long.MinValue;
            }
            draftEmpty = WmcKit.Text(s, new Rect(0f, y - 2f, w, 18f), "hint");
            draftEmpty.text = "No points. Press DRAW, then right-click the map.";
            y -= DraftRows * ListRow + 4f;

            float half = (w - WmcUi.Gap) / 2f;
            AvButton[] alt = AvKit.Stepper(s, 0f, y, half, out altValue, () => EditDraft(d => d.StepAltitude(-1)),
                () => EditDraft(d => d.StepAltitude(+1)), "Altitude of the selected point, or of new points (AUTO: the task's own).");
            AvButton[] spd = AvKit.Stepper(s, half + WmcUi.Gap, y, half, out spdValue, () => EditDraft(d => d.StepSpeed(-1)),
                () => EditDraft(d => d.StepSpeed(+1)), "Speed of the selected point, or of new points (AUTO: cruise).");
            ids["tac.route.alt-"] = alt[0];
            ids["tac.route.alt+"] = alt[1];
            ids["tac.route.spd-"] = spd[0];
            ids["tac.route.spd+"] = spd[1];
            y -= 34f;

            float third = (w - WmcUi.Gap * 2f) / 3f;
            action = RouteButton(s, 0f, y, third, "ACTION", "tac.route.action", () => EditDraft(d => d.CycleAction()),
                "The selected point's arrival: orbit 2 min, land, cargo, none.");
            del = RouteButton(s, third + WmcUi.Gap, y, third, "DEL PT", "tac.route.del", () => EditDraft(d => d.RemoveSelected()),
                "Remove the selected point.");
            skip = RouteButton(s, 2f * (third + WmcUi.Gap), y, third, "SKIP LEG", "tac.route.skip",
                () => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, last.Scope))),
                "The scope's task goes on to its next point now.");
            y -= TogglePitch;
            float fifth = (w - WmcUi.Gap * 4f) / 5f;
            send = RouteButton(s, 0f, y, fifth, "SEND", "tac.route.send", Send,
                "Fly the draft: one point is a MOVE, more a ROUTE; LOOP or PING-PONG makes a patrol.");
            undo = RouteButton(s, fifth + WmcUi.Gap, y, fifth, "UNDO", "tac.route.undo", () => EditDraft(d => d.Undo()), "Remove the last point.");
            clear = RouteButton(s, 2f * (fifth + WmcUi.Gap), y, fifth, "CLEAR", "tac.route.clear", () => EditDraft(d => d.Clear()), "Empty the draft.");
            loop = RouteButton(s, 3f * (fifth + WmcUi.Gap), y, fifth, "ONCE", "tac.route.loop", () => EditDraft(d => d.CycleLoop()),
                "Fly the route once, loop it, or back and forth.");
            save = RouteButton(s, 4f * (fifth + WmcUi.Gap), y, fifth, "SAVE", "tac.route.save", SaveRoute, "Keep the draft as a saved route.");
            y -= TogglePitch + 2f;

            AvStyled.Label(s, new Rect(0f, y, KeyWidth, ToggleH), "SAVED", "metric-key");
            savedPicker = AvStyled.Button(s, new Rect(KeyWidth, y, w - KeyWidth - 92f, ToggleH), "PICK A SAVED ROUTE ›", "btn", OpenSaved);
            savedPicker.WithTooltip("Load a saved route into the draft; SEND flies it.");
            ids["tac.route.saved"] = savedPicker;
            savedDelete = AvStyled.Button(s, new Rect(w - 88f, y, 88f, ToggleH), "DELETE", "btn", DeleteSaved);
            savedDelete.WithTooltip("Delete the loaded saved route (press twice).");
            ids["tac.route.saved.del"] = savedDelete;
            y -= TogglePitch + 8f;

            AvStyled.Label(s, new Rect(0f, y, w, 16f), "MY AUTOPILOT", "section-title");
            apNote = WmcKit.Text(s, new Rect(120f, y, w - 120f, 16f), "section-title-note", TextAlignmentOptions.MidlineRight);
            y -= 20f;
            string[] modes = { "LVL", "HDG", "ALT", "VS", "SPD", "NAV", "OFF" };
            string[] keys = { "level", "heading", "altitude", "vs", "speed", "nav", "off" };
            string[] tips =
            {
                "Wings level.", "Hold the current heading.", "Hold the current altitude.", "Hold the current climb or descent.",
                "Hold the current speed.", "Fly the route you drew (or the scope's route when the draft is empty).", "Autopilot off.",
            };
            float mw = (w - WmcUi.Gap * 6f) / 7f;
            for (int i = 0; i < modes.Length; i++)
            {
                int k = i;
                apModes[i] = AvStyled.Button(s, new Rect(i * (mw + WmcUi.Gap), y, mw, ToggleH), modes[i], "btn", () => PressAp(k), AvButtonStyle.Toggle);
                apModes[i].WithTooltip(tips[i]);
                ids["tac.route.ap." + keys[i]] = apModes[i];
            }
            y -= TogglePitch + 2f;
            for (int i = 0; i < ApFields.Length; i++)
            {
                ApField f = ApFields[i];
                float fx = (i % 2) * (half + WmcUi.Gap), fy = y - (i / 2) * 34f;
                AvButton[] step = AvKit.Stepper(s, fx, fy, half, out apValues[i],
                    () => PlayerAutopilot.Instance?.Adjust(f, -1), () => PlayerAutopilot.Instance?.Adjust(f, 1),
                    ApNames[i] + ": the held value; the hold flies to it.");
                string id = "tac.route.ap." + f.ToString().ToLowerInvariant();
                ids[id + ".down"] = step[0];
                ids[id + ".up"] = step[1];
            }
            y -= 2f * 34f;
            routeScroll.SetContentHeight(-y + 4f);
        }

        private AvButton RouteButton(RectTransform s, float bx, float y, float bw, string text, string id, System.Action click, string tip)
        {
            AvButton b = AvStyled.Button(s, new Rect(bx, y, bw, ToggleH), text, "btn", click);
            b.WithTooltip(tip);
            ids[id] = b;
            return b;
        }

        private void LayoutRoute(float region)
        {
            routeScroll?.SetViewport(new Rect(x, -BannerTop, width + 8f, Mathf.Max(40f, region - BannerTop)));
        }

        private void ToggleDraw()
        {
            if (last == null) return;
            last.Map.Arm(last, last.Map.Mode == MapMode.Route ? MapMode.Off : MapMode.Route);
        }

        /// <summary>A draft edit: local, allowed on a client too; only SEND is an order.</summary>
        private void EditDraft(System.Action<RouteDraft> edit)
        {
            if (last != null) edit(last.Draft);
        }

        private void PickPoint(int row)
        {
            if (last != null) last.Draft.Select(draftFirst + row);
        }

        private void Send() => WmcUi.Order(last, () =>
        {
            if (!last.Draft.TryTask(out WingTask task, out string why))
            {
                WingToast.Show(why);
                return;
            }
            if (!WingOrders.Run(WingOrder.Tasked(task, last.Scope)).Accepted) return;
            last.Draft.Clear();
            if (last.Map.Mode == MapMode.Route) last.Map.Disarm();
        });

        private void SaveRoute()
        {
            if (last == null) return;
            if (WmcRoutes.Store.Save(last.Draft, out string name))
            {
                WmcRoutes.Save();
                WingToast.Show("Saved as " + name);
            }
            else WingToast.Show(last.Draft.Count == 0 ? "Nothing to save: add points first" : RouteStore.Max + " routes saved already: delete one");
        }

        private void OpenSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (routes.Count == 0)
            {
                WingToast.Show("No saved routes. SAVE keeps the draft.");
                return;
            }
            routeEntries.Clear();
            for (int i = 0; i < routes.Count; i++)
                routeEntries.Add(new AvKit.PopupEntry(routes[i].Name, routes[i].Points.Length + " PTS · " + RouteDraft.LoopText(routes[i].Loop),
                    i == savedSelected));
            profilePopup.Show(WmcKit.RectIn(page, (RectTransform)savedPicker.transform), routeEntries, PickSaved);
        }

        private void PickSaved(int i)
        {
            if (last == null) return;
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (i < 0 || i >= routes.Count) return;
            savedSelected = i;
            WmcRoutes.Store.Load(i, last.Draft);
            savedPicker.SetText(routes[i].Name + " ›");
            WingToast.Show("Loaded " + routes[i].Name + ": SEND to fly it");
        }

        private void DeleteSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (savedSelected < 0 || savedSelected >= routes.Count) return;
            string name = routes[savedSelected].Name;
            if (!routeDeleteGate.Press(name, Time.unscaledTime))
            {
                WingToast.Show("Delete " + name + "? Press DELETE again");
                return;
            }
            WmcRoutes.Store.Remove(savedSelected);
            WmcRoutes.Save();
            savedSelected = -1;
            savedPicker.SetText("PICK A SAVED ROUTE ›");
            WingToast.Show(name + " deleted");
        }

        private void PressAp(int k)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                WingToast.Show("Autopilot unavailable");
                return;
            }
            switch (k)
            {
                case 0: WingCommands.Autopilot(ApCommand.Level); break;
                case 1: WingCommands.Autopilot(ApCommand.Heading); break;
                case 2: WingCommands.Autopilot(ApCommand.Altitude); break;
                case 3: WingCommands.Autopilot(ApCommand.VerticalSpeed); break;
                case 4: WingCommands.Autopilot(ApCommand.Speed); break;
                case 5: EngageNav(); break;
                default: WingCommands.Autopilot(ApCommand.Off); break;
            }
        }

        /// <summary>NAV flies the draft; with no draft, the scope's own route (what the element is flying now).</summary>
        private void EngageNav()
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            RouteDraft d = last?.Draft;
            if (d != null && d.Count > 0)
            {
                var pts = new Waypoint[d.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = d[i];
                ap.EngageNav(pts, pts.Length);
                return;
            }
            WingPlanner p = last?.Wing != null && !last.Client && last.Wing.Roster.InUse(last.ScopeElement) ? last.Wing.PlannerOf(last.ScopeElement) : null;
            WingTask task = p != null && p.Active ? p.Current : null;
            bool path = task != null && (task.Kind == TaskKind.Move || task.Kind == TaskKind.Route || task.Kind == TaskKind.Patrol);
            ap.EngageNav(path ? task.Points : null, path ? task.Points.Length : 0);
        }

        private void RefreshRoute(WmcContext c)
        {
            WingService w = c.Wing;
            WingPlanner p = w != null && !c.Client && w.Roster.InUse(c.ScopeElement) ? w.PlannerOf(c.ScopeElement) : null;
            RouteDraft d = c.Draft;
            int headKey = c.ScopeElement * 1000 + d.Count;
            if (headKey != routeHeadKey)
            {
                routeHeadKey = headKey;
                string name = w != null && !c.Client ? w.Roster.Name(c.ScopeElement) : ElementRoster.Letter(c.ScopeElement);
                routeHead.text = "ROUTE · " + (string.IsNullOrEmpty(name) ? ElementRoster.Letter(c.ScopeElement) : name) + " · "
                    + d.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " PTS";
            }
            draw.SetLatched(c.Map.Mode == MapMode.Route);
            draw.SetEnabled(c.CanOrder);

            draftLegs.Clear();
            Vec3 from = WmcMapInput.From(c, out float speed);
            RouteView.Draft(d, from, speed, draftLegs);
            draftFirst = RouteDraft.Window(d.Count, DraftRows, d.Selected);
            for (int r = 0; r < DraftRows; r++)
            {
                int i = draftFirst + r;
                bool on = i < d.Count;
                if (draftRoots[r].activeSelf != on) draftRoots[r].SetActive(on);
                if (!on)
                {
                    draftKeys[r] = long.MinValue;
                    continue;
                }
                Waypoint wp = d[i];
                RouteLeg l = draftLegs[i];
                long key = RowKey(i, l, wp, i == d.Selected);
                if (key == draftKeys[r]) continue;
                draftKeys[r] = key;
                draftLabels[r].text = RouteView.Label(l) + " · " + RouteDraft.AltitudeText(wp.Altitude) + " · "
                    + RouteDraft.SpeedText(wp.Speed) + " · " + RouteDraft.ActionText(wp);
                WmcUi.SetRail(draftRails[r], i == d.Selected ? "armed" : "info");
            }
            if (draftEmpty.gameObject.activeSelf != (d.Count == 0)) draftEmpty.gameObject.SetActive(d.Count == 0);
            float alt = d.Selected >= 0 ? d[d.Selected].Altitude : d.Altitude, spd = d.Selected >= 0 ? d[d.Selected].Speed : d.Speed;
            if (!Same(alt, altShown))
            {
                altShown = alt;
                altValue.text = RouteDraft.AltitudeText(alt);
            }
            if (!Same(spd, spdShown))
            {
                spdShown = spd;
                spdValue.text = RouteDraft.SpeedText(spd);
            }
            if (d.Loop != loopShown)
            {
                loopShown = d.Loop;
                loop.SetText(RouteDraft.LoopText(d.Loop));
            }
            bool any = d.Count > 0, picked = d.Selected >= 0, canSkip = c.CanOrder && p != null && p.Active && p.Leg >= 0;
            Gate(action, picked, "Click a point row first", "The selected point's arrival: orbit 2 min, land, cargo, none.");
            Gate(del, picked, "Click a point row first", "Remove the selected point.");
            Gate(send, any && c.CanOrder, !c.CanOrder ? "Orders are host only for now" : "Add points first",
                "Fly the draft: one point is a MOVE, more a ROUTE; LOOP or PING-PONG makes a patrol.");
            Gate(undo, any, "The draft is empty", "Remove the last point.");
            Gate(clear, any, "The draft is empty", "Empty the draft.");
            Gate(save, any, "Add points first", "Keep the draft as a saved route.");
            Gate(skip, canSkip, "The scope has no route leg to skip", "The scope's task goes on to its next point now.");
            savedDelete.SetEnabled(savedSelected >= 0 && savedSelected < WmcRoutes.Store.Routes.Count);
            RefreshAutopilot();
        }

        /// <summary>Enabled, or disabled with the reason as its tooltip (0.9 critique §2.14: nothing disables silently).</summary>
        private static void Gate(AvButton b, bool on, string why, string tip)
        {
            b.SetEnabled(on);
            b.WithTooltip(on ? tip : why);
        }

        private void RefreshAutopilot()
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                WmcKit.Set(apNote, "UNAVAILABLE");
                return;
            }
            HoldSpec h = ap.Session.Spec;
            apModes[0].SetLatched(h.Lateral == LateralHold.Level);
            apModes[1].SetLatched(h.Lateral == LateralHold.Heading);
            apModes[2].SetLatched(h.Vertical == VerticalHold.Altitude);
            apModes[3].SetLatched(h.Vertical == VerticalHold.VerticalSpeed);
            apModes[4].SetLatched(h.Speed);
            apModes[5].SetLatched(h.Lateral == LateralHold.Nav);
            apModes[6].SetLatched(!ap.Session.Engaged);
            int key = (int)h.Lateral * 7 + (int)h.Vertical * 3 + (h.Speed ? 1 : 0) + Mathf.RoundToInt(h.HeadingDeg) * 101
                + Mathf.RoundToInt(h.AltitudeM) * 1009 + Mathf.RoundToInt(h.VerticalSpeedMps * 10f) * 13 + Mathf.RoundToInt(h.SpeedMps) * 17
                + ap.Nav.Index * 5 + (ap.Session.LateralOverride ? 2 : 0) + (ap.Session.VerticalOverride ? 4 : 0);
            if (key == apKey) return;
            apKey = key;
            string text = WingHudText.Autopilot(h, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count);
            apNote.text = text.Length > 0 ? text : "AP OFF";
            for (int i = 0; i < ApFields.Length; i++)
            {
                ApField f = ApFields[i];
                bool held = f == ApField.Heading ? h.Lateral == LateralHold.Heading || h.Lateral == LateralHold.Nav
                    : f == ApField.Altitude ? h.Vertical == VerticalHold.Altitude
                    : f == ApField.VerticalSpeed ? h.Vertical == VerticalHold.VerticalSpeed : h.Speed;
                apValues[i].text = ApNames[i] + " " + ApSteps.Readout(h, f, held);
            }
        }

        private static bool Same(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

        /// <summary>A draft row's content: rebuilt only when point, tenth of a km, second, altitude, speed, action or
        /// selection change.</summary>
        private static long RowKey(int i, in RouteLeg l, in Waypoint w, bool selected)
        {
            unchecked
            {
                long k = i;
                k = k * 397 ^ (float.IsNaN(l.Km) ? -1L : (long)(l.Km * 10f));
                k = k * 397 ^ (float.IsNaN(l.Eta) ? -1L : (long)l.Eta);
                k = k * 397 ^ (float.IsNaN(w.Altitude) ? -1L : (long)w.Altitude);
                k = k * 397 ^ (float.IsNaN(w.Speed) ? -1L : (long)w.Speed);
                k = k * 397 ^ (long)w.Action;
                return k * 2 + (selected ? 1 : 0);
            }
        }
    }
}
