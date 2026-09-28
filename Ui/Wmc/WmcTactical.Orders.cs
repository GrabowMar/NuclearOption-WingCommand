using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL's controls (spec bezel v2 §5 TACTICAL): the cue row (the armed order and what to click, with HERE and
    /// CANCEL; otherwise the top alert), the 4×4 order grid in labelled rows with a category rail each, whose point and target orders latch and arm the
    /// map, and REACT: the five maneuvers.</summary>
    internal sealed partial class WmcTactical
    {
        private const float KeyWidth = 64f;

        private RectTransform cueRoot, gridRoot;
        private Image bannerRail;
        private TMP_Text bannerText;
        private AvButton bannerHit, here, cancel;
        private readonly AvButton[] grid = new AvButton[OrderGrid.Rows * OrderGrid.Columns];
        private readonly AvButton[] react = new AvButton[5];
        private readonly GridCell[] shownCells = new GridCell[OrderGrid.Rows * OrderGrid.Columns];
        private readonly string[] shownTips = new string[OrderGrid.Rows * OrderGrid.Columns];
        private readonly bool[] shownEnabled = new bool[OrderGrid.Rows * OrderGrid.Columns];
        private int bannerKey = int.MinValue;
        private uint bannerAlertId;
        private string reactWhy;
        private bool helosShown, gridBuilt, reactOn = true;

        // ---------------------------------------------------------------- the cue row

        private void BuildCue()
        {
            cueRoot = Container(page, "TacticalCue", new Rect(x, listTop, width, BezelLayout.Cue));
            float h = BezelLayout.Cue;
            bannerHit = WmcUi.Card(cueRoot, new Rect(0f, 0f, width, h), BannerClick, out _, out bannerRail);
            bannerText = WmcKit.Text(cueRoot, new Rect(10f, -2f, width - 150f, h - 4f), "row-name");
            here = AvStyled.Button(cueRoot, new Rect(width - 128f, -2f, 58f, h - 4f), "HERE", "btn", Here);
            here.WithTooltip("Orbit or hold where the scope is now.");
            ids["tac.orders.here"] = here;
            cancel = AvStyled.Button(cueRoot, new Rect(width - 66f, -2f, 64f, h - 4f), "CANCEL", "btn", () => last?.Map.Disarm());
            cancel.WithTooltip("Disarm the map order (Esc does too).");
            ids["tac.orders.cancel"] = cancel;
        }

        // ---------------------------------------------------------------- the grid and REACT

        private void BuildGrid()
        {
            gridRoot = Container(page, "TacticalGrid", new Rect(x, listTop, width, BezelLayout.GridBlock));
            RectTransform s = gridRoot;
            float cw = (width - KeyWidth - WmcUi.Gap * (OrderGrid.Columns - 1)) / OrderGrid.Columns, h = BezelLayout.GridCell;
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                float y = -r * BezelLayout.GridPitch;
                AvStyled.Rail(s, new Rect(0f, y, 3f, h), OrderGrid.RowRail(r));
                AvStyled.Label(s, new Rect(7f, y, KeyWidth - 7f, h), OrderGrid.RowLabels[r], "metric-key");
                for (int col = 0; col < OrderGrid.Columns; col++)
                {
                    int k = r * OrderGrid.Columns + col;
                    grid[k] = AvStyled.Button(s, new Rect(KeyWidth + col * (cw + WmcUi.Gap), y, cw, h), "", "btn",
                        () => PressCell(k), AvButtonStyle.Toggle);
                }
            }
            SetGrid(false);
            float ry = -OrderGrid.Rows * BezelLayout.GridPitch;
            AvStyled.Rail(s, new Rect(0f, ry, 3f, h), OrderGrid.ReactRail);
            AvStyled.Label(s, new Rect(7f, ry, KeyWidth - 7f, h), OrderGrid.ReactLabel, "metric-key");
            float rw = (width - KeyWidth - WmcUi.Gap * (react.Length - 1)) / react.Length;
            for (int i = 0; i < react.Length; i++)
            {
                int k = i;
                GridCell cell = OrderGrid.React[i];
                react[i] = AvStyled.Button(s, new Rect(KeyWidth + i * (rw + WmcUi.Gap), ry, rw, h), cell.Label, "btn", () => PressReact(k));
                react[i].WithTooltip(cell.Tip);
                ids[cell.Id] = react[i];
            }
        }

        /// <summary>The grid's cells for jets or, with helicopters in scope, the helo swap (spec: SWEEP → SCOUT, SUPPORT → TAKE
        /// OFF · RESCUE · LAND · CARGO). An id always names the button showing it.</summary>
        private void SetGrid(bool helos)
        {
            if (gridBuilt && helos == helosShown) return;
            gridBuilt = true;
            helosShown = helos;
            for (int r = 0; r < OrderGrid.Rows; r++)
                for (int col = 0; col < OrderGrid.Columns; col++)
                {
                    int k = r * OrderGrid.Columns + col;
                    GridCell cell = OrderGrid.At(r, col, helos);
                    if (shownCells[k].Id == cell.Id) continue;
                    if (shownCells[k].Id != null && ids.TryGetValue(shownCells[k].Id, out AvButton old) && old == grid[k]) ids.Remove(shownCells[k].Id);
                    shownCells[k] = cell;
                    shownTips[k] = null;
                    grid[k].SetText(cell.Label);
                    ids[cell.Id] = grid[k];
                }
        }

        private void PressCell(int k)
        {
            if (last == null) return;
            GridCell cell = shownCells[k];
            if (!cell.Built) return;
            if (cell.Map != MapMode.Off)
            {
                // The latched order again disarms it (the cue's CANCEL does too).
                if (last.Map.Mode == cell.Map) last.Map.Disarm();
                else last.Map.Arm(last, cell.Map);
                return;
            }
            WmcUi.Order(last, () =>
            {
                WingScope scoped = last.Scope;
                switch (cell.Order)
                {
                    case GridOrder.Splash: WingCommands.Splash(scoped); break;
                    case GridOrder.Engage: WingCommands.Engage(scoped); break;
                    case GridOrder.Scout: WingCommands.ScoutAhead(scoped); break;
                    case GridOrder.Break: WingCommands.Disengage(scoped); break;
                    case GridOrder.FormUp: WingCommands.FormUp(scoped); break;
                    case GridOrder.Detach: Detach(); break;
                    case GridOrder.Rtb: WingCommands.Rtb(scoped); break;
                    case GridOrder.Refit: WingCommands.Refit(scoped); break;
                    case GridOrder.TakeOff: WingCommands.TakeOff(scoped); break;
                    case GridOrder.Rescue: WingCommands.Rescue(scoped); break;
                    case GridOrder.Ecm: WingOrders.Run(WingOrder.Of(OrderKind.Ecm, scoped)); break;
                }
            });
        }

        /// <summary>A maneuver is a one-shot order: flown through the pipeline, then back to the slot.</summary>
        private void PressReact(int i) =>
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.Maneuver, Number = OrderGrid.React[i].Number, Scope = last.Scope }));

        /// <summary>The selected wingmen orbit the point they are over now, as their own element.</summary>
        private void Detach()
        {
            if (last.Scope.Kind == ScopeKind.Wing)
            {
                WingToast.Show("Select the wingmen to detach");
                return;
            }
            if (!Centroid(out Vec3 c, out _)) return;
            Waypoint at = Waypoint.At(c.X, c.Z);
            at.Altitude = c.Y;
            WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(at), last.Scope));
        }

        /// <summary>HERE: the armed ORBIT or HOLD, at the scope's own position rather than a clicked point.</summary>
        private void Here()
        {
            if (last == null) return;
            MapMode mode = last.Map.Mode;
            if (mode != MapMode.Orbit && mode != MapMode.Hold) return;
            WmcUi.Order(last, () =>
            {
                if (!Centroid(out Vec3 c, out Vec3 v)) return;
                Waypoint at = Waypoint.At(c.X, c.Z);
                at.Altitude = c.Y;
                WingTask task = mode == MapMode.Hold ? WingTask.Hold(at, Vec3.HeadingDeg(v)) : WingTask.Orbit(at);
                if (WingOrders.Run(WingOrder.Tasked(task, last.Scope)).Accepted) last.Map.Disarm();
            });
        }

        /// <summary>The scope's members' mean position and velocity (host).</summary>
        private bool Centroid(out Vec3 pos, out Vec3 vel)
        {
            pos = vel = Vec3.Zero;
            if (last?.Wing == null) return false;
            int n = 0;
            foreach (WingMember m in last.Wing.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                uint id = m.Aircraft.persistentID.Id;
                int i = WingRows.IndexOf(last.Rows, last.Count, id);
                if (i < 0 || !InScope(last, last.Rows[i])) continue;
                pos += m.Last.Pos;
                vel += m.Last.Vel;
                n++;
            }
            if (n == 0) return false;
            pos *= 1f / n;
            vel *= 1f / n;
            return true;
        }

        private void BannerClick()
        {
            if (last == null || last.Map.Mode != MapMode.Off || bannerAlertId == 0u) return;
            FocusAircraft(bannerAlertId);
        }

        private void FocusAircraft(uint id)
        {
            WmcMap.Center(WmcContext.UnitOf(id));
            last.Selection.SelectOnly(id);
            last.Rescope();
        }

        private void RefreshOrders(WmcContext c)
        {
            RefreshBanner(c);
            SetGrid(HelosInScope(c));
            bool scoped = c.Scope.Kind != ScopeKind.Wing;
            for (int k = 0; k < grid.Length; k++)
            {
                GridCell cell = shownCells[k];
                string why = OrderGrid.Why(cell, c.CanOrder, c.Count, scoped);
                bool on = why == null;
                string tip = why ?? cell.Tip;
                if (on != shownEnabled[k] || !ReferenceEquals(tip, shownTips[k]))
                {
                    shownEnabled[k] = on;
                    shownTips[k] = tip;
                    grid[k].SetEnabled(on);
                    grid[k].WithTooltip(tip);
                }
                grid[k].SetLatched(cell.Map != MapMode.Off ? c.Map.Mode == cell.Map : cell.Order == GridOrder.Ecm && JammingInScope(c));
            }
            RefreshReact(c);
        }

        private void RefreshReact(WmcContext c)
        {
            bool flying = false;
            for (int i = 0; i < c.Count && !flying; i++)
                flying = c.InScope(c.Rows[i]) && (MemberDuty)c.Rows[i].Duty == MemberDuty.Formation;
            bool on = c.CanOrder && flying;
            string why = !c.CanOrder ? "Orders are host only for now" : "Nobody in scope is flying in formation";
            if (on == reactOn && (on || ReferenceEquals(why, reactWhy))) return;
            reactOn = on;
            reactWhy = why;
            for (int i = 0; i < react.Length; i++)
            {
                react[i].SetEnabled(on);
                react[i].WithTooltip(on ? OrderGrid.React[i].Tip : why);
            }
        }

        private void RefreshBanner(WmcContext c)
        {
            MapMode mode = c.Map.Mode;
            bool armed = mode != MapMode.Off;
            uint alertId = !armed && alertCount > 0 ? alerts[0].Id : 0u;
            int key = armed ? 1000 + (int)mode : alertId != 0u ? 2000 + (int)alerts[0].Kind * 97 + (int)(alertId % 997u)
                : 3000 + (c.ScopeLabel?.GetHashCode() ?? 0) % 997;
            here.gameObject.SetActive(armed && (mode == MapMode.Orbit || mode == MapMode.Hold));
            cancel.gameObject.SetActive(armed);
            if (key == bannerKey) return;
            bannerKey = key;
            bannerAlertId = alertId;
            if (armed)
            {
                bannerText.text = WmcWords.Banner(mode);
                WmcUi.SetRail(bannerRail, "armed");
            }
            else if (alertId != 0u)
            {
                bannerText.text = AlertLine(alerts[0], c);
                WmcUi.SetRail(bannerRail, alerts[0].Kind <= AlertKind.Damaged ? "danger" : "armed");
            }
            else
            {
                bannerText.text = "Orders go to " + c.ScopeLabel + ".";
                WmcUi.SetRail(bannerRail, "info");
            }
            bannerHit.SetEnabled(!armed && alertId != 0u);
        }

        /// <summary>A member in scope jams now (ECM stays lit while one does); no allocation.</summary>
        private static bool JammingInScope(WmcContext c)
        {
            if (c.Wing == null || c.Client) return false;
            foreach (WingMember m in c.Wing.Members)
            {
                if (float.IsNaN(m.JamUntil) || (object)m.Aircraft == null) continue;
                int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                if (i >= 0 && InScope(c, c.Rows[i])) return true;
            }
            return false;
        }

        /// <summary>Any scoped member a helicopter or tiltwing (host; a client's rows carry no airframe class).</summary>
        private static bool HelosInScope(WmcContext c)
        {
            if (c.Wing == null || c.Client) return false;
            foreach (WingMember m in c.Wing.Members)
            {
                if (m.Released || (object)m.Aircraft == null || m.Profile == null || m.Profile.Class == AirframeClass.FixedWing) continue;
                int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                if (i >= 0 && InScope(c, c.Rows[i])) return true;
            }
            return false;
        }
    }
}
