using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------ target panel

        private sealed class TargetPresenter : Presenter
        {
            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();

            private RectTransform[] pages;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private MfdPagingGrid selectedGrid;
            private AvButton resetFilters;
            private AvButton clearTargets;
            private AvButton followHud;
            private AvButton laser;

            public TargetPresenter(MFDScreen screen, TargetListSelector selector)
                : base(screen, VanillaMfdPanelId.Tgt)
            {
                this.selector = selector;
            }

            protected override int TabCount => 2;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "FILTERS", "SELECTED" }, SelectPage);
                pages = new[] { Shell.CreatePage("Filters"), Shell.CreatePage("Selected") };
                BuildFiltersPage(pages[0]);
                BuildSelectedPage(pages[1]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                if (!Ready)
                {
                    SetFilterInput(false);
                    Shell.DataBar.State.text = "WAITING FOR TARGET FILTERS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                SetFilterInput(true);
                int filters = CountEnabled(selector.toggleFactionItems) +
                              CountEnabled(selector.toggleUnitTypesItems) +
                              CountEnabled(selector.toggleVehicleTypesItems);
                Shell.DataBar.State.text = "TARGET ACQUISITION";
                Shell.DataBar.SetChip(0, filters + " FILTERS", filters > 0);
                Shell.DataBar.SetChip(1, selector.toggleFollowHUD.status ? "HUD LINK" : "MANUAL", true);
                Shell.DataBar.SetChip(2, selector.toggleLaser.status ? "LASER" : "NO LASER",
                                      selector.toggleLaser.status);

                resetFilters.SetLatched(filters == 0);
                clearTargets.SetEnabled(SelectedCount() > 0);
                followHud.SetLatched(selector.toggleFollowHUD.status);
                laser.SetLatched(selector.toggleLaser.status);

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
            }

            protected override string AmbientStatus() =>
                "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";

            private void BuildFiltersPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "TARGET FILTER", "LIVE MAP ACQUISITION");
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * 3f) / 4f;
                resetFilters = AvStyled.Button(page, new Rect(AvTokens.Space3, y, width, AvTokens.RowHeight),
                    "RESET", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                clearTargets = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + (width + gap), y, width, AvTokens.RowHeight),
                    "CLEAR", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                followHud = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + 2f * (width + gap), y, width, AvTokens.RowHeight),
                    "HUD LINK", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleFollowHUD.Toggle();
                        selector.OnToggleFollowHUD();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    },
                    AvButtonStyle.Toggle);
                laser = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + 3f * (width + gap), y, width, AvTokens.RowHeight),
                    "LASER", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "FACTION", "FRIEND / HOSTILE");
                factionGrid = new MfdPagingGrid(page, y, Shell.Body.width, 2, 1, pager: false);
                AddRightClickActions(factionGrid, OnlyFaction);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "UNIT CLASS", "AIR / GROUND / SEA");
                unitGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 2, pager: false);
                AddRightClickActions(unitGrid, OnlyUnitType);
                y -= AvTokens.RowHeight * 2f + AvTokens.Gap + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "PLATFORM TYPE", "DETAILED FILTER");
                vehicleGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 4);
                AddRightClickActions(vehicleGrid, OnlyVehicleType);
            }

            private void BuildSelectedPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "SELECTED TARGETS", "MAP ICONS");
                selectedGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9);
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick);
            }

            private void AddRightClickActions(MfdPagingGrid grid, Action<int> onOnly)
            {
                for (int i = 0; i < 12; i++)
                {
                    int slot = i;
                    AvButton button = grid.ButtonAt(i);
                    if (button == null) continue;
                    MfdRightClickAction action = button.gameObject.AddComponent<MfdRightClickAction>();
                    action.Configure(() => onOnly(grid.CurrentIndex(slot)));
                }
            }

            private void ToggleFaction(int index) => Toggle(selector.toggleFactionItems, index);
            private void ToggleUnitType(int index) => Toggle(selector.toggleUnitTypesItems, index);
            private void ToggleVehicleType(int index) => Toggle(selector.toggleVehicleTypesItems, index);

            private void OnlyFaction(int index) => SetOnly(selector.toggleFactionItems, index);
            private void OnlyUnitType(int index) => SetOnly(selector.toggleUnitTypesItems, index);
            private void OnlyVehicleType(int index) => SetOnly(selector.toggleVehicleTypesItems, index);

            private void Toggle(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                selector.SetOnlyItem(entries[index]);
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void RefreshSelectedGrid()
            {
                selectedUnits.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.selectedIcons != null)
                {
                    for (int i = 0; i < map.selectedIcons.Count; i++)
                    {
                        UnitMapIcon icon = map.selectedIcons[i] as UnitMapIcon;
                        if (icon != null && icon.unit != null) selectedUnits.Add(icon.unit);
                    }
                }

                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => false, _ => { });
            }

            private int SelectedCount()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return map == null || map.selectedIcons == null ? 0 : map.selectedIcons.Count;
            }

            private bool Ready => selector != null && selector.toggleFollowHUD != null &&
                                  selector.toggleLaser != null && selector.toggleFactionItems != null &&
                                  selector.toggleUnitTypesItems != null &&
                                  selector.toggleVehicleTypesItems != null;

            private void SetFilterInput(bool enabled)
            {
                resetFilters?.SetEnabled(enabled);
                clearTargets?.SetEnabled(enabled);
                followHud?.SetEnabled(enabled);
                laser?.SetEnabled(enabled);
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
            }

            private static int CountEnabled(List<TargetListSelector_ToggleButton> entries)
            {
                if (entries == null) return 0;
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].status) count++;
                return count;
            }

            private static string NativeTargetLabel(TargetListSelector_ToggleButton entry)
            {
                if (entry != null && entry.label != null && !string.IsNullOrEmpty(entry.label.text))
                    return AvTheme.Truncate(entry.label.text.Replace("\n", " ").ToUpperInvariant(), 22);
                return NativeLabel(entry, "FILTER");
            }

            private static string TargetUnitLabel(Unit unit)
            {
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return AvTheme.Truncate((name ?? "UNIT").ToUpperInvariant(), 34);
            }
        }
    }
}
