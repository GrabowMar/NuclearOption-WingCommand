using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // LOADOUT's HARDPOINTS table: one row per station (a pair counts once and says ×n), the store and its mass, CLEAR only when a store
    // is fitted, a blocked station naming what blocks it, and the store popup beside its row.
    internal sealed partial class WmcLoadout
    {
        private sealed class RowView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Fill, Rail;
            public TMP_Text Station, Store, Mass;
            public AvButton Hit, Clear;
        }

        private readonly RowView[] rows = new RowView[BezelLayout.HpRowsMax];
        private int rowsPerPage, hpPage, popupStation = -1;
        private TMP_Text hpNote, hpEmpty;
        private GameObject hpPagerRoot;
        private WmcPager hpPager;
        private readonly List<WingLoadoutCatalog.StoreOption> stores = new List<WingLoadoutCatalog.StoreOption>();
        private readonly List<string> storeKeys = new List<string>();
        private readonly List<AvKit.PopupEntry> storeEntries = new List<AvKit.PopupEntry>();
        private float contentShown = -1f;

        private void BuildHardpoints(RectTransform r, float y)
        {
            AvStyled.Label(r, new Rect(0f, y, 150f, BezelLayout.SectionHead), "HARDPOINTS", "section-title");
            hpNote = WmcKit.Text(r, new Rect(150f, y, width - 150f, BezelLayout.SectionHead), "row-sub", TextAlignmentOptions.MidlineRight);
            float hy = y - BezelLayout.SectionHead - BezelLayout.HeadGap;
            AvStyled.Label(r, new Rect(BezelLayout.ColStation, hy, BezelLayout.ColStationW, BezelLayout.ColumnHead), "STATION", "metric-key");
            AvStyled.Label(r, new Rect(BezelLayout.ColStore, hy, BezelLayout.ColStoreW, BezelLayout.ColumnHead), "STORE", "metric-key");
            AvStyled.Label(r, new Rect(BezelLayout.ColMass, hy, BezelLayout.ColMassW, BezelLayout.ColumnHead), "MASS", "metric-key",
                align: TextAlignmentOptions.MidlineRight);
            float ry = y - BezelLayout.HardpointHead;
            hpEmpty = AvStyled.Label(r, new Rect(8f, ry, width - 16f, BezelLayout.HpRowH), "", "row-sub");
            for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(r, new Rect(0f, ry - i * BezelLayout.HpPitch, width, BezelLayout.HpRowH), i);
            var go = new GameObject("HardpointPager", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(r, false);
            AvKit.Place(rt, new Rect(0f, ry - rowsPerPage * BezelLayout.HpPitch, width, BezelLayout.HpPager));
            hpPager = WmcPager.Build(rt, new Rect(0f, 0f, width, BezelLayout.HpPager), "lo.hp.", ids, TurnRows);
            hpPagerRoot = go;
            go.SetActive(false);
        }

        private RowView BuildRow(RectTransform parent, Rect r, int i)
        {
            var go = new GameObject("Hardpoint" + i, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            var v = new RowView { Root = go, Rect = rt };
            v.Fill = AvStyled.Box(rt, new Rect(0f, 0f, r.width, r.height), "row");
            if (v.Fill != null) v.Fill.raycastTarget = false;
            v.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, r.height), "inert");
            v.Station = AvStyled.Label(rt, new Rect(BezelLayout.ColStation, -4f, BezelLayout.ColStationW, r.height - 8f), "", "row-name");
            v.Store = AvStyled.Label(rt, new Rect(BezelLayout.ColStore, -4f, BezelLayout.ColStoreW, r.height - 8f), "", "row-sub");
            v.Mass = WmcKit.Text(rt, new Rect(BezelLayout.ColMass, 0f, BezelLayout.ColMassW, r.height), "row-sub", TextAlignmentOptions.MidlineRight);
            v.Hit = AvKit.HitButton(rt, new Rect(0f, 0f, BezelLayout.ColVerb - 4f, r.height), () => OpenStores(i));
            if (v.Fill != null) v.Hit.SetRowHighlight(v.Fill, WmcUi.RowColor("rest"), WmcUi.RowColor("hover"));
            ids["lo.hp" + i] = v.Hit;
            v.Clear = AvStyled.Button(rt, new Rect(BezelLayout.ColVerb, -9f, BezelLayout.ColVerbW, 24f), "CLEAR", "btn", () => ClearStation(i));
            v.Clear.WithTooltip("Empty this station.");
            ids["lo.hp" + i + ".clear"] = v.Clear;
            go.SetActive(false);
            return v;
        }

        /// <summary>This page of stations in words; the head counts stations once, as the metric does.</summary>
        private void RefreshHardpoints()
        {
            int stations = layout != null ? layout.Stations : 0;
            bool paged = stations > rowsPerPage;
            hpPage = Pages.Clamp(hpPage, stations, rowsPerPage);
            if (hpPagerRoot.activeSelf != paged) hpPagerRoot.SetActive(paged);
            hpPager.Set(hpPage, Pages.Count(stations, rowsPerPage));
            float height = BezelLayout.LoadoutContent(rowsPerPage, paged);
            if (Mathf.Abs(height - contentShown) > 0.5f)
            {
                contentShown = height;
                scroll.SetContentHeight(height);
            }
            WmcKit.Set(hpNote, layout != null ? LoadoutWords.HardpointsNote(stations, summary.Blocked) : "");
            string empty = airframe == null ? "Pick an airframe above." : layout == null ? LoadoutWords.Unreadable
                : current == null ? "NO TEMPLATE · NEW starts one for this airframe" : null;
            hpEmpty.gameObject.SetActive(empty != null);
            if (empty != null) WmcKit.Set(hpEmpty, empty);
            int first = Pages.First(hpPage, rowsPerPage);
            for (int i = 0; i < rows.Length; i++)
            {
                RowView v = rows[i];
                int st = first + i;
                bool on = empty == null && i < rowsPerPage && st < stations;
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                if (on) BindRow(v, st);
            }
        }

        private void BindRow(RowView v, int st)
        {
            int set = layout.First(st), pylons = layout.PylonsOf(st);
            WingLoadoutCatalog.StoreOption o = setOptions[set];
            WmcKit.Set(v.Station, LoadoutWords.StationName(layout.Name(st), pylons));
            int blockedBy = layout.BlockedBy(KnownKeys(), st);
            bool fitted = !o.IsEmpty;
            StoreVerdict verdict = StoreVerdict.Ok;
            if (fitted)
            {
                MountFacts f = WingLoadoutCatalog.FactsOf(o, pylons, hq);
                f.Blocked = set < clearedSets.Length && clearedSets[set];
                verdict = StoreRules.Check(f, mission);
                WmcKit.Set(v.Store, StoreWords.Row(o.Known ? o.Label : null, verdict, RankFor(o)));
                WmcKit.Set(v.Mass, StoreRules.Flies(verdict) ? LoadoutWords.RowMass(o.Mass * pylons) : WmcText.Unknown);
            }
            else
            {
                WmcKit.Set(v.Store, blockedBy >= 0 ? StoreWords.BlockedBy(layout.Name(blockedBy)) : LoadoutWords.EmptyStore);
                WmcKit.Set(v.Mass, WmcText.Unknown);
            }
            bool flies = fitted && StoreRules.Flies(verdict);
            WmcUi.SetRail(v.Rail, flies ? "live" : fitted ? "warn" : "inert");
            // An empty station another store blocks takes nothing; a fitted one always opens (to change it) and says why it will not fly.
            bool can = fitted || blockedBy < 0;
            v.Hit.SetEnabled(can);
            v.Hit.WithTooltip(!can ? StoreWords.Why(StoreVerdict.Blocked, 0) : fitted && !flies ? StoreWords.Why(verdict, RankFor(o))
                : "Pick the store for " + layout.Name(st) + ".");
            v.Clear.gameObject.SetActive(fitted);
        }

        /// <summary>The keys with unknown stores left out (they never reach the launch, so they block nothing).</summary>
        private List<string> KnownKeys()
        {
            knownKeys.Clear();
            for (int s = 0; s < keys.Count; s++) knownKeys.Add(s < setFacts.Length && setFacts[s].Known ? keys[s] : null);
            return knownKeys;
        }

        private readonly List<string> knownKeys = new List<string>();

        private void TurnRows(int dir)
        {
            hpPage = Pages.Clamp(hpPage + dir, layout != null ? layout.Stations : 0, rowsPerPage);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the store popup

        /// <summary>The stores station <paramref name="row"/> can carry: those that fly first, nuclear ones the mission holds back next
        /// (pickable, they launch empty until they clear), restricted ones last and disabled; the popup beside its row.</summary>
        private void OpenStores(int row)
        {
            if (current == null || layout == null) return;
            int st = Pages.First(hpPage, rowsPerPage) + row;
            if (st >= layout.Stations) return;
            int set = layout.First(st), pylons = layout.PylonsOf(st);
            WingLoadoutCatalog.StoresFor(airframe, set, stores);
            storeEntries.Clear();
            storeKeys.Clear();
            string key = layout.KeyOf(keys, st);
            for (int pass = 0; pass < 3; pass++)
                foreach (WingLoadoutCatalog.StoreOption o in stores)
                {
                    StoreVerdict v = StoreRules.Check(WingLoadoutCatalog.FactsOf(o, pylons, hq), mission);
                    if (!StoreRules.Offered(v)) continue;
                    int group = v == StoreVerdict.Ok ? 0 : StoreRules.Pickable(v) ? 1 : 2;
                    if (group != pass) continue;
                    storeEntries.Add(new AvKit.PopupEntry(WmcText.Cut(o.Label, LoadoutWords.StoreChars),
                        StoreWords.Detail(v, o.Kind, o.Ammo, RankFor(o)), o.Key == key, StoreRules.Pickable(v)));
                    storeKeys.Add(o.Key);
                }
            if (storeEntries.Count == 0)
            {
                WingToast.Show("No store for " + layout.Name(st) + " is allowed here");
                return;
            }
            popupStation = st;
            popup.Show(WmcKit.PopupArea(page, body, rows[row].Rect, storeEntries.Count, scroll, width), storeEntries, PickStore);
            WmcKit.FitAll(page);
        }

        private void PickStore(int i)
        {
            if (current == null || layout == null || popupStation < 0 || i < 0 || i >= storeKeys.Count) return;
            int emptied = layout.Pick(keys, popupStation, storeKeys[i]);
            if (emptied < 0)
            {
                WingToast.Show(StoreWords.Why(StoreVerdict.Blocked, 0));
                return;
            }
            WingLoadoutTemplates.SetMounts(current, keys);
            if (emptied > 0) WingToast.Show(emptied == 1 ? "1 station emptied: the new store blocks it" : emptied + " stations emptied: the new store blocks them");
            WmcPanel.Instance?.Refresh();
        }

        private void ClearStation(int row)
        {
            if (current == null || layout == null) return;
            int st = Pages.First(hpPage, rowsPerPage) + row;
            if (st >= layout.Stations) return;
            layout.Clear(keys, st);
            WingLoadoutTemplates.SetMounts(current, keys);
            WmcPanel.Instance?.Refresh();
        }
    }
}
