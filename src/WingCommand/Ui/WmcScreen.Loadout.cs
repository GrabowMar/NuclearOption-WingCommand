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
    /// <summary>The WMC panel's LOADOUT tab: the per-pylon template editor.</summary>
    internal static partial class WmcScreen
    {
        // --- Loadout page ---
        //
        // The page is a template editor now, not a picker. Nothing on it reports the flight:
        // what a wingman in the air is carrying is fixed and is reported on WING, and a
        // control the player cannot act on took a third of a page that now has pylons to
        // draw.
        private static TMP_Text loadoutStatusLabel;
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

        private const int AirframeGridRows = 5;
        private const int AirframeGridCols = 4;
        private const int AirframeGridCapacity = AirframeGridRows * AirframeGridCols; // 20
        private const float AirframeTileHeight = 36f;
        private const float AirframeTileGap = 4f;

        private static int airframePage;
        private static WingButton airframePrevButton;
        private static WingButton airframeNextButton;
        private static TMP_Text airframePageLabel;
        private static readonly List<AirframeTile> airframeTiles = new List<AirframeTile>();

        /// <summary>The list the popup is currently showing, rebuilt on each open.</summary>
        private static readonly List<AvKit.PopupEntry> popupEntries =
            new List<AvKit.PopupEntry>();

        private static readonly List<WingLoadoutCatalog.StoreOption> storeScratch =
            new List<WingLoadoutCatalog.StoreOption>();

        private static AvKit.Popup loadoutPopup;
        private static AvKit.Popup shopTemplatePopup;

        /// <summary>
        /// Which template the editor is working on, by id.
        ///
        /// An id rather than the record, because the record can be deleted from underneath
        /// this — by the delete button, or by a config edit between missions — and a stale
        /// object reference would keep an editor open on a template that no longer exists.
        /// </summary>
        private static string editingTemplateId;

        /// <summary>Which page of the airframe's pylons the editor is showing.</summary>
        private static int pylonPage;

        /// <summary>
        /// Pylons drawn at once. Seven: allows 5 full rows of airframe icons at the top while
        /// keeping hardpoints visible without vertical panel overflow.
        /// </summary>
        private const int PylonRowsPerPage = 7;

        // ----------------------------------------------------------------- loadout page

        /// <summary>
        /// Where loadout templates are built: a store on every pylon, saved under a name.
        ///
        /// The page used to offer bulk-generated role and factory fits. Those paths proved
        /// unreliable, so templates now start empty and every store is chosen explicitly.
        /// The old flight list is also gone because it reported something the player could
        /// not act on from here and the WING tab already says it.
        ///
        /// What is left is a workshop, and nothing on it is per-mission. A template is a
        /// standing preference kept in the config file, and this page never touches the
        /// aircraft in the air or the funds in the bank. Choosing which template the next
        /// requisition of a type flies with is a purchase decision and belongs, with the
        /// price and the stock count, on SUPPLY.
        /// </summary>
        private static float AddLoadoutPage(RectTransform parent, float y)
        {
            loadoutPopup = new AvKit.Popup(parent, PanelWidth);

            y = Heading(parent, y, "AIRFRAME");

            const float arrowW = 20f;
            float pagerX = PanelWidth - Pad - arrowW * 2f - 36f;
            airframePrevButton = WingUi.Button(parent, "<", new Rect(pagerX, y + Space5, arrowW, RowHeight - 4f),
                                               FontMicro, UiButtonStyle.Quiet, () => TurnAirframePage(-1));
            airframePageLabel = Label(parent, "", new Rect(pagerX + arrowW, y + Space5, 36f, RowHeight - 4f),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            airframeNextButton = WingUi.Button(parent, ">", new Rect(pagerX + arrowW + 36f, y + Space5, arrowW, RowHeight - 4f),
                                               FontMicro, UiButtonStyle.Quiet, () => TurnAirframePage(1));
            airframePrevButton.gameObject.SetActive(false);
            airframePageLabel.gameObject.SetActive(false);
            airframeNextButton.gameObject.SetActive(false);

            y = AddAirframeGrid(parent, y);
            y -= Gap;

            y = Heading(parent, y, "TEMPLATE");

            float left = Pad + GutterWidth;
            float inner = PanelWidth - Pad - left;

            // The selector, then the three things that can be done to the list it selects
            // from: new, copy, delete. "C" and "X" were guesses at what a single glyph
            // meant; a three-letter word is not.
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
            templateNewButton = WingUi.Button(parent, "+", new Rect(actionX, y, actionWidth, RowHeight),
                                              FontBody, UiButtonStyle.Default, NewTemplate)
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

            // Renaming is only offered where the keyboard can actually be held off the
            // aircraft. On a build where that fails the field is replaced by a readout, and
            // templates keep the numbered names they are created with — a name is worth
            // having, but not at the price of typing one into the flight controls.
            Gutter(parent, y, "NAME");
            float nameWidth = inner;

            if (WingKeyboardGuard.Available)
            {
                templateNameField = WingUi.InputField(
                    parent, new Rect(left, y, nameWidth, RowHeight),
                    WingLoadoutTemplates.MaxNameLength, RenameTemplate, LoadoutHint.Name);
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

            templateSummaryLabel = Label(parent, "",
                                         new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                         Dim(), FontMicro, FontStyles.Normal,
                                         TextAlignmentOptions.Left);
            y -= LineHeight + Space1;

            loadoutStatusLabel = Label(parent, "",
                                       new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.Left);
            return y - (LineHeight + Space1);
        }

        /// <summary>Where the pylon list starts, so a popup can be dropped onto a row.</summary>
        private static float pylonAreaY;

        private static readonly Column[] PylonColumns =
        {
            new Column("PYLON", Space2, 140f),
            // Left-aligned, not right: a long store name ellipsised from the right keeps its
            // start ("12.7mm Machine Gun…") instead of losing it ("…Machine Gun (100)").
            new Column("STORE", 152f, PanelWidth - Pad * 2f - 152f - Space2),
        };

        /// <summary>A fixed-height area that roster rows are laid out inside.</summary>
        private static RectTransform RosterViewport(RectTransform parent, string name, float y, int rowCount = RosterRowsPerPage)
        {
            var area = new GameObject(name, typeof(RectTransform));
            RectTransform rt = area.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, new Rect(Pad, y, PanelWidth - Pad * 2f, RowPitch * rowCount));
            return rt;
        }

        /// <summary>
        /// Every airframe in the catalogue gets a starting template, not just the one on
        /// screen — a player paging through the grid should find every plane already
        /// carrying the fit the game itself would have suggested, not just the ones they
        /// happened to open the editor on first.
        ///
        /// Cheap to call on every refresh: <see cref="WingLoadoutTemplates.EnsureDefault"/>
        /// bails immediately once an airframe has a template of its own.
        /// </summary>
        private static void SeedDefaultTemplates(IReadOnlyList<WingShop.Offer> offers)
        {
            for (int i = 0; i < offers.Count; i++)
                WingLoadoutTemplates.EnsureDefault(offers[i].Definition);
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
            IReadOnlyList<WingShop.Offer> offers = WingShop.LoadoutCatalogue();
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)AirframeGridCapacity));
            airframePage = Mathf.Clamp(airframePage + direction, 0, pages - 1);
            int first = airframePage * AirframeGridCapacity;
            if (first < offers.Count)
            {
                SelectAirframe(offers[first].Definition);
            }
            else
            {
                RefreshLoadoutPage();
            }
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
            IReadOnlyList<WingShop.Offer> offers = WingShop.LoadoutCatalogue();

            if (selectedOffer == null && offers.Count > 0)
                selectedOffer = offers[0].Definition;

            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)AirframeGridCapacity));

            // Ensure the active airframe's page is showing
            if (selectedOffer != null)
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    if (offers[i].Definition == selectedOffer)
                    {
                        airframePage = i / AirframeGridCapacity;
                        break;
                    }
                }
            }
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

            if (airframePageLabel != null)
            {
                bool multiPage = pages > 1;
                airframePrevButton?.gameObject.SetActive(multiPage);
                airframeNextButton?.gameObject.SetActive(multiPage);
                airframePageLabel.gameObject.SetActive(multiPage);

                if (multiPage)
                {
                    airframePageLabel.text = $"{airframePage + 1}/{pages}";
                    airframePrevButton?.SetEnabled(airframePage > 0);
                    airframeNextButton?.SetEnabled(airframePage < pages - 1);
                }
            }
        }

        // -------------------------------------------------------------- template editing

        /// <summary>
        /// The template being edited, re-resolved every time it is asked for.
        ///
        /// Deliberately not cached. The record can vanish between one refresh and the next —
        /// deleted here, or dropped by a config edit — and every caller on this page has to
        /// cope with null anyway, so there is no reading of it that a stale reference makes
        /// safer.
        /// </summary>
        private static LoadoutTemplateRecord EditingTemplate()
        {
            if (selectedOffer == null) return null;

            LoadoutTemplateRecord record = WingLoadoutTemplates.ById(editingTemplateId);
            if (record != null && record.AirframeKey == selectedOffer.jsonKey) return record;

            // Fall to the airframe's first template rather than leaving the editor blank
            // beside a list that has something in it.
            IReadOnlyList<LoadoutTemplateRecord> mine = WingLoadoutTemplates.For(selectedOffer);
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

            IReadOnlyList<LoadoutTemplateRecord> mine = WingLoadoutTemplates.For(selectedOffer);
            if (mine.Count == 0)
            {
                WingCommandManager.Instance?.Toast(
                    "No templates for " + selectedOffer.unitName + " yet - press + to make one");
                return;
            }

            // Copied out of the scratch list the store hands back, because the popup's pick
            // callback runs long after this method returns and that list is reused.
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

            if (WingLoadoutCatalog.PylonCount(selectedOffer) == 0)
            {
                WingCommandManager.Instance?.Toast(
                    selectedOffer.unitName + "'s hardpoints cannot be read on this build");
                return;
            }

            LoadoutTemplateRecord created = WingLoadoutTemplates.Create(
                selectedOffer, WingLoadoutTemplates.NextDefaultName(selectedOffer), null);

            if (created == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + WingLoadoutTemplates.MaxPerAirframe +
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

            LoadoutTemplateRecord copy = WingLoadoutTemplates.Duplicate(source);
            if (copy == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + WingLoadoutTemplates.MaxPerAirframe +
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
            WingLoadoutTemplates.Delete(doomed);
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

            WingLoadoutTemplates.Rename(template, name);

            // The store trims and defaults the name, so the field is put back in step with
            // what was actually saved rather than what was typed.
            SyncNameField();
        }

        /// <summary>
        /// Put the rename field back in step with the template it is editing.
        ///
        /// Called on every change of template rather than from the refresh loop: writing to
        /// the field five times a second would move the caret out from under anyone typing
        /// in it.
        /// </summary>
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

        // ------------------------------------------------------------------ pylon list

        /// <summary>
        /// The pylons the editor draws, which is not quite the airframe's list of them.
        ///
        /// A hardpoint set that mirrors the one before it is folded away: the two cannot be
        /// armed differently, so showing both would double the length of the list without
        /// adding a decision to it. The hidden one is written whenever its partner is.
        /// </summary>
        private static readonly List<int> visiblePylons = new List<int>();

        private static void RebuildVisiblePylons()
        {
            visiblePylons.Clear();
            if (selectedOffer == null) return;

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                if (WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i)) continue;
                visiblePylons.Add(i);
            }
        }

        private static void TurnPylonPage(int direction) =>
            pylonPage = Mathf.Max(0, pylonPage + direction);

        /// <summary>
        /// Put a store on a pylon, and on its mirror.
        ///
        /// Writing the mirror here rather than at the point of building means a template's
        /// saved keys always describe every station the aircraft actually has, so anything
        /// reading it back — the summary line, another install — sees the real fit rather
        /// than one wing's worth of it.
        /// </summary>
        private static void SetStore(int pylon, string key)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null) return;

            WingLoadoutTemplates.SetMount(template, pylon, key);

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < count && WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i);
                 i++)
                WingLoadoutTemplates.SetMount(template, i, key);
        }

        private static void OpenStorePicker(int pylon, int rowIndex)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null)
            {
                WingCommandManager.Instance?.Toast("Make a template first");
                return;
            }

            WingLoadoutCatalog.OptionsFor(selectedOffer, pylon, storeScratch);
            if (storeScratch.Count <= 1)
            {
                WingCommandManager.Instance?.Toast(
                    WingLoadoutCatalog.PylonName(selectedOffer, pylon) + " takes no stores");
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

            // Dropped onto the row it belongs to, so the list appears where the player is
            // already looking rather than at a fixed spot on the page.
            float rowY = pylonAreaY - RowPitch * rowIndex - RowHeight;
            loadoutPopup?.Show(new Rect(Pad, rowY, PanelWidth - Pad * 2f, 0f), popupEntries,
                               index =>
            {
                if (index < 0 || index >= keys.Count) return;
                SetStore(pylon, keys[index]);
            });
        }

        /// <summary>The right-hand column of a store row: what it is and what it weighs.</summary>
        private static string StoreDetail(WingLoadoutCatalog.StoreOption option)
        {
            if (option.IsEmpty) return "";

            string tag = option.RoleTag;
            string ammo = option.Ammo > 1 ? "x" + option.Ammo : "";

            if (tag.Length == 0 && ammo.Length == 0) return "";
            if (tag.Length == 0) return ammo;
            return ammo.Length == 0 ? tag : tag + "  " + ammo;
        }

        // -------------------------------------------------------------------- refresh

        private static void RefreshLoadoutPage()
        {
            IReadOnlyList<WingShop.Offer> offers = WingShop.LoadoutCatalogue();
            SeedDefaultTemplates(offers);
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
            var liveries = WingLoadoutTemplates.GetLiveries(selectedOffer, faction);
            int currentIdx = WingLoadoutTemplates.GetLiveryIndex(selectedOffer);
            if (currentIdx >= liveries.Count) currentIdx = 0;
            liveryLabel.text = liveries[currentIdx].Name.ToUpperInvariant();
        }

        private static void CycleLivery(int direction)
        {
            if (selectedOffer == null) return;
            FactionHQ hq = WingCommandManager.Instance?.Wing?.Leader?.NetworkHQ;
            Faction faction = hq != null ? hq.faction : null;
            var liveries = WingLoadoutTemplates.GetLiveries(selectedOffer, faction);
            if (liveries.Count <= 1) return;
            int current = WingLoadoutTemplates.GetLiveryIndex(selectedOffer);
            int next = (current + direction + liveries.Count) % liveries.Count;
            WingLoadoutTemplates.SetLiveryIndex(selectedOffer, next);
            RefreshLiveryControl();
        }

        private static void RefreshTemplateControls(LoadoutTemplateRecord template)
        {
            bool haveAirframe = selectedOffer != null;
            bool readable = haveAirframe && WingLoadoutCatalog.PylonCount(selectedOffer) > 0;
            int saved = haveAirframe ? WingLoadoutTemplates.CountFor(selectedOffer) : 0;

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
                                          saved < WingLoadoutTemplates.MaxPerAirframe);
            templateCopyButton?.SetEnabled(template != null &&
                                           saved < WingLoadoutTemplates.MaxPerAirframe);
            templateDeleteButton?.SetEnabled(template != null);
            // The name field is written only when the template underneath it changes, so
            // typing is never interrupted by the refresh loop.
            if (!ReferenceEquals(lastNamedTemplate, template))
            {
                lastNamedTemplate = template;
                SyncNameField();
            }

            RefreshTemplateSummary(template);
        }

        /// <summary>The template the name field was last written for. See SyncNameField.</summary>
        private static LoadoutTemplateRecord lastNamedTemplate;

        /// <summary>
        /// What the template adds up to: how many stations are loaded, what it weighs, and
        /// what it is for.
        ///
        /// The weight is the part worth having. Every other readout on this page is about
        /// one pylon, and the one thing a per-pylon editor makes easy to get wrong is
        /// hanging so much off an airframe that it cannot carry it.
        /// </summary>
        private static void RefreshTemplateSummary(LoadoutTemplateRecord template)
        {
            if (templateSummaryLabel == null) return;

            if (template == null)
            {
                templateSummaryLabel.text = "";
                return;
            }

            int fitted = 0;
            float mass = 0f;
            float air = 0f;
            float surface = 0f;

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                string key = template.KeyAt(i);
                if (string.IsNullOrEmpty(key)) continue;

                WingLoadoutCatalog.StoreOption store =
                    WingLoadoutCatalog.StoreOn(selectedOffer, i, key);
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

        private static void RefreshPylonRows(LoadoutTemplateRecord template)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(visiblePylons.Count /
                                                     (float)PylonRowsPerPage));
            pylonPage = Mathf.Clamp(pylonPage, 0, pages - 1);

            if (pylonPageLabel != null)
            {
                pylonPageLabel.text = visiblePylons.Count == 0
                    ? "no readable hardpoints"
                    : "pylon page " + (pylonPage + 1) + " of " + pages;
            }

            bool hasPylons = visiblePylons.Count > 0;
            if (pylonEmptyCard != null) pylonEmptyCard.gameObject.SetActive(!hasPylons);
            if (pylonEmptyLabel != null) pylonEmptyLabel.gameObject.SetActive(!hasPylons);

            pylonPrevButton?.SetEnabled(pylonPage > 0);
            pylonNextButton?.SetEnabled(pylonPage < pages - 1);

            // Built once per refresh so every row asks the game the same question about the
            // same in-progress fit, and into a scratch loadout rather than a fresh one:
            // this runs five times a second, and the delivery path's BuildFromKeys has to
            // keep allocating because a Loadout handed to the spawner is kept by the
            // aircraft and must never be shared.
            Loadout inProgress = template != null
                ? WingLoadoutCatalog.FillScratch(selectedOffer, template.MountKeys)
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
                    blocked = WingLoadoutCatalog.IsPylonBlocked(
                        selectedOffer, pylon + mirror, inProgress);

                pylonRows[i].Bind(
                    pylon, i,
                    WingLoadoutCatalog.PylonName(selectedOffer, pylon),
                    WingLoadoutCatalog.StoreOn(selectedOffer, pylon, template.KeyAt(pylon)),
                    represented,
                    blocked);
            }
        }

        /// <summary>How many stations one visible row actually stands for.</summary>
        private static int MirrorCount(int pylon)
        {
            int count = 1;
            int total = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < total && WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i);
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

            if (!WingLoadoutCatalog.Available)
            {
                loadoutStatusLabel.text =
                    "Stock station data unreadable on this build - standard fit only.";
                loadoutStatusLabel.color = Warning();
                return;
            }

            if (WingLoadoutCatalog.PylonCount(selectedOffer) == 0)
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

            // Says where the template is actually used, because nothing on this page applies
            // it: a player who builds one and never opens SUPPLY has changed nothing.
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

        /// <summary>What each control on the Loadout tab says about itself on hover.</summary>
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

                Sprite sprite = def.mapIcon != null ? def.mapIcon
                              : def.friendlyIcon != null ? def.friendlyIcon
                              : IconFactory.Get("airframe");
                icon.sprite = sprite;
                icon.color = selected ? Color.white : Dim();

                string codeStr = !string.IsNullOrEmpty(def.code) ? def.code : def.unitName;
                code.text = AvTheme.Truncate(codeStr, 7);
                code.color = selected ? Green() : Friendly();

                name.text = AvTheme.Truncate(def.unitName, 10);
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

        /// <summary>
        /// One pylon: what it is called, what is on it, and a click to change that.
        ///
        /// The whole row opens the store list, the way every other list on this panel is
        /// selected by its row rather than by a button inside it. A blocked pylon still
        /// draws its name — knowing the station exists and why it cannot be used is the
        /// point — but goes inert and says so on hover.
        /// </summary>
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

                // A mirrored pair says so, so the player is not left wondering why the list
                // is shorter than the aircraft looks.
                name.text = mirrors > 1
                    ? AvTheme.Truncate(pylonName, 20) + "  x" + mirrors
                    : AvTheme.Truncate(pylonName, 24);

                if (blocked)
                {
                    store.text = "— BLOCKED —";
                    store.color = Warning();
                    name.color = Dim();

                    // A newly selected store elsewhere can block a station that was already
                    // fitted. Keep that row actionable so the conflicting store can be
                    // cleared instead of trapping the template in an invalid state.
                    bool canClear = !fitted.IsEmpty;
                    hit.SetAction(canClear ? () => SetStore(pylon, null) : (Action)null);
                    hit.SetEnabled(canClear);
                    hit.WithTooltip(canClear ? LoadoutHint.BlockedFitted : LoadoutHint.Blocked);
                    hit.SetRowHighlight(fill, WingUi.CardFill,
                                        canClear ? WingUi.CardFillHover : WingUi.CardFill);
                    return;
                }

                bool empty = fitted.IsEmpty;
                store.text = empty ? "— EMPTY —" : AvTheme.Truncate(fitted.Label, 24);
                store.color = empty ? Dim() : Friendly();
                name.color = Friendly();

                hit.SetEnabled(true);
                hit.WithTooltip(LoadoutHint.Pylon);
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
