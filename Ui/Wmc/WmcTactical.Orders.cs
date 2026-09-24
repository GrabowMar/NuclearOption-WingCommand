using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>TACTICAL › ORDERS (spec WMC rebuild §TACTICAL): the cue banner (the armed order and what to click, with HERE
    /// and CANCEL; otherwise the top alert), the PROFILE picker and the four fast toggles, one 4×4 order grid in labelled rows
    /// whose point and target orders latch and arm the map, then the situation.</summary>
    internal sealed partial class WmcTactical
    {
        private const float Banner = 24f, KeyWidth = 64f, ToggleH = 22f, TogglePitch = 24f, GridH = 26f, GridPitch = 29f;
        private const float BannerTop = BezelLayout.SubTabs + BezelLayout.SubGap, ScrollTop = BannerTop + Banner + 4f;
        private const string Later = " Arrives with the weapons & EMCON update.";

        private static readonly string[] TargetLabels = { "HOLD FIRE", "AIR", "GROUND", "BOTH", "COVER" };
        private static readonly string[] TargetKeys = { "hold", "air", "ground", "both", "cover" };
        private static readonly string[] TargetTips =
        {
            "Hold fire: shoot only when ordered.",
            "Shoot at enemy aircraft in reach on their own.",
            "Shoot at ground targets in reach on their own.",
            "Shoot at air and ground targets in reach on their own.",
            "Cover: take the one air threat nearest the protected aircraft.",
        };

        private WmcScroll ordersScroll;
        private Image bannerRail;
        private TMP_Text bannerText;
        private AvButton bannerHit, here, cancel, profileButton;
        private SegmentRow targets, weapons, radar, guard;
        private readonly AvButton[] grid = new AvButton[OrderGrid.Rows * OrderGrid.Columns];
        private readonly GridCell[] shownCells = new GridCell[OrderGrid.Rows * OrderGrid.Columns];
        private readonly string[] shownTips = new string[OrderGrid.Rows * OrderGrid.Columns];
        private readonly bool[] shownEnabled = new bool[OrderGrid.Rows * OrderGrid.Columns];
        private readonly List<AvKit.PopupEntry> popupEntries = new List<AvKit.PopupEntry>(3);
        private static readonly WingDoctrine[] Profiles = { WingDoctrine.Reserve, WingDoctrine.Escort, WingDoctrine.Sweep };
        private AvKit.Popup profilePopup;
        private int bannerKey = int.MinValue;
        private uint bannerAlertId;
        private string profileShown;
        private bool helosShown, gridBuilt;

        private void BuildOrders(RectTransform root)
        {
            // Pinned cue banner.
            var banner = new Rect(x, -BannerTop, width, Banner);
            bannerHit = WmcUi.Card(root, banner, BannerClick, out _, out bannerRail);
            bannerText = WmcKit.Text(root, new Rect(x + 10f, -BannerTop - 2f, width - 150f, Banner - 4f), "row-name");
            here = AvStyled.Button(root, new Rect(x + width - 128f, -BannerTop - 2f, 58f, Banner - 4f), "HERE", "btn", Here);
            here.WithTooltip("Orbit or hold where the scope is now.");
            ids["tac.orders.here"] = here;
            cancel = AvStyled.Button(root, new Rect(x + width - 66f, -BannerTop - 2f, 64f, Banner - 4f), "CANCEL", "btn",
                () => last?.Map.Disarm());
            cancel.WithTooltip("Disarm the map order (Esc does too).");
            ids["tac.orders.cancel"] = cancel;

            ordersScroll = WmcScroll.Build(root, new Rect(x, -ScrollTop, width + 8f, 100f), "OrdersScroll");
            RectTransform s = ordersScroll.Content;
            float w = ordersScroll.Width, y = 0f;

            AvStyled.Label(s, new Rect(0f, y, KeyWidth, 24f), "PROFILE", "metric-key");
            profileButton = AvStyled.Button(s, new Rect(KeyWidth, y, 186f, 24f), "RESERVE ▾", "btn", OpenProfiles);
            profileButton.WithTooltip("The behaviour the scope flies with: RESERVE holds fire in close formation, ESCORT covers you, SWEEP hunts wide.");
            ids["tac.orders.profile"] = profileButton;
            AvButton tune = AvStyled.Button(s, new Rect(KeyWidth + 190f, y, w - KeyWidth - 190f, 24f), "FINE-TUNE › BEHAVIOUR", "btn", null);
            tune.SetEnabled(false);
            tune.WithTooltip("Behaviour profiles, tuning and reaction rules in the planning room. Arrives in a later update.");
            ids["tac.orders.tune"] = tune;
            y -= 28f;

            targets = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "TARGETS", TargetLabels, TargetTips,
                "tac.orders.targets.", TargetKeys, ids, PickTargets);
            y -= TogglePitch;
            weapons = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "WEAPONS", new[] { "AUTO", "MISSILES", "GUNS", "NO A-G" },
                null, "tac.orders.weapons.", new[] { "auto", "missiles", "guns", "noag" }, ids, null);
            weapons.SetEnabled(false, "Which weapons the scope may use." + Later);
            y -= TogglePitch;
            radar = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "RADAR", new[] { "ON", "SILENT", "OFF" },
                null, "tac.orders.radar.", new[] { "on", "silent", "off" }, ids, null);
            radar.SetEnabled(false, "Radar on, silent until engaged, or off." + Later);
            y -= TogglePitch;
            guard = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, "MSL GUARD", new[] { "OFF", "SELF", "WING", "LEAD" },
                null, "tac.orders.guard.", new[] { "off", "self", "wing", "lead" }, ids, null);
            guard.SetEnabled(false, "Shoot down missiles aimed at self, the wing or the lead. Arrives with the area orders update.");
            y -= TogglePitch + 4f;

            float cw = (w - KeyWidth - WmcUi.Gap * (OrderGrid.Columns - 1)) / OrderGrid.Columns;
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                AvStyled.Label(s, new Rect(0f, y - r * GridPitch, KeyWidth, GridH), OrderGrid.RowLabels[r], "metric-key");
                for (int col = 0; col < OrderGrid.Columns; col++)
                {
                    int k = r * OrderGrid.Columns + col;
                    grid[k] = AvStyled.Button(s, new Rect(KeyWidth + col * (cw + WmcUi.Gap), y - r * GridPitch, cw, GridH), "", "btn",
                        () => PressCell(k), AvButtonStyle.Toggle);
                }
            }
            SetGrid(false);
            situationTop = y - OrderGrid.Rows * GridPitch - 6f;
            BuildSituation(s, w);
            profilePopup = new AvKit.Popup(page, panelWidth);
        }

        /// <summary>The sub-page region is <paramref name="region"/> px tall (below the sub-tab strip's top).</summary>
        private void LayoutOrders(float region)
        {
            if (ordersScroll == null) return;
            ordersScroll.SetViewport(new Rect(x, -ScrollTop, width + 8f, Mathf.Max(40f, region - ScrollTop)));
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
                // The latched order again disarms it (the banner's CANCEL does too).
                if (last.Map.Mode == cell.Map) last.Map.Disarm();
                else last.Map.Arm(last, cell.Map);
                return;
            }
            WmcUi.Order(last, () =>
            {
                WingScope scope = last.Scope;
                switch (cell.Order)
                {
                    case GridOrder.Splash: WingCommands.Splash(scope); break;
                    case GridOrder.Engage: WingCommands.Engage(scope); break;
                    case GridOrder.Scout: WingCommands.ScoutAhead(scope); break;
                    case GridOrder.Break: WingCommands.Disengage(scope); break;
                    case GridOrder.FormUp: WingCommands.FormUp(scope); break;
                    case GridOrder.Detach: Detach(); break;
                    case GridOrder.Rtb: WingCommands.Rtb(scope); break;
                    case GridOrder.Refit: WingCommands.Refit(scope); break;
                    case GridOrder.TakeOff: WingCommands.TakeOff(scope); break;
                    case GridOrder.Rescue: WingCommands.Rescue(scope); break;
                }
            });
        }

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

        private void OpenProfiles()
        {
            if (last == null || last.Wing == null || last.Client) return;
            string current = last.Wing.DoctrineOf(last.ScopeElement).PatternName;
            popupEntries.Clear();
            foreach (WingDoctrine d in Profiles)
                popupEntries.Add(new AvKit.PopupEntry(d.PatternName, null, d.PatternName == current));
            profilePopup.Show(RectIn(page, (RectTransform)profileButton.transform), popupEntries,
                i => WmcUi.Order(last, () => SetDoctrine(Profiles[i])));
        }

        private void PickTargets(int i)
        {
            if (last == null || last.Wing == null || last.Client) return;
            WingDoctrine d = last.Wing.DoctrineOf(last.ScopeElement);
            WmcUi.Order(last, () => SetDoctrine(new WingDoctrine(d.Guard, d.Response, d.Interval, d.SpreadWhenThreatened, (TargetPolicy)i, d.Reach)));
        }

        private void SetDoctrine(WingDoctrine d) =>
            WingOrders.Run(new WingOrder { Kind = OrderKind.SetDoctrine, Text = d.ToString(), Scope = last.Scope });

        /// <summary><paramref name="target"/>'s rectangle in <paramref name="root"/>'s top-left coordinates (y down negative),
        /// for a popup parented to the page while its button sits in a scroll view.</summary>
        private static Rect RectIn(RectTransform root, RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 tl = root.InverseTransformPoint(corners[1]);
            Rect r = root.rect;
            return new Rect(tl.x - r.xMin, tl.y - r.yMax, target.rect.width, target.rect.height);
        }

        private void RefreshOrders(WmcContext c)
        {
            RefreshBanner(c);
            bool host = c.Wing != null && !c.Client;
            WingDoctrine d = host ? c.Wing.DoctrineOf(c.ScopeElement) : WingDoctrine.Reserve;
            string pattern = host ? d.PatternName : WmcText.Unknown;
            if (!ReferenceEquals(pattern, profileShown) && pattern != profileShown)
            {
                profileShown = pattern;
                profileButton.SetText(pattern + " ▾");
            }
            profileButton.SetEnabled(c.CanOrder);
            targets.Set(host ? (int)d.Targets : -1);
            targets.SetEnabled(c.CanOrder, c.Client ? "Orders are host only for now" : "Wing Command is not ready");

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
                grid[k].SetLatched(cell.Map != MapMode.Off && c.Map.Mode == cell.Map);
            }
            RefreshSituation(c);
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
