using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>ORDERS (spec WMC program §4): the scoped element's task card; the MAP strip (right-click orders for the scope,
    /// §5); the ROUTE editor (draft points with altitude, speed and arrival action; SEND, UNDO, CLEAR, LOOP, SKIP, SAVE);
    /// SAVED routes; then the TASK, COMBAT and HELO orders — the radial's entry points, scoped.</summary>
    internal sealed class WmcOrdersTab : IWmcTab
    {
        private const int DraftRows = 8, SavedRows = 5;
        private const float ListRow = 24f, Head = 16f + WmcUi.Gap, Step = WmcUi.Row + WmcUi.Gap;
        private static readonly MapMode[] Modes =
            { MapMode.Move, MapMode.Route, MapMode.Orbit, MapMode.Hold, MapMode.Attack, MapMode.Cargo, MapMode.Off };
        private static readonly string[] ModeTips =
        {
            "Right-click the map: the scope flies there, then orbits.",
            "Right-click the map to add route points; SEND flies them.",
            "Right-click the map: the scope orbits there.",
            "Right-click the map: the scope holds there, facing away from where it is now.",
            "Right-click an enemy on the map to attack it; shift-click adds targets.",
            "Right-click a drop point: helicopters carrying cargo fly there, land and deliver.",
            "No map mode: right-click moves selected wingmen, or is the game's.",
        };

        private readonly Dictionary<string, AvButton> ids;
        private readonly List<AvButton> own = new List<AvButton>();
        private readonly AvButton[] modeButtons = new AvButton[7];
        private readonly AvButton[] draftHits = new AvButton[DraftRows], savedHits = new AvButton[SavedRows];
        private readonly Image[] draftRails = new Image[DraftRows], savedRails = new Image[SavedRows];
        private readonly TMP_Text[] draftLabels = new TMP_Text[DraftRows], savedLabels = new TMP_Text[SavedRows];
        private readonly GameObject[] draftRoots = new GameObject[DraftRows], savedRoots = new GameObject[SavedRows];
        private readonly long[] draftKeys = new long[DraftRows], savedKeys = new long[SavedRows];
        private readonly List<RouteLeg> draftLegs = new List<RouteLeg>(RouteDraft.MaxPoints + 1);
        private readonly ConfirmGate deleteGate = new ConfirmGate();
        private TMP_Text card, prompt, altValue, spdValue, draftEmpty, savedEmpty;
        private AvButton action, del, skip, send, undo, clear, loop, save, prev, next, deleteSaved;
        private WmcContext last;
        private int draftFirst, savedFirst, savedSelected = -1;
        private MapMode promptMode = (MapMode)255;
        private string promptScope;
        private RouteLoop loopShown = (RouteLoop)255;
        private float altShown = -1f, spdShown = -1f;

        public WmcOrdersTab(Dictionary<string, AvButton> controls) => ids = controls;

        // Card, MAP (strip + prompt), ROUTE (rows, steppers, 3 button rows), SAVED (rows, pager), then TASK/COMBAT/HELO (298).
        public float ContentHeight =>
            42f + (Head + 2f * Step + 16f + WmcUi.Gap) + (Head + DraftRows * ListRow + WmcUi.Gap + 4f * Step)
            + (Head + SavedRows * ListRow + WmcUi.Gap + Step) + 298f;

        public string Hint => last != null && !last.CanOrder ? (last.Client ? "Orders are host only for now." : "Not ready.")
            : "Orders go to " + (last != null ? last.ScopeLabel : "WING") + ". Form Up brings them back.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            var cardRect = new Rect(body.x, body.y, body.width, 34f);
            AvButton cardHit = WmcUi.Card(page, cardRect, null, out _, out Image rail);
            cardHit.SetEnabled(false);   // a read-out, not a control (review P1 m3)
            WmcUi.SetRail(rail, "info");
            card = Text(page, new Rect(body.x + 12f, body.y - 8f, body.width - 22f, 18f), "row-main");
            float y = body.y - 34f - WmcUi.Gap - 4f;

            y = WmcUi.Head(page, body, y, "MAP");
            float cw = (body.width - WmcUi.Gap * 3f) / 4f;
            for (int i = 0; i < Modes.Length; i++)
            {
                MapMode m = Modes[i];
                var r = new Rect(body.x + (i % 4) * (cw + WmcUi.Gap), y - (i / 4) * Step, cw, WmcUi.Row);
                modeButtons[i] = AvStyled.Button(page, r, MapOrders.Label(m), "btn", () => Arm(m), AvButtonStyle.Toggle);
                modeButtons[i].WithTooltip(ModeTips[i]);
                ids["orders.map." + MapOrders.Label(m).ToLowerInvariant()] = modeButtons[i];
            }
            y -= 2f * Step;
            prompt = Text(page, new Rect(body.x, y, body.width, 16f), "hint");
            y -= 16f + WmcUi.Gap;

            y = WmcUi.Head(page, body, y, "ROUTE");
            BuildRows(page, body, y, draftRoots, draftHits, draftRails, draftLabels, "orders.route.row", PickPoint);
            draftEmpty = Text(page, new Rect(body.x, y - 3f, body.width, 18f), "hint");
            draftEmpty.text = "Arm ROUTE, then right-click the map to add points.";
            y -= DraftRows * ListRow + WmcUi.Gap;
            float half = (body.width - WmcUi.Gap) / 2f;
            AvButton[] alt = AvKit.Stepper(page, body.x, y, half, out altValue, () => Edit(d => d.StepAltitude(-1)),
                () => Edit(d => d.StepAltitude(+1)), "Altitude of the selected point, or of new points (AUTO: the task's own).");
            AvButton[] spd = AvKit.Stepper(page, body.x + half + WmcUi.Gap, y, half, out spdValue, () => Edit(d => d.StepSpeed(-1)),
                () => Edit(d => d.StepSpeed(+1)), "Speed of the selected point, or of new points (AUTO: cruise).");
            ids["orders.route.alt-"] = alt[0];
            ids["orders.route.alt+"] = alt[1];
            ids["orders.route.spd-"] = spd[0];
            ids["orders.route.spd+"] = spd[1];
            y -= Step;
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.route.action", "ACTION", () => Edit(d => d.CycleAction()), "The selected point's arrival: orbit 2 min, land, cargo, none."),
                ("orders.route.del", "DELETE PT", () => Edit(d => d.RemoveSelected()), "Remove the selected point."),
                ("orders.route.skip", "SKIP LEG", Skip, "The scope's task goes on to its next point now."),
                ("orders.route.send", "SEND", Send, "Fly the draft: one point MOVE, more a ROUTE, LOOP/PING-PONG a patrol."),
                ("orders.route.undo", "UNDO", () => Edit(d => d.Undo()), "Remove the last point."),
                ("orders.route.clear", "CLEAR", () => Edit(d => d.Clear()), "Empty the draft."),
                ("orders.route.loop", "ONCE", () => Edit(d => d.CycleLoop()), "Fly the route once, loop it, or back and forth."),
                ("orders.route.save", "SAVE", Save, "Keep the draft as a saved route."));
            action = ids["orders.route.action"];
            del = ids["orders.route.del"];
            skip = ids["orders.route.skip"];
            send = ids["orders.route.send"];
            undo = ids["orders.route.undo"];
            clear = ids["orders.route.clear"];
            loop = ids["orders.route.loop"];
            save = ids["orders.route.save"];

            y = WmcUi.Head(page, body, y, "SAVED");
            BuildRows(page, body, y, savedRoots, savedHits, savedRails, savedLabels, "orders.saved.row", PickSaved);
            savedEmpty = Text(page, new Rect(body.x, y - 3f, body.width, 18f), "hint");
            savedEmpty.text = "No saved routes. SAVE keeps the draft.";
            y -= SavedRows * ListRow + WmcUi.Gap;
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.saved.prev", "< PREV", () => savedFirst = Math.Max(0, savedFirst - SavedRows), "Earlier saved routes."),
                ("orders.saved.next", "NEXT >", () => savedFirst += SavedRows, "More saved routes."),
                ("orders.saved.del", "DELETE", DeleteSaved, "Delete the loaded saved route (press twice)."));
            prev = ids["orders.saved.prev"];
            next = ids["orders.saved.next"];
            deleteSaved = ids["orders.saved.del"];

            y = WmcUi.Head(page, body, y, "TASK");
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.form", "FORM UP", () => O(() => WingCommands.FormUp(last.Scope)), "Back to formation on you."),
                ("orders.orbit", "ORBIT HERE", () => O(() => WingCommands.OrbitHere(last.Scope)), "Orbit over your position."),
                ("orders.hold", "HOLD HERE", () => O(() => WingCommands.HoldHere(last.Scope)), "Hold over your position."),
                ("orders.move", "MOVE AHEAD", () => O(() => WingCommands.MoveAhead(last.Scope)), "Fly to a point 10 km ahead, then orbit."),
                ("orders.patrol", "PATROL HERE", () => O(() => WingCommands.PatrolHere(last.Scope)), "A 20 km racetrack along your heading."),
                ("orders.rtb", "RTB", () => O(() => WingCommands.Rtb(last.Scope)), "Everyone home."),
                ("orders.refit", "REFIT", () => O(() => WingCommands.Refit(last.Scope)), "Home to refuel and rearm, then back out."));
            y = WmcUi.Head(page, body, y, "COMBAT");
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.engage", "ENGAGE", () => O(() => WingCommands.Engage(last.Scope)), "Fight the enemies in reach."),
                ("orders.attack", "ATTACK TGT", () => O(() => WingCommands.AttackTarget(last.Scope)), "Attack your selected targets."),
                ("orders.splash", "SPLASH", () => O(() => WingCommands.Splash(last.Scope)), "One missile at the nearest enemy aircraft."),
                ("orders.buddy", "BUDDY ATTACK", () => O(WingCommands.BuddyAttack), "The second element attacks your targets."),
                ("orders.bogey", "BOGEY DOPE", () => O(WingCommands.BogeyDope), "Nearest threat: bearing, range, altitude."),
                ("orders.breakoff", "BREAK OFF", () => O(() => WingCommands.Disengage(last.Scope)), "Stop fighting and rejoin."));
            y = WmcUi.Head(page, body, y, "HELO");
            WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.land", "LAND HERE", () => O(() => WingCommands.LandHere(last.Scope)), "Helicopters land near you."),
                ("orders.takeoff", "TAKE OFF", () => O(() => WingCommands.TakeOff(last.Scope)), "Landed helicopters lift off."),
                ("orders.cargo", "DELIVER CARGO", () => O(() => WingCommands.DeliverCargo(last.Scope)), "Helicopters drop their cargo here."),
                ("orders.rescue", "RESCUE", () => O(() => WingCommands.Rescue(last.Scope)), "A helicopter picks up a downed pilot."),
                ("orders.scout", "SCOUT AHEAD", () => O(() => WingCommands.ScoutAhead(last.Scope)), "A low route ahead, reporting contacts."));
            foreach (string id in new[] { "orders.form", "orders.orbit", "orders.hold", "orders.move", "orders.patrol", "orders.rtb",
                         "orders.refit", "orders.engage", "orders.attack", "orders.splash", "orders.buddy", "orders.bogey",
                         "orders.breakoff", "orders.land", "orders.takeoff", "orders.cargo", "orders.rescue", "orders.scout" })
                own.Add(ids[id]);
        }

        private static TMP_Text Text(RectTransform page, Rect r, string classes)
        {
            TMP_Text t = AvStyled.Label(page, r, "", classes);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>A list of row cards, each in its own container so card, rail, hit and label hide together.</summary>
        private void BuildRows(RectTransform page, Rect body, float y, GameObject[] roots, AvButton[] hits, Image[] rails,
            TMP_Text[] labels, string id, Action<int> click)
        {
            for (int i = 0; i < roots.Length; i++)
            {
                int k = i;
                var go = new GameObject(id + i, typeof(RectTransform));
                var row = (RectTransform)go.transform;
                row.SetParent(page, false);
                AvKit.Stretch(row);
                var r = new Rect(body.x, y - i * ListRow, body.width, ListRow - 2f);
                hits[i] = WmcUi.Card(row, r, () => click(k), out _, out rails[i]);
                ids[id + i] = hits[i];
                labels[i] = Text(row, new Rect(r.x + 10f, r.y - 3f, r.width - 16f, r.height - 4f), "row-sub");
                roots[i] = go;
                go.SetActive(false);
            }
        }

        private void O(Action order) => WmcUi.Order(last, order);

        private void Arm(MapMode m)
        {
            if (last == null) return;
            // The armed mode again turns it off.
            last.Map.Arm(last, m == last.Map.Mode ? MapMode.Off : m);
        }

        /// <summary>A draft edit: local, allowed on a client too; only SEND is an order.</summary>
        private void Edit(Action<RouteDraft> edit)
        {
            if (last != null) edit(last.Draft);
        }

        private void PickPoint(int row)
        {
            if (last != null) last.Draft.Select(draftFirst + row);
        }

        private void Send() => O(() =>
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

        private void Skip() => O(() => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, last.Scope)));

        private void Save()
        {
            if (last == null) return;
            if (WmcRoutes.Store.Save(last.Draft, out string name))
            {
                WmcRoutes.Save();
                WingToast.Show("Saved as " + name);
            }
            else WingToast.Show(last.Draft.Count == 0 ? "Nothing to save: add points first" : RouteStore.Max + " routes saved already: delete one");
        }

        private void PickSaved(int row)
        {
            if (last == null) return;
            int i = savedFirst + row;
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (i < 0 || i >= routes.Count) return;
            savedSelected = i;
            WmcRoutes.Store.Load(i, last.Draft);
            WingToast.Show("Loaded " + routes[i].Name + ": SEND to fly it");
        }

        private void DeleteSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (savedSelected < 0 || savedSelected >= routes.Count) return;
            string name = routes[savedSelected].Name;
            if (!deleteGate.Press(name, Time.unscaledTime))
            {
                WingToast.Show("Delete " + name + "? Press DELETE again");
                return;
            }
            WmcRoutes.Store.Remove(savedSelected);
            WmcRoutes.Save();
            savedSelected = -1;
            WingToast.Show(name + " deleted");
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            WingService w = c.Wing;
            WingPlanner p = w != null && w.Roster.InUse(c.ScopeElement) ? w.PlannerOf(c.ScopeElement) : null;
            string who = c.ScopeElement > 0 && w != null ? w.Roster.Name(c.ScopeElement) + ": " : "";
            card.text = c.Client ? "Task: the host's" : p == null ? "FORM · on you"
                : who + TaskCard.Text(p.Active ? p.Current : null, p.Leg, p.Active ? p.Lead.Position : Vec3.Zero, p.Active ? p.Lead.Speed : 0f);
            foreach (AvButton b in own) b.SetEnabled(c.CanOrder);

            MapMode mode = c.Map.Mode;
            for (int i = 0; i < Modes.Length; i++)
            {
                modeButtons[i].SetLatched(Modes[i] == mode);
                modeButtons[i].SetEnabled(Modes[i] == MapMode.Off || c.CanOrder);
            }
            if (mode != promptMode || !ReferenceEquals(c.ScopeLabel, promptScope))
            {
                promptMode = mode;
                promptScope = c.ScopeLabel;
                prompt.text = c.Map.Prompt(c.ScopeLabel)
                    ?? (c.Selection.Count > 0 ? "Right-click the map to MOVE " + c.ScopeLabel + "." : "Arm a mode, then right-click the map.");
            }

            RefreshDraft(c, p);
            RefreshSaved();
        }

        private void RefreshDraft(WmcContext c, WingPlanner p)
        {
            RouteDraft d = c.Draft;
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
                Waypoint w = d[i];
                RouteLeg l = draftLegs[i];
                long key = RowKey(i, l, w, i == d.Selected);
                if (key == draftKeys[r]) continue;
                draftKeys[r] = key;
                draftLabels[r].text = RouteView.Label(l) + " · " + RouteDraft.AltitudeText(w.Altitude) + " · "
                    + RouteDraft.SpeedText(w.Speed) + " · " + RouteDraft.ActionText(w);
                WmcUi.SetRail(draftRails[r], i == d.Selected ? "armed" : "info");
            }
            draftEmpty.gameObject.SetActive(d.Count == 0);
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
            bool any = d.Count > 0, picked = d.Selected >= 0;
            action.SetEnabled(picked);
            del.SetEnabled(picked);
            send.SetEnabled(any && c.CanOrder);
            undo.SetEnabled(any);
            clear.SetEnabled(any);
            save.SetEnabled(any);
            skip.SetEnabled(c.CanOrder && p != null && p.Active && p.Leg >= 0);
        }

        private void RefreshSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (savedFirst >= routes.Count) savedFirst = Math.Max(0, (routes.Count - 1) / SavedRows * SavedRows);
            if (savedSelected >= routes.Count) savedSelected = -1;
            for (int r = 0; r < SavedRows; r++)
            {
                int i = savedFirst + r;
                bool on = i < routes.Count;
                if (savedRoots[r].activeSelf != on) savedRoots[r].SetActive(on);
                if (!on)
                {
                    savedKeys[r] = long.MinValue;
                    continue;
                }
                SavedRoute s = routes[i];
                long key = ((long)s.GetHashCode() << 8) | (i == savedSelected ? 1L : 0L);
                if (key == savedKeys[r]) continue;
                savedKeys[r] = key;
                savedLabels[r].text = s.Name + " · " + s.Points.Length + (s.Points.Length == 1 ? " point" : " points") + " · " + RouteDraft.LoopText(s.Loop);
                WmcUi.SetRail(savedRails[r], i == savedSelected ? "armed" : "info");
            }
            savedEmpty.gameObject.SetActive(routes.Count == 0);
            prev.SetEnabled(savedFirst > 0);
            next.SetEnabled(savedFirst + SavedRows < routes.Count);
            deleteSaved.SetEnabled(savedSelected >= 0);
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
