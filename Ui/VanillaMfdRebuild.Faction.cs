using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // The faction, target and mission presenters use the same host and paging primitive
        // below; keeping the controller-specific code here makes a future game API change a
        // local adapter edit instead of another prefab traversal.

        // ----------------------------------------------------------- faction panels

        /// <summary>
        /// The left and right faction screens share a single data adapter.  The source
        /// controller tells us whose HQ to read; this avoids treating the mutable bezel
        /// short-name as a gameplay identifier and deliberately does not call the stock
        /// airbase switch (which currently switches to players internally).
        /// </summary>
        private sealed class FactionPresenter : Presenter
        {
            private enum LedgerMode { Reserves, Losses, Value, Manpower }
            private enum InfoMode { Airbases, Players }

            private readonly InfoPanel_Faction source;
            private readonly List<UnitDefinition> definitions = new List<UnitDefinition>();
            private readonly List<string> infoRows = new List<string>();

            private RectTransform[] pages;
            private MfdPagingGrid definitionGrid;
            private MfdPagingGrid infoGrid;
            private AvButton[] definitionTabs;
            private AvButton[] ledgerTabs;
            private AvButton[] infoTabs;
            private TMP_Text factionName;
            private Image factionLogo;
            private TMP_Text[] forceTotals;
            private AvStyled.Metric[] ledgerMetrics;
            private int definitionGroup;
            private bool definitionsLoaded;
            private LedgerMode ledgerMode;
            private InfoMode infoMode;

            public FactionPresenter(MFDScreen screen, InfoPanel_Faction source, VanillaMfdPanelId id)
                : base(screen, id)
            {
                this.source = source;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "FORCES", "LEDGER", "STATUS" }, SelectPage);
                pages = new[]
                {
                    Shell.CreatePage("Forces"),
                    Shell.CreatePage("Ledger"),
                    Shell.CreatePage("Status"),
                };

                BuildForcesPage(pages[0]);
                BuildLedgerPage(pages[1]);
                BuildStatusPage(pages[2]);
                SelectDefinitions(0);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                FactionHQ hq = source == null ? null : source.factionHQ;
                if (hq == null)
                {
                    Shell.DataBar.State.text = "WAITING FOR FACTION HQ";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;
                Shell.DataBar.State.text = AvTheme.Truncate((name ?? "FACTION").ToUpperInvariant(), 18);
                Shell.DataBar.SetChip(0, "SCORE " + hq.factionScore.ToString("0.0"), true);
                Shell.DataBar.SetChip(1, UnitConverter.ValueReading(hq.factionFunds), true);
                Shell.DataBar.SetChip(2, "WHD " + hq.GetWarheadStockpile(), true);

                factionName.text = name ?? "FACTION";
                Sprite logo = hq.faction == null ? null : hq.faction.factionColorLogo;
                factionLogo.sprite = logo;
                factionLogo.enabled = logo != null;

                SetForceTotals(hq.missionStatsTracker == null
                    ? default(MissionStatsTracker.TypeStat)
                    : hq.missionStatsTracker.units);
                RefreshDefinitionGrid(hq);
                RefreshLedger(hq);
                RefreshInfo(hq);
                UpdateButtonRows();
            }

            protected override string AmbientStatus()
            {
                return infoMode == InfoMode.Airbases
                    ? "FACTION FORCE INVENTORY — AIRBASE STATUS"
                    : "FACTION FORCE INVENTORY — PLAYER STATUS";
            }

            private void BuildForcesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "FORCE INVENTORY", "LIVE ASSETS");

                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3,
                             68f), AvTheme.RailReady);
                factionLogo = AvKit.Panel(page, new Rect(AvTokens.Space4, y - 8f, 52f, 52f),
                                           Color.white, AvSprites.Control);
                factionLogo.preserveAspect = true;
                factionLogo.raycastTarget = false;
                factionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4 + 64f, y - 8f, Shell.Body.width - 100f, 28f),
                    "SYNCING FACTION", "metric-value");
                AvStyled.Label(page,
                    new Rect(AvTokens.Space4 + 64f, y - 42f, Shell.Body.width - 100f, 12f),
                    "LIVE THEATER ORDER OF BATTLE", "metric-cap");
                y -= 80f;

                forceTotals = new TMP_Text[4];
                string[] labels = { "BLD", "VEH", "SHP", "AIR" };
                float totalWidth = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap * 3f) / 4f;
                for (int i = 0; i < forceTotals.Length; i++)
                {
                    float x = AvTokens.Space3 + i * (totalWidth + AvTokens.Gap);
                    AvStyled.Label(page, new Rect(x, y, totalWidth, 11f), labels[i], "metric-key",
                                   align: TextAlignmentOptions.Center);
                    forceTotals[i] = AvStyled.Label(page, new Rect(x, y - 20f, totalWidth, 20f),
                                                     "—", "row-value",
                                                     align: TextAlignmentOptions.Center);
                }
                y -= 44f;

                y = Heading(page, y, Shell.Body.width, "ASSET CLASS", "TAP TO FILTER");
                definitionTabs = CreateButtonRow(page, y,
                    new[] { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" }, SelectDefinitions);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, Shell.Body.width, "UNIT READOUT", "CURRENT / LOST");
                definitionGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 4);
            }

            private void BuildLedgerPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "THEATER LEDGER", "MISSION ACCOUNTING");
                ledgerTabs = CreateButtonRow(page, y,
                    new[] { "RESERVES", "LOSSES", "VALUE", "MANPOWER" }, SelectLedger);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "ASSET BREAKDOWN", "LIVE TOTALS");
                ledgerMetrics = new AvStyled.Metric[4];
                string[] labels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
                float gap = AvTokens.Gap;
                float cellWidth = (Shell.Body.width - AvTokens.Space3 - gap) * 0.5f;
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    int row = i / 2;
                    int column = i % 2;
                    ledgerMetrics[i] = AvStyled.MetricCell(page,
                        new Rect(AvTokens.Space3 + column * (cellWidth + gap),
                                 y - row * 92f, cellWidth, 82f), labels[i], "UNIT");
                }
            }

            private void BuildStatusPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "FACTION STATUS", "LIVE THEATER LINK");
                infoTabs = CreateButtonRow(page, y, new[] { "AIRBASES", "PLAYERS" }, SelectInfo);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, Shell.Body.width, "ACTIVE ENTRIES", "DIRECTORY");
                infoGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 8);
            }

            private AvButton[] CreateButtonRow(RectTransform parent, float y, string[] labels,
                                               Action<int> selected)
            {
                var row = new AvButton[labels.Length];
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * (labels.Length - 1)) /
                              labels.Length;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    row[i] = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + i * (width + gap), y, width, AvTokens.RowHeight),
                        labels[i], "toggle", () => selected(index), AvButtonStyle.Toggle);
                }
                return row;
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SelectDefinitions(int selected)
            {
                definitionGroup = Mathf.Clamp(selected, 0, 3);
                PopulateDefinitions();
                RequestRefresh();
            }

            private void SelectLedger(int selected)
            {
                ledgerMode = (LedgerMode)Mathf.Clamp(selected, 0, 3);
                RequestRefresh();
            }

            private void SelectInfo(int selected)
            {
                // Keep this local. InfoPanel_Faction.SetDisplayAirbases currently selects
                // Players in stock code, exactly the bug this owned view avoids inheriting.
                infoMode = selected == 0 ? InfoMode.Airbases : InfoMode.Players;
                infoGrid.ResetPage();
                RequestRefresh();
            }

            private void PopulateDefinitions()
            {
                definitions.Clear();
                definitionsLoaded = false;
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (encyclopedia == null) return;

                switch (definitionGroup)
                {
                    case 0:
                        if (encyclopedia.buildings == null) return;
                        for (int i = 0; i < encyclopedia.buildings.Count; i++)
                            definitions.Add(encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles == null) return;
                        for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                            definitions.Add(encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships == null) return;
                        for (int i = 0; i < encyclopedia.ships.Count; i++)
                            definitions.Add(encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft == null) return;
                        for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                            definitions.Add(encyclopedia.aircraft[i]);
                        break;
                }
                definitionsLoaded = true;
            }

            private void SetForceTotals(MissionStatsTracker.TypeStat stats)
            {
                if (forceTotals == null) return;
                for (int i = 0; i < forceTotals.Length; i++) forceTotals[i].text = "—";
                forceTotals[0].text = stats.buildings.current.ToString("0");
                forceTotals[1].text = stats.vehicles.current.ToString("0");
                forceTotals[2].text = stats.ships.current.ToString("0");
                forceTotals[3].text = stats.aircraft.current.ToString("0");
            }

            private void RefreshDefinitionGrid(FactionHQ hq)
            {
                if (!definitionsLoaded) PopulateDefinitions();
                definitionGrid.SetData(definitions.Count,
                    i => DefinitionLabel(definitions[i], hq),
                    i => false,
                    _ => { });
            }

            private string DefinitionLabel(UnitDefinition definition, FactionHQ hq)
            {
                if (definition == null) return "UNKNOWN";
                int current = hq.missionStatsTracker == null ? 0 :
                    hq.missionStatsTracker.GetCurrentUnits(definition);
                int lost = hq.missionStatsTracker == null ? 0 :
                    hq.missionStatsTracker.GetLostUnits(definition);
                string code = string.IsNullOrEmpty(definition.code) ? definition.unitName : definition.code;
                return AvTheme.Truncate((code ?? "UNIT").ToUpperInvariant(), 11) + " " +
                       current + "/" + lost;
            }

            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerMetrics == null || hq.missionStatsTracker == null) return;
                MissionStatsTracker.TypeStat category = LedgerCategory(hq.missionStatsTracker);
                MissionStatsTracker.Stat[] values =
                {
                    category.buildings, category.vehicles, category.ships, category.aircraft,
                };
                string unit = ledgerMode == LedgerMode.Value ? "CR" :
                              ledgerMode == LedgerMode.Manpower ? "PAX" : "UNIT";
                string caption = ledgerMode.ToString().ToUpperInvariant();
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    MissionStatsTracker.Stat stat = values[i];
                    float value = ledgerMode == LedgerMode.Reserves ? ReserveCount(hq, i) :
                                  LedgerValue(stat);
                    float denominator = ledgerMode == LedgerMode.Reserves
                        ? Mathf.Max(1f, value + stat.current)
                        : Mathf.Max(1f, stat.total);
                    float fraction = ledgerMode == LedgerMode.Losses ? stat.lost / denominator :
                                     value / denominator;
                    ledgerMetrics[i].Unit.text = unit;
                    ledgerMetrics[i].Set(FormatLedger(value), caption, fraction,
                                         ledgerMode == LedgerMode.Losses ? AvTheme.Warning : AvTheme.Accent);
                }
            }

            private MissionStatsTracker.TypeStat LedgerCategory(MissionStatsTracker tracker)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses:
                    case LedgerMode.Reserves:
                        return tracker.units;
                    case LedgerMode.Value:
                        return tracker.value;
                    default:
                        return tracker.manpower;
                }
            }

            private float LedgerValue(MissionStatsTracker.Stat stat)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses: return stat.lost;
                    case LedgerMode.Value: return stat.current;
                    case LedgerMode.Manpower: return stat.current;
                    default: return stat.current;
                }
            }

            private string FormatLedger(float value)
            {
                return ledgerMode == LedgerMode.Value
                    ? UnitConverter.ValueReading(value)
                    : value.ToString("0");
            }

            private static int ReserveCount(FactionHQ hq, int group)
            {
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (hq == null || encyclopedia == null) return 0;

                int total = 0;
                switch (group)
                {
                    case 0:
                        if (encyclopedia.buildings != null)
                            for (int i = 0; i < encyclopedia.buildings.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles != null)
                            for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships != null)
                            for (int i = 0; i < encyclopedia.ships.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft != null)
                            for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.aircraft[i]);
                        break;
                }
                return total;
            }

            private static int SupportedSupply(FactionHQ hq, UnitDefinition definition)
            {
                // FactionHQ stores reserve supply only for mobile definitions. The
                // encyclopedia also exposes buildings through UnitDefinition, but passing
                // one to GetUnitSupply throws in current game builds. Keep the adapter
                // tolerant of mixed/future category lists by filtering on the API contract.
                if (hq == null || definition == null) return 0;
                if (!(definition is AircraftDefinition) && !(definition is VehicleDefinition)) return 0;
                return Mathf.Max(0, hq.GetUnitSupply(definition));
            }

            private void RefreshInfo(FactionHQ hq)
            {
                infoRows.Clear();
                if (infoMode == InfoMode.Airbases)
                {
                    foreach (Airbase airbase in hq.GetAirbases())
                    {
                        if (airbase != null) infoRows.Add("AIRBASE  " + airbase.name.ToUpperInvariant());
                    }
                }
                else
                {
                    foreach (var player in hq.GetPlayers(sortByScore: false))
                    {
                        if (player != null) infoRows.Add("PLAYER   " + player);
                    }
                }

                if (infoRows.Count == 0)
                    infoRows.Add(infoMode == InfoMode.Airbases ? "NO ACTIVE AIRBASES" : "NO ACTIVE PLAYERS");
                infoGrid.SetData(infoRows.Count, i => infoRows[i], i => false, _ => { });
            }

            private void UpdateButtonRows()
            {
                SetRow(definitionTabs, definitionGroup);
                SetRow(ledgerTabs, (int)ledgerMode);
                SetRow(infoTabs, infoMode == InfoMode.Airbases ? 0 : 1);
            }

            private static void SetRow(AvButton[] buttons, int selected)
            {
                if (buttons == null) return;
                for (int i = 0; i < buttons.Length; i++) buttons[i].SetLatched(i == selected);
            }
        }
    }
}
