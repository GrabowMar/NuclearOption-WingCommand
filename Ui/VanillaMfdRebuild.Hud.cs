using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------------- HUD panel

        private sealed class HudPresenter : Presenter
        {
            private readonly HUDOptions options;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels =
                new Dictionary<HUDOptions_ToggleButton, string>();
            private RectTransform[] pages;
            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private MfdPagingGrid vehicles;
            private MfdPagingGrid buildings;

            public HudPresenter(MFDScreen screen, HUDOptions options)
                : base(screen, VanillaMfdPanelId.Hud)
            {
                this.options = options;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "MODE", "VEHICLES", "BUILDINGS" }, SelectPage);
                pages = new[]
                {
                    Shell.CreatePage("HudModes"),
                    Shell.CreatePage("HudVehicles"),
                    Shell.CreatePage("HudBuildings"),
                };

                BuildModePage(pages[0]);
                DrawSpine(pages[1]);
                float vehicleTop = Heading(pages[1], -AvTokens.Space1, Shell.Body.width,
                                            "VEHICLE PRIORITY", "ONE TYPE AT A TIME");
                vehicles = new MfdPagingGrid(pages[1], vehicleTop, Shell.Body.width, 3, 5);

                DrawSpine(pages[2]);
                float buildingTop = Heading(pages[2], -AvTokens.Space1, Shell.Body.width,
                                             "BUILDING PRIORITY", "ONE TYPE AT A TIME");
                buildings = new MfdPagingGrid(pages[2], buildingTop, Shell.Body.width, 3, 5);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                // These lists are populated by the native component's startup pass. The
                // dock can attach a host one frame sooner, so wait for a complete model
                // rather than logging an error every refresh or freezing a partial view.
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    Shell.DataBar.State.text = "WAITING FOR HUD OPTIONS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                Shell.DataBar.State.text = "HUD PRIORITY MATRIX";
                SetGridInput(true);
                Shell.DataBar.SetChip(0, options.currentMode.ToString(), true);
                Shell.DataBar.SetChip(1, CountEnabled(options.listVehicleTypes) + " VEH", true);
                Shell.DataBar.SetChip(2, CountEnabled(options.listBuildingTypes) + " BLD", true);

                modes.SetData(options.listModes.Count,
                    i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(options.listModes[i], "MODE"),
                    i => options.listModes[i] != null && options.listModes[i].status,
                    SelectMode);
                categories.SetData(options.listCategories.Count,
                    i => NativeCategoryLabel(options.listCategories[i], i),
                    i => options.listCategories[i] != null && options.listCategories[i].maximized,
                    ToggleCategory);
                vehicles.SetData(options.listVehicleTypes.Count,
                    i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                    i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                    SelectVehicle);
                buildings.SetData(options.listBuildingTypes.Count,
                    i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                    i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                    SelectBuilding);
            }

            protected override string AmbientStatus() =>
                "HUD " + options.currentMode + " — SELECT WHAT GETS EMPHASISED";

            private void BuildModePage(RectTransform root)
            {
                DrawSpine(root);
                float y = Heading(root, -AvTokens.Space1, Shell.Body.width,
                                  "ENGAGEMENT MODE", "AUTO HUD PROFILE");
                modes = new MfdPagingGrid(root, y, Shell.Body.width, 3, 2, pager: false);
                y -= AvTokens.RowHeight * 2f + AvTokens.Gap * 2f + AvTokens.Space3;
                y = Heading(root, y, Shell.Body.width, "PRIORITY GATES", "MAXIMISE TRACKS");
                categories = new MfdPagingGrid(root, y, Shell.Body.width, 3, 2, pager: false);
            }

            private void SelectPage(int next)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == next);
                Shell.SetSelectedTab(next);
                RequestRefresh();
            }

            private void SelectMode(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listModes.Count) return;
                HUDOptions_ToggleButton source = options.listModes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                if (index < 6) options.currentMode = (HUDOptions.HUDMode)index;
                Persist();
            }

            private void ToggleCategory(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listCategories.Count) return;
                HUDOptions_Category category = options.listCategories[index];
                if (category == null) return;
                category.Set(!category.maximized);
                Persist();
            }

            private void SelectVehicle(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listVehicleTypes.Count) return;
                HUDOptions_ToggleButton source = options.listVehicleTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void SelectBuilding(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listBuildingTypes.Count) return;
                HUDOptions_ToggleButton source = options.listBuildingTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void Persist()
            {
                options.SaveSettings();
                options.NeedUpdateIcons();
                RequestRefresh();
            }

            private string LabelFor(HUDOptions_ToggleButton source, string fallback)
            {
                if (source == null) return fallback;
                if (!labels.TryGetValue(source, out string label))
                {
                    label = NativeLabel(source, fallback);
                    labels.Add(source, label);
                }
                return label;
            }

            private bool Ready => options != null && options.listModes != null &&
                                  options.listCategories != null &&
                                  options.listVehicleTypes != null &&
                                  options.listBuildingTypes != null;

            private void SetGridInput(bool enabled)
            {
                modes?.SetInteractable(enabled);
                categories?.SetInteractable(enabled);
                vehicles?.SetInteractable(enabled);
                buildings?.SetInteractable(enabled);
            }
        }
    }
}
