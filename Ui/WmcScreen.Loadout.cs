using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>LOADOUT page for persistent pylon-template editing.</summary>
    internal static partial class WmcScreen
    {
        // Template editing state; current airborne fits are displayed on WING.
        private static TMP_Text loadoutStatusLabel;
        private static TMP_Text loadoutProfileTitle;
        private static Image loadoutProfileRail;
        private static Image loadoutProfileIcon;
        private static TMP_Text templateLabel;
        private static TMP_Text liveryLabel;
        private static TMP_InputField templateNameField;
        private static TMP_Text templateSummaryLabel;
        private static WingButton templateSelectButton;
        private static RectTransform pylonEmptyCard;
        private static TMP_Text pylonEmptyLabel;
        private static WingButton templateNewButton;
        private static WingButton templateCopyButton;
        private static WingButton templateDeleteButton;
        private static RectTransform pylonArea;
        private static WingButton pylonPrevButton;
        private static WingButton pylonNextButton;
        private static TMP_Text pylonPageLabel;
        private static readonly List<PylonRow> pylonRows = new List<PylonRow>();

        private const int AirframeGridRows = 4;
        private const int AirframeGridCols = 4;
        private const int AirframeGridCapacity = AirframeGridRows * AirframeGridCols; // Airframe grid capacity.
        private const float AirframeTileHeight = 36f;
        private const float AirframeTileGap = 4f;

        private static int airframePage;
        private static RectTransform airframePager;
        private static WingButton airframePrevButton;
        private static WingButton airframeNextButton;
        private static TMP_Text airframePageLabel;
        private static readonly List<AirframeTile> airframeTiles = new List<AirframeTile>();

        /// <summary>Popup entries rebuilt when opened.</summary>
        private static readonly List<AvKit.PopupEntry> popupEntries =
            new List<AvKit.PopupEntry>();

        private static readonly List<WingLoadoutCatalog.StoreOption> storeScratch =
            new List<WingLoadoutCatalog.StoreOption>();

        private static AvKit.Popup loadoutPopup;
        private static AvKit.Popup shopTemplatePopup;

        /// <summary>Resolve the edited template by stable ID so deletion cannot leave a stale record
        /// active.</summary>
        private static string editingTemplateId;

        /// <summary>Current pylon page for the edited airframe.</summary>
        private static int pylonPage;

        /// <summary>Visible pylon rows per page, sized for common airframes without excess empty
        /// space.</summary>
        private const int PylonRowsPerPage = 6;

        // Loadout-page construction.

        /// <summary>Build persistent templates from explicit per-pylon choices without changing live
        /// aircraft or funds. SUPPLY selects the template for a purchase; WING displays airborne
        /// fits.</summary>
        private static float AddLoadoutPage(RectTransform parent, float y)
        {
            loadoutPopup = new AvKit.Popup(parent, PanelWidth);

            y = Heading(parent, y, "AIRFRAME");
            float airframeGridTop = y;
            y = AddAirframeGrid(parent, airframeGridTop);

            // Build pager after tiles so shared-border rounding cannot let a tile intercept its arrows.
            airframePager = HeaderPager(parent, airframeGridTop + Space5,
                                        () => TurnAirframePage(-1), () => TurnAirframePage(1),
                                        out airframePrevButton, out airframePageLabel, out airframeNextButton);
            airframePager.SetAsLastSibling();

            y = Heading(parent, y, "TEMPLATE");

            float left = Pad + GutterWidth;
            float inner = PanelWidth - Pad - left;

            // Group template selection with clearly labelled new, copy, and delete actions.
            const float actionWidth = WingUi.ButtonCompact;
            const float actionBlock = (actionWidth + Gap) * 3f;

            Gutter(parent, y, "TEMPLATE");
            float selectWidth = inner - actionBlock;
            templateSelectButton = WingUi.Button(parent, "", new Rect(left, y, selectWidth, RowHeight),
                                                 FontSmall, UiButtonStyle.Default,
                                                 () => OpenTemplatePicker(left, y, selectWidth))
                                        .WithTooltip(LoadoutHint.Select);
            templateLabel = null;

            float actionX = left + selectWidth + Gap;
            templateNewButton = WingUi.Button(parent, "NEW", new Rect(actionX, y, actionWidth, RowHeight),
                                              FontSmall, UiButtonStyle.Default, NewTemplate)
                                     .WithTooltip(LoadoutHint.New);
            templateCopyButton = WingUi.Button(parent, "COPY",
                                               new Rect(actionX + actionWidth + Gap, y,
                                                        actionWidth, RowHeight),
                                               FontSmall, UiButtonStyle.Default, CopyTemplate)
                                      .WithTooltip(LoadoutHint.Copy);
            templateDeleteButton = WingUi.Button(parent, "DEL",
                                                 new Rect(actionX + (actionWidth + Gap) * 2f, y,
                                                          actionWidth, RowHeight),
                                                 FontSmall, UiButtonStyle.Danger, DeleteTemplate)
                                        .WithTooltip(LoadoutHint.Delete);
            y -= RowHeight + Gap;

            // Offer rename input only when keyboard capture works; otherwise show the saved/default
            // name without leaking typing into flight controls.
            Gutter(parent, y, "NAME");
            float nameWidth = inner;

            if (WingKeyboardGuard.Available)
            {
                templateNameField = WingUi.InputField(
                    parent, new Rect(left, y, nameWidth, RowHeight),
                    EconomyFacade.LoadoutTemplates.MaxNameLength, RenameTemplate, LoadoutHint.Name);
            }
            else
            {
                Panel(parent, new Rect(left, y, nameWidth, RowHeight), RowColor());
                templateLabel = Label(parent, "",
                                      new Rect(left + Space2, y, nameWidth - Space4, RowHeight),
                                      Dim(), FontBody, FontStyles.Normal,
                                      TextAlignmentOptions.Left);
            }

            y -= RowHeight + Gap;

            Gutter(parent, y, "LIVERY");
            Stepper(parent, left, y, nameWidth, out liveryLabel, () => CycleLivery(-1), () => CycleLivery(1),
                    "Select paint livery for requisitioned aircraft of this type");

            y -= RowHeight + Gap;

            y = Heading(parent, y, "PYLONS");
            y = ColumnHeaders(parent, y, PylonColumns);

            var area = new GameObject("PylonArea", typeof(RectTransform));
            pylonArea = area.GetComponent<RectTransform>();
            pylonArea.SetParent(parent, worldPositionStays: false);

            float areaHeight = RowPitch * PylonRowsPerPage;
            Place(pylonArea, new Rect(Pad, y, PanelWidth - Pad * 2f, areaHeight));
            pylonAreaY = y;

            pylonEmptyCard = WingUi.TacticalCard(pylonArea, new Rect(0f, 0f, PanelWidth - Pad * 2f, areaHeight), WingUi.RailInert, hasRail: false).CardFill.rectTransform;
            pylonEmptyLabel = EmptyNote(pylonEmptyCard, "NO HARDPOINTS DETECTED ON AIRFRAME");

            for (int i = 0; i < PylonRowsPerPage; i++) pylonRows.Add(new PylonRow(pylonArea, i));
            y -= areaHeight + Gap;

            pylonPrevButton = Pager(parent, y, "<", () => TurnPylonPage(-1));
            pylonPageLabel = PagerLabel(parent, y);
            pylonNextButton = Pager(parent, y, ">", () => TurnPylonPage(1));
            y -= RowHeight + Gap;

            return AddLoadoutProfile(parent, y);
        }

        /// <summary>Show aggregate mass, role, and next-step feedback below the pylon rows.</summary>
        private static float AddLoadoutProfile(RectTransform parent, float y)
        {
            const float height = 96f;
            const float iconSize = 58f;
            float w = PanelWidth - Pad * 2f;
            float textX = Pad + Space3;
            float iconX = Pad + w - iconSize - Space2;
            float textW = iconX - textX - Space2;

            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, height), WingUi.RailCyan);
            loadoutProfileRail = rail;
            loadoutProfileTitle = Label(parent, "LOADOUT PROFILE", new Rect(textX, y - Space2, textW, LineHeight),
                                        WingUi.RailCyan, FontMicro, FontStyles.Bold,
                                        TextAlignmentOptions.Left);
            templateSummaryLabel = Label(parent, "", new Rect(textX, y - 28f, textW, LineHeight),
                                         Dim(), FontSmall, FontStyles.Normal,
                                         TextAlignmentOptions.Left);
            loadoutStatusLabel = Label(parent, "", new Rect(textX, y - 50f, textW, 30f),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.TopLeft);
            loadoutStatusLabel.enableWordWrapping = true;
            loadoutStatusLabel.overflowMode = TextOverflowModes.Ellipsis;
            loadoutProfileIcon = AddSprite(parent, "LoadoutProfileAirframe", IconFactory.Get("airframe"),
                                           new Rect(iconX, y - Space3, iconSize, iconSize), Dim());
            return y - height - Gap;
        }

        /// <summary>Pylon-list origin for row-aligned store popups.</summary>
        private static float pylonAreaY;

        private static readonly Column[] PylonColumns =
        {
            new Column("PYLON", Space2, 140f),
            // Left-align store names so ellipsis preserves their identifying prefix.
            new Column("STORE", 152f, PanelWidth - Pad * 2f - 152f - Space2),
        };

        /// <summary>Create a fixed-height viewport for a page of roster rows.</summary>
        private static RectTransform RosterViewport(RectTransform parent, string name, float y, int rowCount = RosterRowsPerPage)
        {
            var area = new GameObject(name, typeof(RectTransform));
            RectTransform rt = area.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, new Rect(Pad, y, PanelWidth - Pad * 2f, RowPitch * rowCount));
            return rt;
        }

        private static void SelectAirframe(AircraftDefinition def)
        {
            if (def == null || selectedOffer == def) return;
            selectedOffer = def;
            editingTemplateId = null;
            pylonPage = 0;
            RefreshLoadoutPage();
        }

        private static void TurnAirframePage(int direction)
        {
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.LoadoutCatalogue();
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)AirframeGridCapacity));
            airframePage = Mathf.Clamp(airframePage + direction, 0, pages - 1);
            RefreshLoadoutPage();
        }

        private static float AddAirframeGrid(RectTransform parent, float y)
        {
            airframeTiles.Clear();
            float w = PanelWidth - Pad * 2f;
            float colWidth = (w - (AirframeGridCols - 1) * AirframeTileGap) / AirframeGridCols;

            for (int r = 0; r < AirframeGridRows; r++)
            {
                float rowY = y - r * (AirframeTileHeight + AirframeTileGap);
                for (int c = 0; c < AirframeGridCols; c++)
                {
                    float tileX = Pad + c * (colWidth + AirframeTileGap);
                    int index = r * AirframeGridCols + c;
                    airframeTiles.Add(new AirframeTile(parent, new Rect(tileX, rowY, colWidth, AirframeTileHeight), index));
                }
            }

            return y - (AirframeGridRows * AirframeTileHeight + (AirframeGridRows - 1) * AirframeTileGap + Gap);
        }

        private static void RefreshAirframeGrid()
        {
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.LoadoutCatalogue();

            if (selectedOffer == null && offers.Count > 0)
                selectedOffer = offers[0].Definition;

            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)AirframeGridCapacity));

            // Paging changes visible tiles, not the edited airframe; retain details until another tile
            // is selected.
            airframePage = Mathf.Clamp(airframePage, 0, pages - 1);

            int first = airframePage * AirframeGridCapacity;
            for (int i = 0; i < airframeTiles.Count; i++)
            {
                int offerIndex = first + i;
                if (offerIndex < offers.Count)
                {
                    AircraftDefinition def = offers[offerIndex].Definition;
                    airframeTiles[i].Bind(def, def == selectedOffer);
                }
                else
                {
                    airframeTiles[i].Bind(null, false);
                }
            }

            RefreshHeaderPager(airframePager, airframePrevButton, airframePageLabel, airframeNextButton,
                               airframePage, pages);
        }

        // Template editing.

        /// <summary>Resolve the edited ID on each read; return null when its record was deleted or
        /// invalidated.</summary>
        private static LoadoutTemplateRecord EditingTemplate()
        {
            if (selectedOffer == null) return null;

            LoadoutTemplateRecord record = EconomyFacade.LoadoutTemplates.ById(editingTemplateId);
            if (record != null && record.AirframeKey == selectedOffer.jsonKey) return record;

            // Fall back to the first remaining template instead of an empty editor.
            IReadOnlyList<LoadoutTemplateRecord> mine = EconomyFacade.LoadoutTemplates.For(selectedOffer);
            if (mine.Count == 0)
            {
                editingTemplateId = null;
                return null;
            }

            editingTemplateId = mine[0].Id;
            return mine[0];
        }

        private static void OpenTemplatePicker(float x, float y, float width)
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            IReadOnlyList<LoadoutTemplateRecord> mine = EconomyFacade.LoadoutTemplates.For(selectedOffer);
            if (mine.Count == 0)
            {
                WingCommandManager.Instance?.Toast(
                    "No templates for " + selectedOffer.unitName + " yet - press + to make one");
                return;
            }

            // Copy popup IDs because its callback outlives the reusable query list.
            var ids = new List<string>(mine.Count);
            popupEntries.Clear();
            for (int i = 0; i < mine.Count; i++)
            {
                ids.Add(mine[i].Id);
                popupEntries.Add(new AvKit.PopupEntry(
                    AvTheme.Truncate(mine[i].Name, 24),
                    FittedCount(mine[i]) + " fitted",
                    mine[i].Id == editingTemplateId));
            }

            loadoutPopup?.Show(new Rect(x, y - RowHeight, width, 0f), popupEntries, index =>
            {
                if (index < 0 || index >= ids.Count) return;
                editingTemplateId = ids[index];
                pylonPage = 0;
                SyncNameField();
            });
        }

        private static void NewTemplate()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            if (EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer) == 0)
            {
                WingCommandManager.Instance?.Toast(
                    selectedOffer.unitName + "'s hardpoints cannot be read on this build");
                return;
            }

            LoadoutTemplateRecord created = EconomyFacade.LoadoutTemplates.Create(
                selectedOffer, EconomyFacade.LoadoutTemplates.NextDefaultName(selectedOffer), null);

            if (created == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + EconomyFacade.LoadoutTemplates.MaxPerAirframe +
                    " templates");
                return;
            }

            editingTemplateId = created.Id;
            pylonPage = 0;
            SyncNameField();
        }

        private static void CopyTemplate()
        {
            LoadoutTemplateRecord source = EditingTemplate();
            if (source == null)
            {
                WingCommandManager.Instance?.Toast("Nothing to copy");
                return;
            }

            LoadoutTemplateRecord copy = EconomyFacade.LoadoutTemplates.Duplicate(source);
            if (copy == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + EconomyFacade.LoadoutTemplates.MaxPerAirframe +
                    " templates");
                return;
            }

            editingTemplateId = copy.Id;
            SyncNameField();
        }

        private static void DeleteTemplate()
        {
            LoadoutTemplateRecord doomed = EditingTemplate();
            if (doomed == null)
            {
                WingCommandManager.Instance?.Toast("Nothing to delete");
                return;
            }

            string name = doomed.Name;
            EconomyFacade.LoadoutTemplates.Delete(doomed);
            editingTemplateId = null;
            pylonPage = 0;
            SyncNameField();

            WingCommandManager.Instance?.Toast(
                "Deleted " + name + ". Anything already flying keeps its fit.");
        }

        private static void RenameTemplate(string name)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null) return;

            EconomyFacade.LoadoutTemplates.Rename(template, name);

            // Synchronise the field with the store's trimmed/defaulted saved name.
            SyncNameField();
        }

        /// <summary>Update rename text on template changes, not periodic refreshes, to preserve the typing
        /// caret.</summary>
        private static void SyncNameField()
        {
            LoadoutTemplateRecord template = EditingTemplate();
            string name = template != null ? template.Name : "";

            if (templateNameField != null)
            {
                if (templateNameField.text != name)
                    templateNameField.SetTextWithoutNotify(name);
                templateNameField.interactable = template != null;
            }

            if (templateLabel != null)
            {
                templateLabel.text = template != null ? name : "-";
                templateLabel.color = template != null ? Friendly() : Dim();
            }
        }

        // Pylon rows.

        /// <summary>Visible pylons exclude linked mirrors; editing the shown partner writes both actual
        /// stations.</summary>
        private static readonly List<int> visiblePylons = new List<int>();

        private static void RebuildVisiblePylons()
        {
            visiblePylons.Clear();
            if (selectedOffer == null) return;

            int count = EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                if (EconomyFacade.LoadoutCatalog.MirrorsPrevious(selectedOffer, i)) continue;
                visiblePylons.Add(i);
            }
        }

        private static void TurnPylonPage(int direction)
        {
            pylonPage = Mathf.Max(0, pylonPage + direction);
            RefreshLoadoutPage();
        }

        /// <summary>Write store keys to the pylon and its mirror so saved templates describe the complete
        /// fit.</summary>
        private static void SetStore(int pylon, string key)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null) return;

            EconomyFacade.LoadoutTemplates.SetMount(template, pylon, key);

            int count = EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < count && EconomyFacade.LoadoutCatalog.MirrorsPrevious(selectedOffer, i);
                 i++)
                EconomyFacade.LoadoutTemplates.SetMount(template, i, key);
        }

        private static void OpenStorePicker(int pylon, int rowIndex)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null)
            {
                WingCommandManager.Instance?.Toast("Make a template first");
                return;
            }

            EconomyFacade.LoadoutCatalog.OptionsFor(selectedOffer, pylon, storeScratch);
            if (storeScratch.Count <= 1)
            {
                WingCommandManager.Instance?.Toast(
                    EconomyFacade.LoadoutCatalog.PylonName(selectedOffer, pylon) + " takes no stores");
                return;
            }

            string current = template.KeyAt(pylon);
            var keys = new List<string>(storeScratch.Count);
            popupEntries.Clear();

            for (int i = 0; i < storeScratch.Count; i++)
            {
                WingLoadoutCatalog.StoreOption option = storeScratch[i];
                keys.Add(option.Key);
                popupEntries.Add(new AvKit.PopupEntry(
                    AvTheme.Truncate(option.Label, 26), StoreDetail(option),
                    option.Key == current));
            }

            // Open the store list beside its pylon row.
            float rowY = pylonAreaY - RowPitch * rowIndex - RowHeight;
            loadoutPopup?.Show(new Rect(Pad, rowY, PanelWidth - Pad * 2f, 0f), popupEntries,
                               index =>
            {
                if (index < 0 || index >= keys.Count) return;
                SetStore(pylon, keys[index]);
            });
        }

        /// <summary>Store type and mass for the row's detail column.</summary>
        private static string StoreDetail(WingLoadoutCatalog.StoreOption option)
        {
            if (option.IsEmpty) return "";

            string tag = option.RoleTag;
            string ammo = option.Ammo > 1 ? "x" + option.Ammo : "";

            if (tag.Length == 0 && ammo.Length == 0) return "";
            if (tag.Length == 0) return ammo;
            return ammo.Length == 0 ? tag : tag + "  " + ammo;
        }

        // Loadout refresh.

        private static void RefreshLoadoutPage()
        {
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.LoadoutCatalogue();
            ValidateSelectedOffer(offers);

            if (selectedOffer == null && offers.Count > 0) selectedOffer = offers[0].Definition;

            RefreshAirframeGrid();

            LoadoutTemplateRecord template = EditingTemplate();
            RebuildVisiblePylons();

            RefreshTemplateControls(template);
            RefreshLiveryControl();
            RefreshPylonRows(template);
            RefreshLoadoutStatus(template);
        }

        private static void RefreshLiveryControl()
        {
            if (liveryLabel == null) return;
            if (selectedOffer == null)
            {
                liveryLabel.text = "—";
                return;
            }

            FactionHQ hq = WingCommandManager.Instance?.Wing?.Leader?.NetworkHQ;
            Faction faction = hq != null ? hq.faction : null;
            var liveries = EconomyFacade.LoadoutTemplates.GetLiveries(selectedOffer, faction);
            int currentIdx = EconomyFacade.LoadoutTemplates.GetLiveryIndex(selectedOffer);
            if (currentIdx >= liveries.Count) currentIdx = 0;
            liveryLabel.text = liveries[currentIdx].Name.ToUpperInvariant();
        }

        private static void CycleLivery(int direction)
        {
            if (selectedOffer == null) return;
            FactionHQ hq = WingCommandManager.Instance?.Wing?.Leader?.NetworkHQ;
            Faction faction = hq != null ? hq.faction : null;
            var liveries = EconomyFacade.LoadoutTemplates.GetLiveries(selectedOffer, faction);
            if (liveries.Count <= 1) return;
            int current = EconomyFacade.LoadoutTemplates.GetLiveryIndex(selectedOffer);
            int next = (current + direction + liveries.Count) % liveries.Count;
            EconomyFacade.LoadoutTemplates.SetLiveryIndex(selectedOffer, next);
            RefreshLiveryControl();
        }

        private static void RefreshTemplateControls(LoadoutTemplateRecord template)
        {
            bool haveAirframe = selectedOffer != null;
            bool readable = haveAirframe && EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer) > 0;
            int saved = haveAirframe ? EconomyFacade.LoadoutTemplates.CountFor(selectedOffer) : 0;

            if (templateSelectButton != null)
            {
                templateSelectButton.SetText(
                    template != null ? AvTheme.Truncate(template.Name, 22).ToUpperInvariant()
                    : saved > 0 ? "SELECT A TEMPLATE"
                    : "NO TEMPLATES");
                templateSelectButton.SetEnabled(saved > 0);
                templateSelectButton.SetLatched(template != null);
            }

            templateNewButton?.SetEnabled(readable &&
                                          saved < EconomyFacade.LoadoutTemplates.MaxPerAirframe);
            templateCopyButton?.SetEnabled(template != null &&
                                           saved < EconomyFacade.LoadoutTemplates.MaxPerAirframe);
            templateDeleteButton?.SetEnabled(template != null);
            // Refresh name text only when the edited template changes.
            if (!ReferenceEquals(lastNamedTemplate, template))
            {
                lastNamedTemplate = template;
                SyncNameField();
            }

            RefreshTemplateSummary(template);
        }

        /// <summary>Template last synchronised into the name field.</summary>
        private static LoadoutTemplateRecord lastNamedTemplate;

        /// <summary>Summarise loaded stations, total mass, and role so per-pylon editing exposes the whole
        /// fit's weight.</summary>
        private static void RefreshTemplateSummary(LoadoutTemplateRecord template)
        {
            RefreshLoadoutProfileChrome(template);
            if (templateSummaryLabel == null) return;

            if (template == null)
            {
                templateSummaryLabel.text = selectedOffer == null
                    ? "NO AIRFRAME SELECTED"
                    : "NO SAVED TEMPLATE  ·  STANDARD FIT ONLY";
                templateSummaryLabel.color = selectedOffer == null ? Dim() : Warning();
                return;
            }

            int fitted = 0;
            float mass = 0f;
            float air = 0f;
            float surface = 0f;

            int count = EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                string key = template.KeyAt(i);
                if (string.IsNullOrEmpty(key)) continue;

                WingLoadoutCatalog.StoreOption store =
                    EconomyFacade.LoadoutCatalog.StoreOn(selectedOffer, i, key);
                fitted++;
                mass += store.Mass;
                air += store.AntiAir;
                surface += store.AntiSurface;
            }

            string role = air <= 0f && surface <= 0f ? "unarmed"
                : air > surface * 1.5f ? "air-to-air"
                : surface > air * 1.5f ? "air-to-ground"
                : "multirole";

            templateSummaryLabel.text =
                fitted + " of " + count + " pylons  ·  " + Grouped(mass) + " kg  ·  " +
                role;
            templateSummaryLabel.color = fitted == 0 ? Warning() : Dim();
        }

        /// <summary>Refresh selected-airframe title and silhouette even when station data or a template is
        /// unavailable.</summary>
        private static void RefreshLoadoutProfileChrome(LoadoutTemplateRecord template)
        {
            if (selectedOffer == null)
            {
                if (loadoutProfileTitle != null)
                {
                    loadoutProfileTitle.text = "LOADOUT PROFILE  ·  NO AIRFRAME";
                    loadoutProfileTitle.color = Dim();
                }
                if (loadoutProfileRail != null) loadoutProfileRail.color = Dim();
                if (loadoutProfileIcon != null)
                {
                    loadoutProfileIcon.sprite = IconFactory.Get("airframe");
                    loadoutProfileIcon.color = Dim();
                }
                return;
            }

            string designation = !string.IsNullOrEmpty(selectedOffer.code)
                ? selectedOffer.code
                : AvTheme.Truncate(selectedOffer.unitName, 12);
            string templateName = template != null
                ? AvTheme.Truncate(template.Name, 14).ToUpperInvariant()
                : "NO TEMPLATE";

            if (loadoutProfileTitle != null)
            {
                loadoutProfileTitle.text = "LOADOUT PROFILE  ·  " + designation + "  ·  " + templateName;
                loadoutProfileTitle.color = template != null ? WingUi.RailCyan : Warning();
            }
            if (loadoutProfileRail != null)
                loadoutProfileRail.color = template != null ? WingUi.RailEmerald : Warning();
            if (loadoutProfileIcon != null)
            {
                loadoutProfileIcon.sprite = IconFactory.Aircraft(selectedOffer);
                loadoutProfileIcon.color = template != null ? Friendly() : Dim();
            }
        }

        private static void RefreshPylonRows(LoadoutTemplateRecord template)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(visiblePylons.Count /
                                                     (float)PylonRowsPerPage));
            pylonPage = Mathf.Clamp(pylonPage, 0, pages - 1);

            if (pylonPageLabel != null)
                pylonPageLabel.text = visiblePylons.Count == 0
                    ? "NO READABLE HARDPOINTS"
                    : PageSummary(visiblePylons.Count, pylonPage, pages, "PYLON", "PYLONS");

            bool hasPylons = visiblePylons.Count > 0;
            if (pylonEmptyCard != null) pylonEmptyCard.gameObject.SetActive(!hasPylons);
            if (pylonEmptyLabel != null) pylonEmptyLabel.gameObject.SetActive(!hasPylons);

            pylonPrevButton?.SetEnabled(pylonPage > 0);
            pylonNextButton?.SetEnabled(pylonPage < pages - 1);

            // Build one reusable scratch fit per refresh for consistent exclusion checks. Spawn
            // loadouts remain separately allocated per aircraft.
            Loadout inProgress = template != null
                ? EconomyFacade.LoadoutCatalog.FillScratch(selectedOffer, template.MountKeys)
                : null;

            int first = pylonPage * PylonRowsPerPage;
            for (int i = 0; i < pylonRows.Count; i++)
            {
                int slot = first + i;
                if (template == null || slot >= visiblePylons.Count)
                {
                    pylonRows[i].Hide();
                    continue;
                }

                int pylon = visiblePylons[slot];
                bool blocked = false;
                int represented = MirrorCount(pylon);
                for (int mirror = 0; mirror < represented && !blocked; mirror++)
                    blocked = EconomyFacade.LoadoutCatalog.IsPylonBlocked(
                        selectedOffer, pylon + mirror, inProgress);

                pylonRows[i].Bind(
                    pylon, i,
                    EconomyFacade.LoadoutCatalog.PylonName(selectedOffer, pylon),
                    EconomyFacade.LoadoutCatalog.StoreOn(selectedOffer, pylon, template.KeyAt(pylon)),
                    represented,
                    blocked);
            }
        }

        /// <summary>Number of actual stations represented by this visible row.</summary>
        private static int MirrorCount(int pylon)
        {
            int count = 1;
            int total = EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < total && EconomyFacade.LoadoutCatalog.MirrorsPrevious(selectedOffer, i);
                 i++)
                count++;
            return count;
        }

        private static void RefreshLoadoutStatus(LoadoutTemplateRecord template)
        {
            if (loadoutStatusLabel == null) return;

            if (selectedOffer == null)
            {
                loadoutStatusLabel.text = "No airframe your flight can formate on is in stock.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            if (!EconomyFacade.LoadoutCatalog.Available)
            {
                loadoutStatusLabel.text =
                    "Stock station data unreadable on this build - standard fit only.";
                loadoutStatusLabel.color = Warning();
                return;
            }

            if (EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer) == 0)
            {
                loadoutStatusLabel.text =
                    AvTheme.Truncate(selectedOffer.unitName, 18) +
                    "'s hardpoints cannot be read; it flies its standard fit.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            if (template == null)
            {
                loadoutStatusLabel.text =
                    "Press + to start a template for " +
                    AvTheme.Truncate(selectedOffer.unitName, 18) + ".";
                loadoutStatusLabel.color = Dim();
                return;
            }

            // Explain that saved templates are selected for purchase on SUPPLY.
            loadoutStatusLabel.text = "Saved — choose it on the SUPPLY tab to fly it.";
            loadoutStatusLabel.color = Friendly();
        }

        private static int FittedCount(LoadoutTemplateRecord template) =>
            template == null ? 0 : CountFitted(template.MountKeys);

        private static int CountFitted(IReadOnlyList<string> keys)
        {
            if (keys == null) return 0;

            int fitted = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrEmpty(keys[i])) fitted++;
            }
            return fitted;
        }

        /// <summary>Loadout-control hover descriptions.</summary>
        private static class LoadoutHint
        {
            public const string Airframe =
                "Which airframe's pylons you are editing. Templates belong to one aircraft " +
                "type, because the hardpoints do.";

            public const string Select =
                "Switch between the templates saved for this airframe.";

            public const string New =
                "Start a new empty template and fit it pylon by pylon.";

            public const string Copy =
                "Copy this template, so a variation can be made without losing the original.";

            public const string Delete =
                "Delete this template for good. Aircraft already flying it keep their fit.";

            public const string Name =
                "Name the template. Flight controls are held off while you type here.";

            public const string Pylon =
                "Choose what hangs on this pylon. A symmetric pair is set together.";

            public const string Blocked =
                "Another store already fitted rules this pylon out. Clear that one to use it.";

            public const string BlockedFitted =
                "Another store rules this fitted pylon out. Click to clear this pylon.";
        }

        private sealed class AirframeTile
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly Image[] outline;
            private readonly Image rail;
            private readonly Image icon;
            private readonly TMP_Text code;
            private readonly TMP_Text name;
            private readonly WingButton hit;
            private AircraftDefinition bound;

            public AirframeTile(RectTransform parent, Rect rect, int index)
            {
                go = new GameObject("AirframeTile_" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, rect);

                fill = go.GetComponent<Image>();
                fill.color = WingUi.CardFill;
                fill.raycastTarget = false;

                outline = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor());
                rail = Rule(rt, new Rect(0f, 0f, 3f, rect.height), Color.clear);

                icon = AddSprite(rt, "AirframeIcon", IconFactory.Get("airframe"),
                                 new Rect(4f, -4f, 28f, 28f), Color.white);

                float textLeft = 34f;
                float textWidth = rect.width - textLeft - 2f;
                code = Label(rt, "", new Rect(textLeft, -2f, textWidth, 16f), Friendly(),
                             FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                code.overflowMode = TextOverflowModes.Ellipsis;

                name = Label(rt, "", new Rect(textLeft, -18f, textWidth, 14f), Dim(),
                             FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                name.overflowMode = TextOverflowModes.Ellipsis;

                hit = HitButton(rt, new Rect(0f, 0f, rect.width, rect.height), () =>
                {
                    if (bound != null) SelectAirframe(bound);
                });

                go.SetActive(false);
            }

            public void Bind(AircraftDefinition def, bool selected)
            {
                bound = def;
                if (def == null)
                {
                    if (go.activeSelf) go.SetActive(false);
                    return;
                }

                if (!go.activeSelf) go.SetActive(true);

                Sprite sprite = IconFactory.Aircraft(def);
                icon.sprite = sprite;
                icon.color = selected ? Color.white : Dim();

                string codeStr = !string.IsNullOrEmpty(def.code) ? def.code : def.unitName;
                code.text = AvTheme.Truncate(codeStr, 7);
                code.color = selected ? Green() : Friendly();

                name.text = def.unitName;
                name.color = selected ? Friendly() : Dim();

                fill.color = selected ? WingUi.CardFillSelected : WingUi.CardFill;
                Color frameColor = selected ? Green() : FrameColor();
                if (outline != null)
                {
                    for (int i = 0; i < outline.Length; i++)
                    {
                        if (outline[i] != null) outline[i].color = frameColor;
                    }
                }
                rail.color = selected ? Green() : Color.clear;

                hit.WithTooltip(def.unitName + " — Click to edit hardpoint loadout");
                hit.SetRowHighlight(fill, selected ? WingUi.CardFillSelected : WingUi.CardFill, WingUi.CardFillHover);
            }
        }

        /// <summary>Clickable pylon row with native name and fitted store. Keep blocked stations visible
        /// with a reason; permit clearing existing conflicting stores.</summary>
        private sealed class PylonRow
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly TMP_Text name;
            private readonly TMP_Text store;
            private readonly WingButton hit;

            public PylonRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Pylon" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = go.GetComponent<Image>();
                fill.color = WingUi.CardFill;
                fill.raycastTarget = false;
                Outline(rt, new Rect(0f, 0f, width, RowHeight), FrameColor());

                name = Label(rt, "", new Rect(Space2, 0f, 140f, RowHeight), Friendly(), FontSmall,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                store = Label(rt, "", new Rect(152f, 0f, width - 152f - Space2, RowHeight),
                              Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);

                hit = HitButton(rt, new Rect(0f, 0f, width, RowHeight), null);
                go.SetActive(false);
            }

            public void Bind(int pylon, int rowIndex, string pylonName,
                             WingLoadoutCatalog.StoreOption fitted, int mirrors, bool blocked)
            {
                if (!go.activeSelf) go.SetActive(true);

                // Label mirrored pairs explicitly to explain the reduced row count.
                name.text = mirrors > 1
                    ? AvTheme.Truncate(pylonName, 20) + "  x" + mirrors
                    : AvTheme.Truncate(pylonName, 24);

                if (blocked)
                {
                    store.text = "— BLOCKED —";
                    store.color = Warning();
                    name.color = Dim();

                    // Allow clearing an already-fitted blocked station so the player can repair
                    // conflicting loadouts.
                    bool canClear = !fitted.IsEmpty;
                    hit.SetAction(canClear ? () => SetStore(pylon, null) : (Action)null);
                    hit.SetEnabled(canClear);
                    hit.WithTooltip(canClear ? LoadoutHint.BlockedFitted : LoadoutHint.Blocked);
                    hit.SetRowHighlight(fill, WingUi.CardFill,
                                        canClear ? WingUi.CardFillHover : WingUi.CardFill);
                    return;
                }

                bool empty = fitted.IsEmpty;
                store.text = empty ? "— EMPTY —" : fitted.Label;
                store.color = empty ? Dim() : Friendly();
                name.color = Friendly();

                hit.SetEnabled(true);
                hit.WithTooltip(pylonName + " / " + (empty ? "Empty" : fitted.Label) + ". " + LoadoutHint.Pylon);
                hit.SetAction(() => OpenStorePicker(pylon, rowIndex));
                hit.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }

    }
}
