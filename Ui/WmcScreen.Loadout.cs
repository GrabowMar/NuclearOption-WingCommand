using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>LOADOUT page: build summary, airframe grid, template bar and flexible hardpoint rows.
    /// Everything here edits saved presets; SUPPLY applies a preset to a purchase. No control on this
    /// page changes an aircraft that is already flying.</summary>
    internal static partial class WmcScreen
    {
        // ---------------------------------------------------------------- page state

        private static TMP_Text loadoutStatusLabel;
        private static TMP_Text loadoutProfileTitle;
        private static Image loadoutProfileRail;
        private static Image loadoutProfileIcon;
        private static TMP_Text loadoutSavedLabel;
        private static TMP_Text loadoutChainLabel;
        private static TMP_Text hardpointCaptionLabel;

        // Build-anchor readouts: station count with pips, total mass and role.
        private static TMP_Text loadoutStationsLabel;
        private static TMP_Text loadoutMassLabel;
        private static TMP_Text loadoutRoleLabel;
        private static StationPips loadoutStationPips;

        private static TMP_Text templateLabel;
        private static TMP_InputField templateNameField;
        private static TMP_Text templateSummaryLabel;
        private static TMP_Text liveryLabel;
        private static RectTransform pylonPagerRoot;
        private static float pylonAreaFull;

        /// <summary>Rows plus card footer between the column headers and the pager, derived from
        /// BodyHeight at build.</summary>
        private static float pylonRegion;

        private static TMP_Text airframeEmptyLabel;
        private static WingButton templateSelectButton;
        private static WingButton templateNewButton;
        private static WingButton templateCopyButton;
        private static WingButton templateDeleteButton;
        private static readonly Confirmation templateDeletion = new Confirmation();

        private static RectTransform pylonArea;
        private static RectTransform pylonEmptyCard;
        private static Image pylonEmptyRail;
        private static TMP_Text pylonEmptyLabel;
        private static WingButton pylonEmptyHit;
        private static Image hardpointFooterRule;
        private static TMP_Text hardpointTotalLabel;
        private static TMP_Text hardpointMassLabel;
        private static TMP_Text hardpointNoteLabel;
        private static WingButton pylonPrevButton;
        private static WingButton pylonNextButton;
        private static TMP_Text pylonPageLabel;
        private static readonly List<PylonRow> pylonRows = new List<PylonRow>();

        /// <summary>Bounded body viewport for the whole page; content coordinates start at the body
        /// top. The store popup lives on this content so it clamps to the visible body.</summary>
        private static RectTransform loadoutBody;
        private static ScrollRect loadoutScroll;

        private static int airframePage;
        private static RectTransform airframePager;
        private static WingButton airframePrevButton;
        private static WingButton airframeNextButton;
        private static TMP_Text airframePageLabel;
        private static readonly List<AirframeTile> airframeTiles = new List<AirframeTile>();

        /// <summary>Current pylon page for the edited airframe.</summary>
        private static int pylonPage;

        /// <summary>Pylon-list origin for row-aligned store popups, content-local.</summary>
        private static float pylonAreaY;

        /// <summary>Visible pylon rows and their height, derived from BodyHeight at build.</summary>
        private static int pylonRowsVisible = PylonRowMinRows;
        private static float pylonRowHeight = PylonRowMinHeight;
        private static float airframeTileHeight = AirframeTileShort;

        /// <summary>Why template actions are unavailable; surfaced in tooltips and the status
        /// line.</summary>
        private static string templateDisabledReason;

        /// <summary>Popup entries rebuilt when opened.</summary>
        private static readonly List<AvKit.PopupEntry> popupEntries = new List<AvKit.PopupEntry>();

        private static readonly List<WingLoadoutCatalog.StoreOption> storeScratch =
            new List<WingLoadoutCatalog.StoreOption>();

        private static AvKit.Popup loadoutPopup;
        private static AvKit.Popup shopTemplatePopup;

        /// <summary>Resolve the edited template by stable ID so deletion cannot leave a stale record
        /// active.</summary>
        private static string editingTemplateId;

        /// <summary>Template last synchronised into the name field.</summary>
        private static LoadoutTemplateRecord lastNamedTemplate;

        // ---------------------------------------------------------------- geometry

        private const int AirframeGridRows = 2;
        private const int AirframeGridCols = 3;
        private const int AirframeGridCapacity = AirframeGridRows * AirframeGridCols;
        private const float AirframeTileGap = 4f;
        private const float AirframeTileShort = 40f;
        private const float AirframeTileTall = 44f;

        /// <summary>Body height above which the roomier tiles are used: 688 at panel 896, 388 at
        /// panel 596. Pylon row count and height come from the space left below the headers.</summary>
        private const float TallBodyHeight = 560f;

        private const int PylonRowMinRows = 5;
        private const int PylonRowMaxRows = 8;
        private const float PylonRowMinHeight = 36f;
        private const float PylonRowMaxHeight = 48f;
        private const float LoadoutRowGap = 4f;

        /// <summary>Pager under the hardpoint list. Livery lives on SUPPLY.</summary>
        private const float HardpointFooterHeight = LoadoutRowGap + KeyHeight;

        /// <summary>Caption, value and pips of one build-summary readout.</summary>
        private const float SummaryReadoutHeight = 42f;

        private const float SummaryCardHeight = 124f;

        /// <summary>Card footer under the hardpoint rows: hidden below this height, second line
        /// (unfitted stations) above the taller one.</summary>
        private const float FooterMinHeight = 22f;
        private const float FooterNoteHeight = 40f;

        private const float PylonNameWidth = 168f;
        private const float PylonStoreX = 180f;
        private const float PylonStoreWidth = 148f;
        private const float PylonMassX = 332f;
        private const float PylonMassWidth = 48f;
        private const float PylonActionX = 384f;
        private const float PylonActionWidth = 52f;

        private static readonly Column[] PylonColumns =
        {
            new Column("STATION", 8f, PylonNameWidth),
            new Column("STORE", PylonStoreX, PylonStoreWidth),
            new Column("MASS", PylonMassX, PylonMassWidth, rightAligned: true),
            new Column("ACTION", PylonActionX, PylonActionWidth, rightAligned: true),
        };

        // Loadout-page construction.

        /// <summary>Build persistent templates from explicit per-pylon choices without changing live
        /// aircraft or funds. SUPPLY selects the template for a purchase; WING displays airborne
        /// fits.</summary>
        private static float AddLoadoutPage(RectTransform parent, float y)
        {
            float bodyHeight = BodyHeight;
            BuildViewport(parent, new Rect(0f, y, PageWidth, bodyHeight), "LoadoutViewport",
                          out loadoutBody, out loadoutScroll);
            pageScrolls[(int)Page.Loadout] = loadoutScroll;

            loadoutPopup = new AvKit.Popup(loadoutBody, PageWidth);

            float cy = 0f;
            cy = AddBuildSummary(loadoutBody, cy);
            cy = AddAirframeGrid(loadoutBody, cy);
            cy = AddTemplateBar(loadoutBody, cy);
            cy = AddHardpoints(loadoutBody, cy);

            // Reflow pads content by Pad; hand it a pre-padded bottom so a tall body fits exactly
            // and a short one scrolls to the livery row.
            Reflow(loadoutScroll, cy + Pad);
            return y + cy;
        }

        /// <summary>Labelled readout: micro caption over a bold value.</summary>
        private static TMP_Text SummaryReadout(RectTransform parent, string caption, Rect area,
                                               float valueSize)
        {
            Label(parent, caption, new Rect(area.x, area.y, area.width, 12f), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            return Label(parent, "—", new Rect(area.x, area.y - 13f, area.width, 18f),
                         WingUi.TextPrimary, valueSize, FontStyles.Bold, TextAlignmentOptions.Left);
        }

        /// <summary>Build summary: silhouette, designation, template name and saved state, then
        /// labelled station, mass and role readouts over the airframe -> template -> stations
        /// chain.</summary>
        private static float AddBuildSummary(RectTransform parent, float y)
        {
            float w = ContentWidth;
            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, SummaryCardHeight),
                                                WingUi.RailCyan);
            loadoutProfileRail = rail;

            loadoutProfileIcon = AddSprite(parent, "LoadoutProfileAirframe", IconFactory.Get("airframe"),
                                           new Rect(Pad + Space2, y - Space3 - 4f, 48f, 34f), Dim());

            float textX = Pad + Space2 + 48f + Space3;
            float textW = Pad + w - Space3 - textX;
            const float stateWidth = 112f;

            loadoutProfileTitle = Label(parent, "NO AIRFRAME SELECTED",
                                        new Rect(textX, y - 6f, textW - stateWidth - Gap, 18f),
                                        WingUi.TextPrimary, FontLead, FontStyles.Bold,
                                        TextAlignmentOptions.Left);
            loadoutProfileTitle.overflowMode = TextOverflowModes.Ellipsis;

            loadoutSavedLabel = Label(parent, "", new Rect(textX + textW - stateWidth, y - 6f,
                                                            stateWidth, 18f),
                                      Dim(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            // Labelled readouts carry the build anchor: station count with pips, mass and role.
            const float stationW = 140f;
            const float massX = 230f;
            const float massW = 88f;
            const float roleX = 326f;
            float roleW = textX + textW - roleX;
            float bandY = y - 30f;

            loadoutStationsLabel = SummaryReadout(parent, "STATIONS",
                                                  new Rect(textX, bandY, stationW, SummaryReadoutHeight),
                                                  FontBody);
            loadoutStationPips = new StationPips(parent, textX, bandY - 33f);
            loadoutMassLabel = SummaryReadout(parent, "MASS",
                                              new Rect(massX, bandY, massW, SummaryReadoutHeight),
                                              FontBody);
            loadoutRoleLabel = SummaryReadout(parent, "ROLE",
                                              new Rect(roleX, bandY, roleW, SummaryReadoutHeight),
                                              FontBody);

            // Role detail: the store mix behind the role word.
            templateSummaryLabel = Label(parent, "", new Rect(roleX, bandY - 31f, roleW, 12f),
                                         Dim(), FontMicro, FontStyles.Normal,
                                         TextAlignmentOptions.Left);
            templateSummaryLabel.overflowMode = TextOverflowModes.Ellipsis;

            Rule(parent, new Rect(massX - Space2, bandY, 1f, SummaryReadoutHeight), WingUi.BorderSubtle);
            Rule(parent, new Rect(roleX - Space2, bandY, 1f, SummaryReadoutHeight), WingUi.BorderSubtle);

            Label(parent, "NAME", new Rect(textX, y - 74f, 38f, 26f), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            float nameX = textX + 42f;
            float nameW = textW - 42f;

            if (WingKeyboardGuard.Available)
            {
                templateLabel = null;
                templateNameField = WingUi.InputField(
                    parent, new Rect(nameX, y - 74f, nameW, 26f),
                    EconomyFacade.LoadoutTemplates.MaxNameLength, RenameTemplate, LoadoutHint.Name);
            }
            else
            {
                templateNameField = null;
                Panel(parent, new Rect(nameX, y - 74f, nameW, 26f), RowColor());
                templateLabel = Label(parent, "",
                                      new Rect(nameX + Space2, y - 74f, nameW - Space4, 26f),
                                      Dim(), FontBody, FontStyles.Normal,
                                      TextAlignmentOptions.MidlineLeft);
            }

            loadoutChainLabel = Label(parent, "", new Rect(textX, y - 104f, textW, 14f),
                                      Dim(), FontMicro, FontStyles.Normal,
                                      TextAlignmentOptions.Left);
            loadoutChainLabel.overflowMode = TextOverflowModes.Ellipsis;

            float bottom = y - SummaryCardHeight;
            loadoutStatusLabel = Label(parent, "", new Rect(Pad + 2f, bottom - 4f, w - 4f, 14f),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.Left);
            loadoutStatusLabel.enableWordWrapping = false;
            loadoutStatusLabel.overflowMode = TextOverflowModes.Ellipsis;

            return bottom - 4f - 14f - Gap;
        }

        /// <summary>Airframe picker: header with pager, then a 2x3 tile grid.</summary>
        private static float AddAirframeGrid(RectTransform parent, float y)
        {
            airframeTiles.Clear();
            float w = ContentWidth;
            airframeTileHeight = BodyHeight >= TallBodyHeight ? AirframeTileTall : AirframeTileShort;

            float gridTop = SectionHeader(parent, Pad, y, w, "AIRFRAME");
            float colWidth = (w - (AirframeGridCols - 1) * AirframeTileGap) / AirframeGridCols;

            airframeEmptyLabel = Label(parent,
                "NO COMPATIBLE AIRFRAMES\nAvailable aircraft types appear here.",
                new Rect(Pad + Space2, gridTop - Space2, w - Space4,
                         AirframeGridRows * airframeTileHeight - Space4),
                Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            airframeEmptyLabel.enableWordWrapping = true;

            for (int r = 0; r < AirframeGridRows; r++)
            {
                float rowY = gridTop - r * (airframeTileHeight + AirframeTileGap);
                for (int c = 0; c < AirframeGridCols; c++)
                {
                    float tileX = Pad + c * (colWidth + AirframeTileGap);
                    int index = r * AirframeGridCols + c;
                    airframeTiles.Add(new AirframeTile(parent,
                        new Rect(tileX, rowY, colWidth, airframeTileHeight), index));
                }
            }

            // Build the pager after tiles so shared-border rounding cannot let a tile intercept its
            // arrows, then pull it back inside the content column.
            airframePager = HeaderPager(parent, y - 2f, () => TurnAirframePage(-1),
                                        () => TurnAirframePage(1), out airframePrevButton,
                                        out airframePageLabel, out airframeNextButton);
            Place(airframePager, new Rect(Pad + w - HeaderPagerWidth, y - 2f,
                                          HeaderPagerWidth, HeaderPagerHeight));
            airframePager.SetAsLastSibling();

            float gridBottom = gridTop -
                (AirframeGridRows * airframeTileHeight + (AirframeGridRows - 1) * AirframeTileGap);
            return gridBottom - Gap;
        }

        /// <summary>Template bar: selector plus NEW / COPY / DELETE, the destructive control separated
        /// from the group.</summary>
        private static float AddTemplateBar(RectTransform parent, float y)
        {
            float w = ContentWidth;
            const float barH = 22f;
            const float actionW = 64f;
            const float deleteSeparation = 8f;
            float selectW = w - (actionW * 3f + 4f * 2f + deleteSeparation);
            Image track = Panel(parent, new Rect(Pad, y, w - actionW - deleteSeparation, barH), WingUi.CardFill);
            track.sprite = AvSprites.Slot;
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;

            float pickerY = y;
            templateSelectButton = WingUi.Button(parent, "", new Rect(Pad + 2f, y - 2f, selectW - 4f, barH - 4f),
                                                 FontMicro, UiButtonStyle.Default,
                                                 () => OpenTemplatePicker(Pad, pickerY, selectW))
                                        .WithTooltip(LoadoutHint.Select);

            float x = Pad + selectW + 2f;
            templateNewButton = WingUi.Button(parent, "NEW", new Rect(x, y - 2f, actionW - 4f, barH - 4f),
                                              FontMicro, UiButtonStyle.Default, NewTemplate)
                                     .WithTooltip(LoadoutHint.New);
            StampKey(templateNewButton, "tasking", WingUi.RailCyan, barH - 4f);
            x += actionW;
            templateCopyButton = WingUi.Button(parent, "COPY", new Rect(x, y - 2f, actionW - 4f, barH - 4f),
                                               FontMicro, UiButtonStyle.Default, CopyTemplate)
                                      .WithTooltip(LoadoutHint.Copy);
            StampKey(templateCopyButton, "formation", WingUi.RailCyan, barH - 4f);
            x += actionW + deleteSeparation;
            templateDeleteButton = WingUi.Button(parent, "DELETE", new Rect(x, y, actionW, barH),
                                                 FontMicro, UiButtonStyle.Danger, DeleteTemplate)
                                        .WithTooltip(LoadoutHint.Delete);
            StampKey(templateDeleteButton, "fallback", Alert(), barH);

            return y - barH - Gap;
        }

        /// <summary>Hardpoints: caption, column headers, flexible rows, a card footer carrying the
        /// fit totals and the pager anchored under the card.</summary>
        private static float AddHardpoints(RectTransform parent, float y)
        {
            float w = ContentWidth;

            float captionBottom = SectionHeader(parent, Pad, y, w, "HARDPOINTS");
            hardpointCaptionLabel = Label(parent, "", new Rect(Pad + 10f, y - 1f, w - 10f, 14f),
                                          Dim(), FontMicro, FontStyles.Normal,
                                          TextAlignmentOptions.Right);

            float rowsTop = ColumnHeaders(parent, captionBottom - 2f, PylonColumns);

            // Fewest rows that fit at the 48-unit ceiling: rows grow from 36 towards 48 with the
            // body, and the card footer absorbs whatever the page leaves below them.
            pylonRegion = Mathf.Max(0f, rowsTop - (-BodyHeight + HardpointFooterHeight));
            pylonRowsVisible = Mathf.Clamp(Mathf.FloorToInt(pylonRegion / PylonRowMaxHeight),
                                           PylonRowMinRows, PylonRowMaxRows);
            pylonRowHeight = Mathf.Clamp(pylonRegion / pylonRowsVisible,
                                         PylonRowMinHeight, PylonRowMaxHeight);
            pylonAreaFull = Mathf.Max(pylonRegion, pylonRowsVisible * pylonRowHeight);

            var area = new GameObject("PylonArea", typeof(RectTransform));
            pylonArea = area.GetComponent<RectTransform>();
            pylonArea.SetParent(parent, worldPositionStays: false);
            float areaHeight = pylonAreaFull;
            Place(pylonArea, new Rect(Pad, rowsTop, w, areaHeight));
            pylonAreaY = rowsTop;

            var (emptyBg, emptyRail) = WingUi.TacticalCard(pylonArea, new Rect(0f, 0f, w, areaHeight),
                                                           WingUi.RailInert);
            emptyBg.color = AvTheme.SurfaceInert;
            pylonEmptyCard = emptyBg.rectTransform;
            pylonEmptyRail = emptyRail;
            pylonEmptyLabel = Label(pylonEmptyCard,
                "NO TEMPLATE SELECTED  ·  CLICK TO CREATE [+ NEW]\nConfigure custom stores and pylon mounts for this airframe.",
                new Rect(Space4, -(areaHeight - 44f) * 0.5f, w - Space4 * 2f, 44f),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            pylonEmptyLabel.enableWordWrapping = true;
            pylonEmptyHit = WingUi.HitButton(pylonEmptyCard, new Rect(0f, 0f, w, areaHeight), NewTemplate)
                                 .WithTooltip("Click to create a new custom template for this airframe");

            // Card footer: fit totals on the first line, unfitted stations on the second when the
            // card is tall enough. It is what lets the card reach the pager at the body foot.
            hardpointFooterRule = Rule(pylonArea, new Rect(1f, -areaHeight, w - 2f, 1f),
                                       WingUi.BorderSubtle);
            hardpointTotalLabel = Label(pylonArea, "", new Rect(10f, -areaHeight, w - 20f, 14f),
                                        Dim(), FontMicro, FontStyles.Normal,
                                        TextAlignmentOptions.Left);
            hardpointMassLabel = Label(pylonArea, "", new Rect(PylonMassX, -areaHeight,
                                                               PylonMassWidth, 14f),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.MidlineRight);
            hardpointNoteLabel = Label(pylonArea, "", new Rect(10f, -areaHeight, w - 20f, 14f),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.Left);
            hardpointFooterRule.gameObject.SetActive(false);
            hardpointTotalLabel.gameObject.SetActive(false);
            hardpointMassLabel.gameObject.SetActive(false);
            hardpointNoteLabel.gameObject.SetActive(false);

            pylonRows.Clear();
            for (int i = 0; i < pylonRowsVisible; i++)
                pylonRows.Add(new PylonRow(pylonArea, i, pylonRowHeight));

            float pagerY = rowsTop - areaHeight - LoadoutRowGap;
            pylonPagerRoot = PageRoot(parent, "PylonPager");
            Place(pylonPagerRoot, new Rect(Pad, pagerY, w, KeyHeight));
            (pylonPrevButton, pylonPageLabel, pylonNextButton) = PagerRow(
                pylonPagerRoot, 0f, w, () => TurnPylonPage(-1), () => TurnPylonPage(1),
                null, KeyHeight);

            return pagerY - KeyHeight;
        }

        /// <summary>Fixed-height viewport container used by the pilot roster on WING.</summary>
        private static RectTransform RosterViewport(RectTransform parent, string name, float y, int rowCount = RosterRowsPerPage)
        {
            var area = new GameObject(name, typeof(RectTransform));
            RectTransform rt = area.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, new Rect(Pad, y, PageWidth - Pad * 2f, RowPitch * rowCount));
            return rt;
        }

        private static void SelectAirframe(AircraftDefinition def)
        {
            if (def == null || selectedOffer == def) return;
            templateDeletion.Clear();
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

        private static void RefreshAirframeGrid()
        {
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.LoadoutCatalogue();

            if (airframeEmptyLabel != null) airframeEmptyLabel.gameObject.SetActive(offers.Count == 0);
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
                    "No templates for " + selectedOffer.unitName + " yet - select NEW to make one");
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
                templateDeletion.Clear();
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
            templateDeletion.Clear();
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
            templateDeletion.Clear();
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
            if (!templateDeletion.IsArmedFor(doomed))
            {
                templateDeletion.Arm(doomed);
                WingCommandManager.Instance?.Toast("Select DELETE again within 3 seconds to delete " + name);
                RefreshTemplateControls(doomed);
                RefreshLoadoutStatus(doomed);
                return;
            }

            templateDeletion.Clear();
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

            // Open the store list below its pylon row; the popup clamps itself into the viewport.
            float rowY = pylonAreaY - pylonRowHeight * rowIndex - pylonRowHeight;
            loadoutPopup?.Show(new Rect(Pad, rowY, ContentWidth, 0f), popupEntries, index =>
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
            RefreshTemplateSummary(template);
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

        /// <summary>Update the template bar; disabled controls carry their reason in the tooltip and
        /// the status line.</summary>
        private static void RefreshTemplateControls(LoadoutTemplateRecord template)
        {
            bool haveAirframe = selectedOffer != null;
            bool readable = haveAirframe && EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer) > 0;
            int saved = haveAirframe ? EconomyFacade.LoadoutTemplates.CountFor(selectedOffer) : 0;
            int max = EconomyFacade.LoadoutTemplates.MaxPerAirframe;
            bool atLimit = saved >= max;

            templateDisabledReason = !haveAirframe ? "Select an airframe to edit its saved presets."
                : !readable ? "Hardpoint data is unavailable; this airframe uses its standard fit."
                : atLimit ? "Template limit reached (" + max + ") — delete a template to make room."
                : null;

            if (templateSelectButton != null)
            {
                templateSelectButton.SetText(
                    template != null ? AvTheme.Truncate(template.Name, 22).ToUpperInvariant()
                    : saved > 0 ? "SELECT A TEMPLATE"
                    : "NO TEMPLATES");
                templateSelectButton.SetEnabled(saved > 0);
                templateSelectButton.SetLatched(template != null);
                templateSelectButton.WithTooltip(saved > 0
                    ? LoadoutHint.Select
                    : "No saved presets for this airframe yet — NEW makes one.");
            }

            if (templateNewButton != null)
            {
                templateNewButton.SetEnabled(readable && !atLimit);
                templateNewButton.WithTooltip(!haveAirframe ? "Select an airframe first."
                    : !readable ? "Hardpoint data is unavailable; this airframe uses its standard fit."
                    : atLimit ? "Template limit reached (" + max + "). Delete a template to make room."
                    : LoadoutHint.New);
            }

            if (templateCopyButton != null)
            {
                templateCopyButton.SetEnabled(template != null && !atLimit);
                templateCopyButton.WithTooltip(template == null ? "Select a template to copy."
                    : atLimit ? "Template limit reached. Delete a template to copy over."
                    : LoadoutHint.Copy);
            }

            if (templateDeleteButton != null)
            {
                bool deleteArmed = templateDeletion.IsArmedFor(template);
                templateDeleteButton.SetEnabled(template != null);
                templateDeleteButton.SetLatched(deleteArmed);
                templateDeleteButton.SetText(deleteArmed ? "DELETE?" : "DELETE");
                templateDeleteButton.WithTooltip(template == null ? "Nothing to delete."
                    : deleteArmed ? "Press again within 3 seconds to delete " + template.Name +
                                    ". Aircraft already flying it keep their fit."
                    : LoadoutHint.Delete);
            }

            // Refresh name text only when the edited template changes.
            if (!ReferenceEquals(lastNamedTemplate, template))
            {
                lastNamedTemplate = template;
                SyncNameField();
            }
        }

        /// <summary>Summarise the saved fit into the build-anchor readouts: station count with pips,
        /// total mass, role and the store mix behind it.</summary>
        private static void RefreshTemplateSummary(LoadoutTemplateRecord template)
        {
            RefreshLoadoutProfileChrome(template);
            RefreshHardpointCaption(template);
            if (loadoutStationsLabel == null) return;

            FitTotals fit = MeasureFit(template);
            loadoutStationPips?.Set(fit.Fitted, fit.Stations);

            if (selectedOffer == null)
            {
                loadoutStationsLabel.text = "—";
                loadoutStationsLabel.color = Dim();
                loadoutMassLabel.text = "—";
                loadoutMassLabel.color = Dim();
                loadoutRoleLabel.text = "—";
                loadoutRoleLabel.color = Dim();
                templateSummaryLabel.text = "";
                return;
            }

            if (template == null)
            {
                loadoutStationsLabel.text = "0 / " + fit.Stations;
                loadoutStationsLabel.color = Warning();
                loadoutMassLabel.text = "—";
                loadoutMassLabel.color = Dim();
                loadoutRoleLabel.text = "—";
                loadoutRoleLabel.color = Dim();
                templateSummaryLabel.text = "NO SAVED PRESET";
                templateSummaryLabel.color = Warning();
                return;
            }

            loadoutStationsLabel.text = fit.Fitted + " / " + fit.Stations;
            loadoutStationsLabel.color = fit.Fitted == 0 ? Warning() : WingUi.TextPrimary;
            loadoutMassLabel.text = Grouped(fit.Mass) + " kg";
            loadoutMassLabel.color = fit.Fitted == 0 ? Dim() : WingUi.TextPrimary;
            loadoutRoleLabel.text = FitRole(fit).ToUpperInvariant();
            loadoutRoleLabel.color = fit.Fitted == 0 ? Warning() : Friendly();
            templateSummaryLabel.text = StoreMix(fit);
            templateSummaryLabel.color = Dim();
        }

        /// <summary>One pass over the saved fit: stations, fitted count, mass and store mix.</summary>
        private struct FitTotals
        {
            public int Stations;
            public int Fitted;
            public float Mass;
            public int Air;
            public int Surface;
            public int Cargo;
            public float AirScore;
            public float SurfaceScore;
        }

        private static FitTotals MeasureFit(LoadoutTemplateRecord template)
        {
            var fit = new FitTotals();
            fit.Stations = selectedOffer != null
                ? EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer)
                : 0;
            if (template == null) return fit;

            for (int i = 0; i < fit.Stations; i++)
            {
                string key = template.KeyAt(i);
                if (string.IsNullOrEmpty(key)) continue;

                WingLoadoutCatalog.StoreOption store =
                    EconomyFacade.LoadoutCatalog.StoreOn(selectedOffer, i, key);
                fit.Fitted++;
                fit.Mass += store.Mass;

                // StoreOption exposes cargo plus A-A/A-G effectiveness; missile-defence values are
                // not projected by the catalogue, so they cannot colour the role.
                if (store.Cargo)
                {
                    fit.Cargo++;
                    continue;
                }
                fit.AirScore += store.AntiAir;
                fit.SurfaceScore += store.AntiSurface;
                if (store.AntiAir > 0f) fit.Air++;
                if (store.AntiSurface > 0f) fit.Surface++;
            }

            return fit;
        }

        private static string FitRole(FitTotals fit) =>
            fit.AirScore <= 0f && fit.SurfaceScore <= 0f
                ? fit.Cargo > 0 ? "transport" : "unarmed"
                : fit.AirScore > fit.SurfaceScore * 1.5f ? "air-to-air"
                : fit.SurfaceScore > fit.AirScore * 1.5f ? "air-to-ground"
                : "multirole";

        /// <summary>Store mix behind the role word, e.g. "A-A 4 · A-G 2".</summary>
        private static string StoreMix(FitTotals fit)
        {
            string mix = "";
            if (fit.Air > 0) mix = "A-A " + fit.Air;
            if (fit.Surface > 0) mix += (mix.Length > 0 ? "  ·  " : "") + "A-G " + fit.Surface;
            if (fit.Cargo > 0) mix += (mix.Length > 0 ? "  ·  " : "") + "CARGO " + fit.Cargo;
            return mix;
        }

        /// <summary>Right-aligned caption naming the airframe and template currently edited.</summary>
        private static void RefreshHardpointCaption(LoadoutTemplateRecord template)
        {
            if (hardpointCaptionLabel == null) return;

            if (selectedOffer == null)
            {
                hardpointCaptionLabel.text = "NO AIRFRAME";
                hardpointCaptionLabel.color = Dim();
                return;
            }

            string code = !string.IsNullOrEmpty(selectedOffer.code)
                ? selectedOffer.code
                : AvTheme.Truncate(selectedOffer.unitName, 12);
            string preset = template != null
                ? AvTheme.Truncate(template.Name, 18).ToUpperInvariant()
                : "NO TEMPLATE";

            hardpointCaptionLabel.text = code + "  ·  " + preset;
            hardpointCaptionLabel.color = template != null ? Friendly() : Warning();
        }

        /// <summary>Refresh selected-airframe title, saved state and silhouette even when station data
        /// or a template is unavailable.</summary>
        private static void RefreshLoadoutProfileChrome(LoadoutTemplateRecord template)
        {
            if (selectedOffer == null)
            {
                if (loadoutProfileTitle != null)
                {
                    loadoutProfileTitle.text = "NO AIRFRAME SELECTED";
                    loadoutProfileTitle.color = Dim();
                }
                if (loadoutProfileRail != null) loadoutProfileRail.color = Dim();
                if (loadoutProfileIcon != null)
                {
                    loadoutProfileIcon.sprite = IconFactory.Get("airframe");
                    loadoutProfileIcon.enabled = loadoutProfileIcon.sprite != null;
                    loadoutProfileIcon.color = Dim();
                }
                if (loadoutSavedLabel != null)
                {
                    loadoutSavedLabel.text = "—";
                    loadoutSavedLabel.color = Dim();
                }
                if (loadoutChainLabel != null)
                {
                    loadoutChainLabel.text = "Pick an airframe, then a saved preset, to see its fit.";
                    loadoutChainLabel.color = Dim();
                }
                return;
            }

            string designation = !string.IsNullOrEmpty(selectedOffer.code)
                ? selectedOffer.code
                : AvTheme.Truncate(selectedOffer.unitName, 12);
            string templateName = template != null
                ? AvTheme.Truncate(template.Name, 18).ToUpperInvariant()
                : "NO TEMPLATE";

            if (loadoutProfileTitle != null)
            {
                loadoutProfileTitle.text = designation + "  ·  " + templateName;
                loadoutProfileTitle.color = template != null ? WingUi.TextPrimary : Dim();
            }
            if (loadoutProfileRail != null)
                loadoutProfileRail.color = template != null ? WingUi.RailEmerald : Warning();
            if (loadoutProfileIcon != null)
            {
                loadoutProfileIcon.sprite = IconFactory.Aircraft(selectedOffer);
                loadoutProfileIcon.enabled = loadoutProfileIcon.sprite != null;
                loadoutProfileIcon.color = template != null ? Friendly() : Dim();
            }
            if (loadoutSavedLabel != null)
            {
                loadoutSavedLabel.text = template != null ? "SAVED" : "NO TEMPLATE";
                loadoutSavedLabel.color = template != null ? Green() : Warning();
            }
            if (loadoutChainLabel != null)
            {
                int stations = EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer);
                loadoutChainLabel.text = designation + "  >  " + templateName + "  >  " +
                    stations + " STATIONS  ·  APPLIED ON SUPPLY · FIT";
                loadoutChainLabel.color = template != null ? Dim() : Warning();
            }
        }

        private static void RefreshPylonRows(LoadoutTemplateRecord template)
        {
            int perPage = Mathf.Max(1, pylonRowsVisible);
            int pages = Mathf.Max(1, Mathf.CeilToInt(visiblePylons.Count / (float)perPage));
            pylonPage = Mathf.Clamp(pylonPage, 0, pages - 1);

            bool hasPylons = visiblePylons.Count > 0 && template != null;
            bool creatable = selectedOffer != null &&
                             EconomyFacade.LoadoutCatalog.PylonCount(selectedOffer) > 0;
            if (pylonEmptyCard != null) pylonEmptyCard.gameObject.SetActive(!hasPylons);
            if (pylonEmptyLabel != null)
            {
                pylonEmptyLabel.gameObject.SetActive(!hasPylons);
                pylonEmptyLabel.text = selectedOffer == null ? "SELECT AN AIRFRAME TO VIEW HARDPOINTS"
                    : visiblePylons.Count == 0 ? "HARDPOINT DATA UNAVAILABLE\nThis airframe flies its standard fit."
                    : "NO TEMPLATE SELECTED  ·  CLICK TO CREATE [+ NEW]\nConfigure custom stores and pylon mounts for this airframe.";
                pylonEmptyLabel.enableWordWrapping = true;
            }
            if (pylonEmptyHit != null)
            {
                // Inert card unless creating a template is actually possible.
                bool canCreate = !hasPylons && creatable;
                pylonEmptyHit.SetEnabled(canCreate);
                pylonEmptyHit.WithTooltip(canCreate
                    ? "Click to create a new custom template for this airframe"
                    : selectedOffer == null
                    ? "Select an airframe first."
                    : "Hardpoint data is unavailable; this airframe uses its standard fit.");
            }
            if (pylonEmptyRail != null)
                pylonEmptyRail.color = hasPylons || creatable ? WingUi.RailCyan : WingUi.RailInert;

            if (pylonPageLabel != null)
                pylonPageLabel.text = visiblePylons.Count == 0
                    ? "NO READABLE HARDPOINTS"
                    : template == null
                    ? "—"
                    : PageSummary(visiblePylons.Count, pylonPage, pages, "HARDPOINT", "HARDPOINTS");

            pylonPrevButton?.SetEnabled(hasPylons && pylonPage > 0);
            pylonNextButton?.SetEnabled(hasPylons && pylonPage < pages - 1);

            int shown = hasPylons ? Mathf.Min(perPage, Mathf.Max(0, visiblePylons.Count - pylonPage * perPage)) : 0;
            float areaH = pylonAreaFull;
            if (pylonArea != null)
            {
                pylonArea.sizeDelta = new Vector2(pylonArea.sizeDelta.x, areaH);
                if (pylonEmptyCard != null)
                    pylonEmptyCard.sizeDelta = new Vector2(ContentWidth, areaH);
                if (pylonPagerRoot != null)
                    Place(pylonPagerRoot, new Rect(Pad, pylonAreaY - areaH - LoadoutRowGap, ContentWidth, KeyHeight));
            }
            RefreshHardpointFooter(template, shown);

            // Build one reusable scratch fit per refresh for consistent exclusion checks. Spawn
            // loadouts remain separately allocated per aircraft.
            Loadout inProgress = template != null
                ? EconomyFacade.LoadoutCatalog.FillScratch(selectedOffer, template.MountKeys)
                : null;

            int first = pylonPage * perPage;
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

        /// <summary>Card footer under the rows: fit totals, then the unfitted stations when the card
        /// has room. It fills the space the page leaves so the card reaches the pager.</summary>
        private static void RefreshHardpointFooter(LoadoutTemplateRecord template, int shown)
        {
            float rowsHeight = shown * pylonRowHeight;
            float bandHeight = pylonAreaFull - rowsHeight;
            bool on = template != null && shown > 0 && bandHeight >= FooterMinHeight;
            bool twoLines = on && bandHeight >= FooterNoteHeight;

            if (hardpointFooterRule != null) hardpointFooterRule.gameObject.SetActive(on);
            if (hardpointTotalLabel != null) hardpointTotalLabel.gameObject.SetActive(on);
            if (hardpointMassLabel != null) hardpointMassLabel.gameObject.SetActive(on);
            if (hardpointNoteLabel != null) hardpointNoteLabel.gameObject.SetActive(twoLines);
            if (!on) return;

            float blockHeight = twoLines ? 30f : 14f;
            float line1Y = -rowsHeight - Mathf.Max(0f, (bandHeight - blockHeight) * 0.5f);
            // Sit on the last row's own hairline so the table body ends in one 1px line.
            Place(hardpointFooterRule.rectTransform,
                  new Rect(0f, -rowsHeight + 1f, ContentWidth, 1f));
            Place(hardpointTotalLabel.rectTransform,
                  new Rect(10f, line1Y, PylonStoreX + PylonStoreWidth - 10f, 14f));
            Place(hardpointMassLabel.rectTransform,
                  new Rect(PylonMassX, line1Y, PylonMassWidth, 14f));

            FitTotals fit = MeasureFit(template);
            hardpointTotalLabel.text = "TOTAL  ·  " + fit.Fitted + " / " + fit.Stations + " STATIONS FITTED";
            hardpointTotalLabel.color = fit.Fitted == 0 ? Dim() : Friendly();
            hardpointMassLabel.text = Grouped(fit.Mass) + " kg";
            hardpointMassLabel.color = Dim();

            if (!twoLines) return;
            Place(hardpointNoteLabel.rectTransform,
                  new Rect(10f, line1Y - 16f, ContentWidth - 20f, 14f));
            hardpointNoteLabel.text = EmptyStations(template);
            hardpointNoteLabel.color = Dim();
        }

        /// <summary>Visible stations still unarmed, for the card footer; names the complete state
        /// when there is nothing left to arm.</summary>
        private static string EmptyStations(LoadoutTemplateRecord template)
        {
            if (selectedOffer == null || template == null) return "";

            string names = "";
            int unarmed = 0;
            int listed = 0;
            for (int i = 0; i < visiblePylons.Count; i++)
            {
                int pylon = visiblePylons[i];
                if (!string.IsNullOrEmpty(template.KeyAt(pylon))) continue;
                unarmed++;
                if (listed >= 3) continue;

                string name = EconomyFacade.LoadoutCatalog.PylonName(selectedOffer, pylon);
                names = names.Length == 0 ? name : names + "  ·  " + name;
                listed++;
            }

            if (unarmed == 0) return "ALL VISIBLE STATIONS FITTED";
            if (unarmed > listed) names += "  +" + (unarmed - listed);
            return "UNARMED  ·  " + names;
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

        /// <summary>Status line under the build summary. Always names a saved preset as distinct from
        /// the flying aircraft.</summary>
        private static void RefreshLoadoutStatus(LoadoutTemplateRecord template)
        {
            if (loadoutStatusLabel == null) return;

            if (selectedOffer == null)
            {
                loadoutStatusLabel.text = "No compatible airframe is available. Join a faction and check stock.";
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

            if (templateDeletion.IsArmedFor(template))
            {
                loadoutStatusLabel.text = "CONFIRM: press DELETE again to remove " +
                    AvTheme.Truncate(template.Name, 18) + " - flying aircraft keep their fit.";
                loadoutStatusLabel.color = Warning();
                return;
            }

            if (!string.IsNullOrEmpty(templateDisabledReason))
            {
                loadoutStatusLabel.text = templateDisabledReason;
                loadoutStatusLabel.color = Warning();
                return;
            }

            if (template == null)
            {
                loadoutStatusLabel.text = "Select NEW to start a saved preset for " +
                    AvTheme.Truncate(selectedOffer.unitName, 16) + ". Apply it on SUPPLY · FIT.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            // Saved presets only; they are applied to a purchase on SUPPLY.
            loadoutStatusLabel.text =
                "Saved preset, not the flying aircraft. Apply on SUPPLY · FIT.";
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
                "Select twice within three seconds to delete this template. Aircraft already flying it keep their fit.";

            public const string Name =
                "Name the template. Flight controls are held off while you type here.";

            public const string Pylon =
                "Choose what hangs on this pylon. A symmetric pair is set together.";

            public const string Blocked =
                "Another store already fitted rules this pylon out. Clear that one to use it.";

            public const string BlockedFitted =
                "Another store rules this fitted pylon out. Click to clear this pylon.";
        }

        /// <summary>Station pips for the build summary: cells are created once and recoloured,
        /// because a periodic refresh cannot afford AvKit.PipMeter's per-call objects.</summary>
        private sealed class StationPips
        {
            private const float PipSize = 8f;
            private const float PipGap = 3f;
            private const int MaxPips = 12;

            private readonly List<Image> pips = new List<Image>();

            public StationPips(RectTransform parent, float x, float y)
            {
                for (int i = 0; i < MaxPips; i++)
                {
                    Image pip = AvKit.Panel(parent,
                                            new Rect(x + i * (PipSize + PipGap), y, PipSize, PipSize),
                                            Color.clear);
                    Outline(pip.rectTransform, new Rect(0f, 0f, PipSize, PipSize),
                            WingUi.BorderSubtle);
                    pip.gameObject.SetActive(false);
                    pips.Add(pip);
                }
            }

            public void Set(int fitted, int total)
            {
                int shown = Mathf.Clamp(total, 0, MaxPips);
                for (int i = 0; i < pips.Count; i++)
                {
                    bool active = i < shown;
                    if (pips[i].gameObject.activeSelf != active) pips[i].gameObject.SetActive(active);
                    if (active) pips[i].color = i < fitted ? Green() : Color.clear;
                }
            }
        }

        private sealed class AirframeTile
        {
            private readonly GameObject go;
            private readonly Image fill;
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

                Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor());
                rail = Rule(rt, new Rect(0f, 0f, 2f, rect.height), Color.clear);

                icon = AddSprite(rt, "AirframeIcon", IconFactory.Get("airframe"),
                                 new Rect(6f, -(rect.height - 22f) * 0.5f, 22f, 22f), Color.white);

                float textLeft = 32f;
                float textWidth = rect.width - textLeft - Space1;
                code = Label(rt, "", new Rect(textLeft, -4f, textWidth, 14f), Friendly(),
                             FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                code.overflowMode = TextOverflowModes.Ellipsis;
                code.enableWordWrapping = false;

                name = Label(rt, "", new Rect(textLeft, -18f, textWidth, 12f), Dim(),
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
                icon.enabled = sprite != null;
                icon.color = selected ? Color.white : Dim();

                string codeStr = !string.IsNullOrEmpty(def.code) ? def.code : def.unitName;
                code.text = codeStr;
                code.color = selected ? WingUi.TextPrimary : Friendly();

                string nameStr = def.unitName;
                if (!string.IsNullOrEmpty(def.code) && nameStr.StartsWith(def.code, StringComparison.OrdinalIgnoreCase))
                {
                    nameStr = nameStr.Substring(def.code.Length).TrimStart(' ', '-', '_');
                }
                name.text = nameStr;
                name.color = selected ? Friendly() : Dim();

                fill.color = selected ? WingUi.CardFillSelected : WingUi.CardFill;
                rail.color = selected ? Green() : Color.clear;

                hit.WithTooltip(def.unitName + " — Click to edit hardpoint loadout");
                hit.SetRowHighlight(fill, selected ? WingUi.CardFillSelected : WingUi.CardFill,
                    selected ? WingUi.CardFillSelectedHover : WingUi.CardFillHover);
            }
        }

        /// <summary>Clickable pylon row with native name, fitted store, mass, linked/locked state and
        /// one verb. Keep blocked stations visible with a reason; permit clearing existing conflicting
        /// stores.</summary>
        private sealed class PylonRow
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text store;
            private readonly TMP_Text mass;
            private readonly TMP_Text action;
            private readonly WingButton hit;

            public PylonRow(RectTransform parent, int index, float rowHeight)
            {
                float width = parent.rect.width;
                float y = -index * rowHeight;

                go = new GameObject("Pylon" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, rowHeight));

                fill = go.GetComponent<Image>();
                fill.color = WingUi.CardFill;
                fill.raycastTarget = false;
                rail = Rule(rt, new Rect(0f, 0f, 3f, rowHeight), WingUi.RailInert);
                Rule(rt, new Rect(0f, -rowHeight + 1f, width, 1f), WingUi.BorderSubtle);

                float textY = -(rowHeight - 16f) * 0.5f;
                name = Label(rt, "", new Rect(10f, textY, PylonNameWidth - 4f, 16f), Dim(), FontMicro,
                             FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                store = Label(rt, "", new Rect(PylonStoreX, textY, PylonStoreWidth, 16f),
                              Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                mass = Label(rt, "", new Rect(PylonMassX, textY, PylonMassWidth, 16f),
                             Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
                action = Label(rt, "", new Rect(PylonActionX, textY, PylonActionWidth, 16f),
                               Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
                name.overflowMode = TextOverflowModes.Ellipsis;
                store.overflowMode = TextOverflowModes.Ellipsis;
                mass.overflowMode = TextOverflowModes.Ellipsis;
                action.overflowMode = TextOverflowModes.Ellipsis;

                hit = HitButton(rt, new Rect(0f, 0f, width, rowHeight), null);
                go.SetActive(false);
            }

            public void Bind(int pylon, int rowIndex, string pylonName,
                             WingLoadoutCatalog.StoreOption fitted, int mirrors, bool blocked)
            {
                if (!go.activeSelf) go.SetActive(true);

                // Label mirrored pairs explicitly to explain the reduced row count.
                name.text = mirrors > 1 ? pylonName + "  ×" + mirrors : pylonName;
                name.color = WingUi.TextPrimary;
                name.enableWordWrapping = false;
                if (rail != null) rail.color = blocked ? Warning() : fitted.IsEmpty ? WingUi.RailInert : Green();

                if (blocked)
                {
                    if (!fitted.IsEmpty)
                    {
                        store.text = fitted.Label;
                        store.color = Warning();
                        mass.text = fitted.Mass > 0f ? Mathf.RoundToInt(fitted.Mass) + " kg" : "";
                        mass.color = Warning();
                        action.text = "CLEAR";
                        action.color = Warning();
                        hit.SetEnabled(true);
                        hit.SetAction(() => SetStore(pylon, null));
                        hit.WithTooltip(pylonName + " is blocked by another store — click to clear it. " +
                                        LoadoutHint.BlockedFitted);
                        hit.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
                    }
                    else
                    {
                        // Locked with nothing to clear: state only, no dead verb.
                        store.text = "LOCKED";
                        store.color = Warning();
                        mass.text = "";
                        action.text = "";
                        hit.SetEnabled(false);
                        hit.SetAction(null);
                        hit.WithTooltip(LoadoutHint.Blocked);
                        hit.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFill);
                    }
                    return;
                }

                bool empty = fitted.IsEmpty;
                store.text = empty ? "UNARMED" : fitted.Label;
                store.color = empty ? Dim() : Friendly();
                mass.text = !empty && fitted.Mass > 0f ? Mathf.RoundToInt(fitted.Mass) + " kg" : "—";
                mass.color = Dim();
                action.text = empty ? "ARM" : "EDIT";
                action.color = empty ? Green() : Dim();

                hit.SetEnabled(true);
                hit.SetAction(() => OpenStorePicker(pylon, rowIndex));
                hit.WithTooltip(pylonName + " / " + (empty ? "Empty" : fitted.Label) + ". " +
                                LoadoutHint.Pylon);
                hit.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }

    }
}
