using System;
using System.Collections.Generic;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Pilot Studio: a two-column editor with the catalog on the left, the record editor on
    /// the right and the save actions pinned above the status strip.</summary>
    internal static partial class WmcScreen
    {
        // Custom Pilot Studio UI and lifecycle.

        private static RectTransform customStudioRoot;
        private static RectTransform studioBody;
        private static ScrollRect studioScroll;
        private static bool studioTall;
        private static TMP_Text studioTableEmptyLabel;
        private static TMP_Text studioCatalogCountLabel;
        private static TMP_Text studioPagerLabel;
        private static WingButton studioPagerPrev;
        private static WingButton studioPagerNext;

        private static readonly List<StudioPilotRow> studioRows = new List<StudioPilotRow>();
        private static readonly List<CustomPilotRecord> studioPilotsList = new List<CustomPilotRecord>();
        private static int studioCatalogPage;
        private const float StudioPortraitWidth = 56f;
        private const float StudioPortraitHeight = 76f;
        private const float StudioTallBodyHeight = 560f;
        private const float StudioWideBodyHeight = 640f;
        private const float StudioColumnWidth = 186f;
        private const float StudioBioHeightMin = 72f;

        // The tall studio absorbs the body's leftover height instead of stranding glass under
        // the two columns: the catalog lists more rows and the bio field stretches to the footer.
        private static int StudioRowsPerPage => BodyHeight >= StudioTallBodyHeight ? 6 : 4;
        private static float StudioRowHeight => BodyHeight >= StudioWideBodyHeight ? 48f : 44f;
        private static float StudioRowPitch => StudioRowHeight + 2f;
        private static float StudioPreviewHeight => BodyHeight >= StudioWideBodyHeight ? 148f : 104f;
        private static float StudioPreviewPortraitHeight =>
            BodyHeight >= StudioWideBodyHeight ? 96f : StudioPortraitHeight;

        // Editor draft pilot.
        private static CustomPilotRecord draftPilot = new CustomPilotRecord();
        private static int selectedCatalogIndex = -1;

        // Studio editor controls.
        private static Image studioPortraitImage;
        private static Image[] studioPortraitFrame;

        private static TMP_Text bodyValueLabel;
        private static TMP_Text faceValueLabel;
        private static TMP_Text hairValueLabel;
        private static TMP_Text uniformValueLabel;
        private static TMP_Text backdropValueLabel;

        private static TMP_InputField studioCallsignField;
        private static TMP_InputField studioNameField;
        private static TMP_Text personaValueLabel;
        private static TMP_Text rankValueLabel;
        private static TMP_Text studioStatusLabel;

        private static TMP_InputField studioBioField;

        private static WingButton studioRecruitSelectedButton;
        private static WingButton studioDeleteButton;

        // Selected preview card.
        private static Image studioPreviewPortrait;
        private static Image studioPreviewFrame;
        private static Image studioPreviewRail;
        private static TMP_Text studioPreviewName;
        private static TMP_Text studioPreviewDetail;
        private static TMP_Text studioPreviewState;
        private static Image studioPreviewXpBar;
        private static float studioPreviewXpWidth;

        // Destructive actions keep the shell's three-second two-press confirmation.
        private static readonly Confirmation studioDeleteConfirm = new Confirmation();
        private static readonly Confirmation studioDischargeConfirm = new Confirmation();

        private sealed class StudioPilotRow
        {
            private readonly Image Fill;
            private readonly Image[] Outline;
            private readonly Image Portrait;
            private readonly Image Rail;
            private readonly TMP_Text CallLabel;
            private readonly TMP_Text NameLabel;
            private readonly TMP_Text StateLabel;
            private readonly WingButton Hit;

            public StudioPilotRow(RectTransform parent, float x, float y, float w)
            {
                Rect rect = new Rect(x, y, w, StudioRowHeight);
                Fill = Panel(parent, rect, WingUi.CardFill);
                Outline = WingUi.Outline(parent, rect, FrameColor());
                Rail = Rule(parent, new Rect(x, y, 3f, StudioRowHeight), MemberFrameColor());

                Panel(parent, new Rect(x + 6f, y - 4f, 28f, 36f), AvTheme.Surface);
                var maskGo = new GameObject("StudioRowPortraitMask", typeof(RectTransform), typeof(RectMask2D));
                RectTransform maskRt = maskGo.GetComponent<RectTransform>();
                maskRt.SetParent(parent, worldPositionStays: false);
                Place(maskRt, new Rect(x + 6f, y - 4f, 28f, 36f));
                Portrait = AddSprite(maskRt, "StudioRowPortrait", null,
                                     new Rect(0f, 0f, 28f, 36f), Color.white);

                CallLabel = Label(parent, "", new Rect(x + 40f, y - 3f, w - 106f, 18f), Friendly(),
                                  FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
                CallLabel.enableWordWrapping = false;
                CallLabel.overflowMode = TextOverflowModes.Ellipsis;
                NameLabel = Label(parent, "", new Rect(x + 40f, y - 22f, w - 46f, 16f), Dim(),
                                  FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                NameLabel.enableWordWrapping = false;
                NameLabel.overflowMode = TextOverflowModes.Ellipsis;
                StateLabel = Label(parent, "", new Rect(x + w - 66f, y - 3f, 62f, 18f), Dim(),
                                   FontMicro, FontStyles.Bold, TextAlignmentOptions.Right);

                Hit = HitButton(parent, rect, null);
                Hide();
            }

            public void Bind(CustomPilotRecord record, bool isSelected, Action onClick)
            {
                bool inSquadron = PersonnelFacade.Roster.ContainsCallsign(record.Callsign);
                Fill.color = isSelected ? WingUi.CardFillSelected : WingUi.CardFill;
                Rail.color = isSelected ? Green() : MemberFrameColor();
                if (Outline != null)
                {
                    Color border = isSelected ? Green() : FrameColor();
                    for (int i = 0; i < Outline.Length; i++)
                        if (Outline[i] != null) Outline[i].color = border;
                }

                CallLabel.text = AvTheme.Truncate(record.Callsign, 12);
                CallLabel.color = isSelected ? Green() : Friendly();
                NameLabel.text = AvTheme.Truncate(record.Name, 16);

                StateLabel.text = inSquadron ? "IN SQUADRON" : "AVAILABLE";
                StateLabel.color = inSquadron ? WingUi.RailEmerald : Dim();

                UpdatePortraitAspectFill(Portrait, PersonnelFacade.Portraits.ForSelection(record.Selection),
                                         28f, 36f);

                Hit.SetAction(onClick);
                Hit.SetRowHighlight(Fill, isSelected ? WingUi.CardFillSelected : WingUi.CardFill,
                    isSelected ? WingUi.CardFillSelectedHover : WingUi.CardFillHover);
                Hit.WithTooltip($"Select {record.Callsign} · {record.Name} to inspect and edit");
                SetVisible(true);
            }

            /// <summary>Inert, named placeholder for a catalog row with no saved pilot behind it.</summary>
            public void ShowVacant(int slot)
            {
                Fill.color = WingUi.CardFill;
                Rail.color = WingUi.RailInert;
                if (Outline != null)
                {
                    for (int i = 0; i < Outline.Length; i++)
                        if (Outline[i] != null) Outline[i].color = FrameColor();
                }

                CallLabel.text = "EMPTY SLOT " + (slot + 1).ToString("00");
                CallLabel.color = Dim();
                NameLabel.text = "NEW OR IMPORT";
                NameLabel.color = Dim();
                StateLabel.text = "VACANT";
                StateLabel.color = Dim();
                if (Portrait != null) Portrait.enabled = false;

                Hit.SetAction(null);
                SetVisible(true);
                if (Hit != null) Hit.gameObject.SetActive(false);
            }

            public void Hide() => SetVisible(false);

            private void SetVisible(bool visible)
            {
                if (Fill != null && Fill.gameObject.activeSelf != visible) Fill.gameObject.SetActive(visible);
                if (Portrait != null && Portrait.gameObject.activeSelf != visible) Portrait.gameObject.SetActive(visible);
                if (CallLabel != null && CallLabel.gameObject.activeSelf != visible) CallLabel.gameObject.SetActive(visible);
                if (NameLabel != null && NameLabel.gameObject.activeSelf != visible) NameLabel.gameObject.SetActive(visible);
                if (StateLabel != null && StateLabel.gameObject.activeSelf != visible) StateLabel.gameObject.SetActive(visible);
                if (Rail != null && Rail.gameObject.activeSelf != visible) Rail.gameObject.SetActive(visible);
                if (Hit != null && Hit.gameObject.activeSelf != visible) Hit.gameObject.SetActive(visible);
                if (Outline != null)
                {
                    for (int i = 0; i < Outline.Length; i++)
                        if (Outline[i] != null && Outline[i].gameObject.activeSelf != visible)
                            Outline[i].gameObject.SetActive(visible);
                }
            }
        }

        private static float BuildCustomPilotsStudio(RectTransform parent, float y)
        {
            _ = y;
            studioCatalogPage = 0;
            selectedCatalogIndex = -1;
            studioDeleteConfirm.Clear();
            studioDischargeConfirm.Clear();
            studioTall = BodyHeight >= StudioTallBodyHeight;

            // The save footer stays visible; the columns share one viewport when the body is short.
            const float footerBlock = RowHeight + Space2;
            float bodyBottom = BodyBottom + footerBlock;
            float bodyHeight = Mathf.Max(RowHeight, BodyTop - bodyBottom);

            if (studioTall)
            {
                studioScroll = null;
                studioBody = PageRoot(parent, "StudioColumns");
                Place(studioBody, new Rect(0f, BodyTop, PageWidth, bodyHeight));
            }
            else
            {
                BuildViewport(parent, new Rect(0f, BodyTop, PageWidth, bodyHeight), "StudioViewport",
                              out studioBody, out studioScroll);
            }

            float titleBottom = Heading(studioBody, -Space1, "PILOT STUDIO");
            float columnsTop = titleBottom - Space1;
            float rightX = Pad + StudioColumnWidth + Gap;
            float rightWidth = ContentWidth - StudioColumnWidth - Gap;
            float leftBottom = BuildStudioCatalog(studioBody, Pad, StudioColumnWidth, columnsTop);
            float rightBottom = BuildStudioEditor(studioBody, rightX, rightWidth, columnsTop);

            if (studioScroll != null) Reflow(studioScroll, Mathf.Min(leftBottom, rightBottom));

            BuildStudioFooter(parent);
            return BodyBottom;
        }

        private static float BuildStudioCatalog(RectTransform parent, float x, float w, float y)
        {
            WingUi.Button(parent, "< SQUADRON", new Rect(x, y, w, 28f), FontSmall, ShowSquadronView)
                .WithTooltip("Return to squadron roster view");
            y -= 28f + Space1;

            float half = (w - Gap) * 0.5f;
            WingUi.Button(parent, "IMPORT FILES", new Rect(x, y, half, 28f), FontMicro, OnStudioImportAll)
                .WithTooltip("Import pilots and chatter from the custom pilot files into the squadron");
            WingUi.Button(parent, "EXPORT ALL", new Rect(x + half + Gap, y, half, 28f), FontMicro, OnStudioExportAll)
                .WithTooltip("Export active squadron roster to exported_squadron.json");
            y -= 28f + Space1;
            WingUi.Button(parent, "OPEN FOLDER", new Rect(x, y, half, 28f), FontMicro,
                PersonnelFacade.CustomPilots.OpenFolder)
                .WithTooltip("Open Pilots folder in Windows Explorer");
            WingUi.Button(parent, "RECRUIT ALL", new Rect(x + half + Gap, y, half, 28f), FontMicro, OnStudioRecruitAll)
                .WithTooltip("Recruit all unrecruited custom pilots into squadron");
            y -= 28f + Space2;

            float headerTop = y;
            y = SectionHeader(parent, x, y, w, "SAVED PILOTS");
            studioCatalogCountLabel = Label(parent, "", new Rect(x + 10f, headerTop - 1f, w - 10f, 14f),
                                            Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            y -= Space1;

            float rowsTop = y;
            studioRows.Clear();
            for (int i = 0; i < StudioRowsPerPage; i++)
                studioRows.Add(new StudioPilotRow(parent, x, rowsTop - i * StudioRowPitch, w));

            studioTableEmptyLabel = Label(parent,
                "NO SAVED PILOTS\nCreate one with NEW or import your pilot files.",
                new Rect(x + 4f, rowsTop - 24f, w - 8f, 44f), Dim(), FontSmall, FontStyles.Normal,
                TextAlignmentOptions.Center);
            studioTableEmptyLabel.enableWordWrapping = true;
            studioTableEmptyLabel.gameObject.SetActive(false);
            y = rowsTop - StudioRowsPerPage * StudioRowPitch;

            RectTransform pagerRoot = PageRoot(parent, "StudioPager");
            Place(pagerRoot, new Rect(x, y, w, RowHeight));
            (studioPagerPrev, studioPagerLabel, studioPagerNext) =
                PagerRow(pagerRoot, 0f, w, () => TurnStudioCatalogPage(-1), () => TurnStudioCatalogPage(1),
                         "Previous or next saved pilot page");
            y -= RowHeight + Space1;

            half = (w - Gap) * 0.5f;
            WingUi.Button(parent, "NEW", new Rect(x, y, half, 28f), FontSmall, OnStudioNewPilot)
                .WithTooltip("Create a new blank pilot record in the editor");
            WingUi.Button(parent, "CLONE", new Rect(x + half + Gap, y, half, 28f), FontSmall, OnStudioClonePilot)
                .WithTooltip("Duplicate currently selected pilot with a new callsign");
            y -= 28f + Space1;
            studioDeleteButton = WingUi.Button(parent, "DELETE", new Rect(x, y, half, 28f), FontSmall,
                UiButtonStyle.Danger, OnStudioDeletePressed)
                .WithTooltip("Delete the selected custom pilot file. Press twice to confirm.");
            studioRecruitSelectedButton = WingUi.Button(parent, "RECRUIT",
                new Rect(x + half + Gap, y, half, 28f), FontSmall, OnStudioToggleRecruitSelected)
                .WithTooltip("Recruit this pilot into the active squadron roster");
            y -= 28f + Space2;

            // Selected preview card; the portrait and card grow with the body.
            float previewHeight = StudioPreviewHeight;
            float previewPortraitW = StudioPortraitWidth;
            float previewPortraitH = StudioPreviewPortraitHeight;
            var (_, rail) = WingUi.TacticalCard(parent, new Rect(x, y, w, previewHeight), WingUi.RailCyan);
            studioPreviewRail = rail;
            studioPreviewFrame = Panel(parent,
                new Rect(x + 5f, y - 5f, previewPortraitW + 2f, previewPortraitH + 2f), FrameColor());
            Panel(parent, new Rect(x + 6f, y - 6f, previewPortraitW, previewPortraitH), AvTheme.Surface);

            var previewMaskGo = new GameObject("StudioPreviewMask", typeof(RectTransform), typeof(RectMask2D));
            RectTransform previewMask = previewMaskGo.GetComponent<RectTransform>();
            previewMask.SetParent(parent, worldPositionStays: false);
            Place(previewMask, new Rect(x + 6f, y - 6f, previewPortraitW, previewPortraitH));
            studioPreviewPortrait = AddSprite(previewMask, "StudioPreviewPortrait", null,
                                              new Rect(0f, 0f, previewPortraitW, previewPortraitH), Color.white);
            UpdatePortraitAspectFill(studioPreviewPortrait, PersonnelFacade.Portraits.Sprite,
                                     previewPortraitW, previewPortraitH);

            float previewTextX = x + 6f + previewPortraitW + 6f;
            float previewTextWidth = x + w - 6f - previewTextX;
            studioPreviewName = Label(parent, "", new Rect(previewTextX, y - 8f, previewTextWidth, 16f),
                                      Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            studioPreviewName.enableWordWrapping = false;
            studioPreviewName.overflowMode = TextOverflowModes.Ellipsis;
            studioPreviewDetail = Label(parent, "", new Rect(previewTextX, y - 26f, previewTextWidth, 14f),
                                        Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            studioPreviewDetail.enableWordWrapping = false;
            studioPreviewDetail.overflowMode = TextOverflowModes.Ellipsis;
            studioPreviewState = Label(parent, "", new Rect(previewTextX, y - 44f, previewTextWidth, 14f),
                                       Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            // Rank/XP strip sits on the card foot so a taller preview does not strand glass.
            float previewBarY = y - previewHeight + 20f;
            Rule(parent, new Rect(previewTextX, previewBarY, previewTextWidth, 6f), FrameColor());
            studioPreviewXpBar = Rule(parent, new Rect(previewTextX, previewBarY, 0f, 6f), Green());
            studioPreviewXpWidth = previewTextWidth;
            for (int i = 1; i < 5; i++)
                Rule(parent, new Rect(previewTextX + previewTextWidth * (i / 5f), previewBarY, 1f, 6f),
                     WingUi.BorderSubtle);

            y -= previewHeight + Gap;
            return y;
        }

        private static float BuildStudioEditor(RectTransform parent, float x, float w, float y)
        {
            y = SectionHeader(parent, x, y, w, "IDENTITY");
            y -= Space1;

            const float labelWidth = 54f;
            const float randomWidth = 54f;
            const float fieldHeight = 30f;
            float fieldWidth = w - labelWidth - Gap - randomWidth - Space1;

            Label(parent, "CALLSIGN", new Rect(x, y, labelWidth, fieldHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            studioCallsignField = WingUi.InputField(parent, new Rect(x + labelWidth, y, fieldWidth, fieldHeight), 14,
                val => { draftPilot.Callsign = val.Trim().ToUpperInvariant(); RefreshDraftVisual(); },
                tooltip: "Pilot callsign shown on the squadron roster",
                placeholderText: "CALLSIGN");
            WingUi.Button(parent, "RANDOM", new Rect(x + w - randomWidth, y, randomWidth, fieldHeight),
                FontMicro, OnRandomizeCallsign).WithTooltip("Generate a random callsign");
            y -= fieldHeight + Space1;

            Label(parent, "NAME", new Rect(x, y, labelWidth, fieldHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            studioNameField = WingUi.InputField(parent, new Rect(x + labelWidth, y, fieldWidth, fieldHeight), 24,
                val => { draftPilot.Name = val.Trim(); RefreshDraftVisual(); },
                tooltip: "Pilot name", placeholderText: "NAME");
            WingUi.Button(parent, "RANDOM", new Rect(x + w - randomWidth, y, randomWidth, fieldHeight),
                FontMicro, OnRandomizeName).WithTooltip("Generate a random name");
            y -= fieldHeight + Space1;

            CreateStudioStepper(parent, x, y, w, "RADIO", out personaValueLabel,
                () => CyclePersona(-1), () => CyclePersona(1));
            y -= RowHeight + Space1;
            CreateStudioStepper(parent, x, y, w, "RANK", out rankValueLabel,
                () => CycleRank(-1), () => CycleRank(1));
            y -= RowHeight + Space1;

            studioStatusLabel = Label(parent, "", new Rect(x, y, w, 16f), Dim(), FontMicro,
                                      FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 16f + Space2;

            y = SectionHeader(parent, x, y, w, "APPEARANCE");
            y -= Space1;
            float blockTop = y;

            Panel(parent, new Rect(x + 2f, blockTop - 2f, StudioPortraitWidth, StudioPortraitHeight),
                  AvTheme.Surface);
            var maskGo = new GameObject("StudioPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            RectTransform maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.SetParent(parent, worldPositionStays: false);
            Place(maskRt, new Rect(x + 2f, blockTop - 2f, StudioPortraitWidth, StudioPortraitHeight));

            var portraitGo = new GameObject("StudioPortrait", typeof(RectTransform), typeof(Image));
            RectTransform portraitRt = portraitGo.GetComponent<RectTransform>();
            portraitRt.SetParent(maskRt, worldPositionStays: false);
            studioPortraitImage = portraitGo.GetComponent<Image>();
            studioPortraitImage.color = Color.white;
            studioPortraitImage.raycastTarget = false;
            UpdatePortraitAspectFill(studioPortraitImage, PersonnelFacade.Portraits.Sprite,
                                     StudioPortraitWidth, StudioPortraitHeight);
            studioPortraitFrame = Outline(parent,
                new Rect(x + 2f, blockTop - 2f, StudioPortraitWidth, StudioPortraitHeight), FrameColor());

            WingUi.Button(parent, "REROLL", new Rect(x + 2f, blockTop - StudioPortraitHeight - 8f,
                                                     StudioPortraitWidth, 26f), FontMicro, OnRerollLooks)
                .WithTooltip("Randomize body, face, hair, faction uniform, and backdrop");

            float stepperX = x + StudioPortraitWidth + 6f;
            float stepperW = w - StudioPortraitWidth - 6f;
            float stepperPitch = BodyHeight >= StudioWideBodyHeight ? 38f : 34f;
            float stepperY = blockTop;
            CreateStudioStepper(parent, stepperX, stepperY, stepperW, "BODY", out bodyValueLabel,
                () => CycleBody(-1), () => CycleBody(1));
            stepperY -= stepperPitch;
            CreateStudioStepper(parent, stepperX, stepperY, stepperW, "FACE", out faceValueLabel,
                () => CycleFace(-1), () => CycleFace(1));
            stepperY -= stepperPitch;
            CreateStudioStepper(parent, stepperX, stepperY, stepperW, "HAIR", out hairValueLabel,
                () => CycleHair(-1), () => CycleHair(1));
            stepperY -= stepperPitch;
            CreateStudioStepper(parent, stepperX, stepperY, stepperW, "SUIT", out uniformValueLabel,
                () => CycleUniform(-1), () => CycleUniform(1));
            stepperY -= stepperPitch;
            CreateStudioStepper(parent, stepperX, stepperY, stepperW, "SCENE", out backdropValueLabel,
                () => CycleBackdrop(-1), () => CycleBackdrop(1));

            y = blockTop - 5f * stepperPitch - Space2;

            y = SectionHeader(parent, x, y, w, "BACKGROUND");
            y -= Space1;
            float bioHeight = StudioBioHeight(y);
            studioBioField = WingUi.InputField(parent, new Rect(x, y, w, bioHeight), 280,
                val => { draftPilot.Background = val; },
                tooltip: "Pilot background notes or military history",
                placeholderText: "Enter pilot background notes or military history...",
                lineType: TMP_InputField.LineType.MultiLineNewline);
            y -= bioHeight + Space1;

            float half = (w - Gap) * 0.5f;
            WingUi.Button(parent, "GENERATE BIO", new Rect(x, y, half, 28f), FontSmall, OnGenerateBio)
                .WithTooltip("Generate background text matching pilot persona and callsign");
            WingUi.Button(parent, "RANDOMIZE ALL", new Rect(x + half + Gap, y, half, 28f), FontSmall, OnRandomizeAll)
                .WithTooltip("Generate a completely fresh random pilot (identity, looks, and lore)");
            y -= 28f + Gap;
            return y;
        }

        /// <summary>Bio field height: fixed on short bodies, stretched to the pinned footer on tall
        /// ones so the editor fills its column instead of stranding glass above SAVE PILOT.</summary>
        private static float StudioBioHeight(float bioTop)
        {
            if (!studioTall) return StudioBioHeightMin;
            float footerTop = BodyBottom + RowHeight + Space2;
            return Mathf.Clamp(bioTop - footerTop - (Space1 + 28f + Gap), StudioBioHeightMin, 320f);
        }

        private static void BuildStudioFooter(RectTransform parent)
        {
            float top = BodyBottom + Space2 + RowHeight;
            float half = (ContentWidth - Gap) * 0.5f;
            WingUi.Button(parent, "SAVE PILOT", new Rect(Pad, top, half, RowHeight), FontSmall,
                UiButtonStyle.Primary, OnStudioSavePilot)
                .WithTooltip("Save or update this custom pilot record in custom_pilots.json");
            WingUi.Button(parent, "SAVE & RECRUIT", new Rect(Pad + half + Gap, top, half, RowHeight),
                FontSmall, OnStudioRecruitToSquadron)
                .WithTooltip("Recruit this pilot into active squadron roster");
        }

        private static void CreateStudioStepper(RectTransform parent, float x, float y, float w,
            string labelPrefix, out TMP_Text valueLabel, Action onPrev, Action onNext)
        {
            if (!string.IsNullOrEmpty(labelPrefix))
            {
                const float labelWidth = 54f;
                Label(parent, labelPrefix, new Rect(x, y, labelWidth, RowHeight), Dim(), FontMicro,
                    FontStyles.Normal, TextAlignmentOptions.Left);
                x += labelWidth;
                w -= labelWidth;
            }
            Panel(parent, new Rect(x, y, w, RowHeight), RowColor());
            Outline(parent, new Rect(x, y, w, RowHeight), FrameColor());
            const float arrow = 28f;

            WingUi.Button(parent, "<", new Rect(x, y, arrow, RowHeight),
                FontSmall, UiButtonStyle.Quiet, onPrev).WithTooltip("Previous " + (labelPrefix ?? "value").ToLowerInvariant());
            valueLabel = Label(parent, "", new Rect(x + arrow + 4f, y, w - arrow * 2f - 8f, RowHeight),
                Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            WingUi.Button(parent, ">", new Rect(x + w - arrow, y, arrow, RowHeight),
                FontSmall, UiButtonStyle.Quiet, onNext).WithTooltip("Next " + (labelPrefix ?? "value").ToLowerInvariant());
        }

        public static void ShowPilotStudioView()
        {
            if (squadronViewRoot != null) squadronViewRoot.gameObject.SetActive(false);
            if (customStudioRoot != null) customStudioRoot.gameObject.SetActive(true);
            if (studioScroll != null) pageScrolls[(int)Page.Wing] = studioScroll;
            ResetPageScroll(Page.Wing);

            studioDeleteConfirm.Clear();
            studioDischargeConfirm.Clear();
            ReloadCustomPilotsList();
            if (studioPilotsList.Count > 0)
            {
                SelectStudioPilot(studioPilotsList[0], 0);
            }
            else
            {
                OnStudioNewPilot();
            }
        }

        public static void ShowSquadronView()
        {
            ReleaseStudioInput();
            if (customStudioRoot != null) customStudioRoot.gameObject.SetActive(false);
            if (squadronViewRoot != null) squadronViewRoot.gameObject.SetActive(true);
            if (wingScroll != null) pageScrolls[(int)Page.Wing] = wingScroll;
            ResetPageScroll(Page.Wing);

            PruneFocus(Wing());
            RefreshWingPage(WingCommandManager.Instance?.Wing);
        }

        public static void ReleaseStudioInput()
        {
            if (studioCallsignField != null && studioCallsignField.isFocused)
                studioCallsignField.DeactivateInputField();
            if (studioNameField != null && studioNameField.isFocused)
                studioNameField.DeactivateInputField();
            if (studioBioField != null && studioBioField.isFocused)
                studioBioField.DeactivateInputField();
            ReleasePanelInput();
        }

        private static void ReloadCustomPilotsList()
        {
            studioPilotsList.Clear();
            List<CustomPilotRecord> loaded = PersonnelFacade.CustomPilots.LoadAllCustomPilots(out _);
            if (loaded != null) studioPilotsList.AddRange(loaded);

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(studioPilotsList.Count / (float)StudioRowsPerPage));
            studioCatalogPage = Mathf.Clamp(studioCatalogPage, 0, pageCount - 1);
            RefreshStudioCatalog();
        }

        private static void RefreshStudioCatalog()
        {
            int total = studioPilotsList.Count;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)StudioRowsPerPage));
            studioCatalogPage = Mathf.Clamp(studioCatalogPage, 0, pageCount - 1);

            if (studioPagerLabel != null)
                studioPagerLabel.text = PageSummary(total, studioCatalogPage, pageCount, "PILOT", "PILOTS");
            studioPagerPrev?.SetEnabled(studioCatalogPage > 0);
            studioPagerNext?.SetEnabled(studioCatalogPage < pageCount - 1);

            if (studioTableEmptyLabel != null) studioTableEmptyLabel.gameObject.SetActive(total == 0);
            if (studioCatalogCountLabel != null)
                studioCatalogCountLabel.text = total == 0 ? "NO FILES" : total + (total == 1 ? " FILE" : " FILES");

            int startIndex = studioCatalogPage * StudioRowsPerPage;
            for (int i = 0; i < studioRows.Count; i++)
            {
                int itemIndex = startIndex + i;
                if (itemIndex < total)
                {
                    CustomPilotRecord record = studioPilotsList[itemIndex];
                    bool isSelected = itemIndex == selectedCatalogIndex;
                    int captureIndex = itemIndex;
                    studioRows[i].Bind(record, isSelected, () => SelectStudioPilot(record, captureIndex));
                }
                else if (total > 0)
                {
                    // Named inert rows keep the catalog looking intentional on tall bodies;
                    // the empty-file card owns the area when nothing is saved yet.
                    studioRows[i].ShowVacant(itemIndex);
                }
                else
                {
                    studioRows[i].Hide();
                }
            }

            RefreshStudioActions();
        }

        private static void TurnStudioCatalogPage(int direction)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(studioPilotsList.Count / (float)StudioRowsPerPage));
            studioCatalogPage = Mathf.Clamp(studioCatalogPage + direction, 0, pageCount - 1);
            RefreshStudioCatalog();
        }

        private static void RefreshStudioActions()
        {
            bool hasCallsign = !string.IsNullOrWhiteSpace(draftPilot.Callsign);
            bool inSquadron = hasCallsign && PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);

            if (studioDeleteButton != null)
            {
                bool armed = hasCallsign && studioDeleteConfirm.IsArmedFor(draftPilot.Callsign);
                studioDeleteButton.SetText(armed ? "DELETE?" : "DELETE");
                studioDeleteButton.SetLatched(armed);
                studioDeleteButton.SetEnabled(hasCallsign);
            }

            if (studioRecruitSelectedButton != null)
            {
                bool armed = inSquadron && studioDischargeConfirm.IsArmedFor(draftPilot.Callsign);
                studioRecruitSelectedButton.SetText(inSquadron
                    ? (armed ? "DISCHARGE?" : "DISCHARGE")
                    : "RECRUIT");
                studioRecruitSelectedButton.SetLatched(armed);
                studioRecruitSelectedButton.SetEnabled(hasCallsign);
                studioRecruitSelectedButton.WithTooltip(inSquadron
                    ? "Remove " + draftPilot.Callsign + " from the squadron. Press twice to confirm."
                    : "Recruit " + draftPilot.Callsign + " into the active squadron roster");
            }
        }

        private static void SelectStudioPilot(CustomPilotRecord record, int index = -1)
        {
            selectedCatalogIndex = index;
            draftPilot = record.Clone();
            if (!draftPilot.HasCustomPortrait) draftPilot.ApplySelection(PilotPortraitGenerator.DefaultSelection);
            studioDeleteConfirm.Clear();
            studioDischargeConfirm.Clear();

            if (studioCallsignField != null) studioCallsignField.text = draftPilot.Callsign;
            if (studioNameField != null) studioNameField.text = draftPilot.Name;
            if (studioBioField != null) studioBioField.text = draftPilot.Background;

            RefreshDraftVisual();
            RefreshStudioCatalog();
        }

        private static void RefreshDraftVisual()
        {
            PortraitSelection selection = draftPilot.Selection;

            if (bodyValueLabel != null) bodyValueLabel.text = PilotPortraitGenerator.BodyLabel(selection.Body);
            if (faceValueLabel != null) faceValueLabel.text = $"{selection.Face + 1} / {PilotPortraitGenerator.FacesPerBody}";
            if (hairValueLabel != null) hairValueLabel.text = selection.Hair == 0 ? "BALD" : selection.Hair.ToString();
            if (uniformValueLabel != null) uniformValueLabel.text = PilotPortraitGenerator.UniformLabel(selection.Uniform);
            if (backdropValueLabel != null) backdropValueLabel.text = $"{selection.Backdrop + 1} / {PilotPortraitGenerator.BackdropCount}";

            if (personaValueLabel != null) personaValueLabel.text = draftPilot.Persona.ToString().ToUpperInvariant();

            WingRank rank = PersonnelFacade.Roster.RankFor(draftPilot.Xp);
            if (rankValueLabel != null)
                rankValueLabel.text = PersonnelFacade.Roster.RankName(rank).ToUpperInvariant() + " (" + draftPilot.Xp + " XP)";

            if (studioPreviewXpBar != null)
            {
                float progress = 1f;
                if (rank < PersonnelFacade.Roster.TopRank)
                {
                    int floor = PersonnelFacade.Roster.XpForRank(rank);
                    int ceiling = PersonnelFacade.Roster.XpForRank(rank + 1);
                    progress = ceiling > floor
                        ? Mathf.Clamp01((draftPilot.Xp - floor) / (float)(ceiling - floor))
                        : 0f;
                }
                studioPreviewXpBar.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, studioPreviewXpWidth * progress), 6f);
            }

            Sprite portraitSprite = PersonnelFacade.Portraits.ForSelection(selection);
            if (studioPortraitImage != null)
                UpdatePortraitAspectFill(studioPortraitImage, portraitSprite,
                                         StudioPortraitWidth, StudioPortraitHeight);
            if (studioPreviewPortrait != null)
                UpdatePortraitAspectFill(studioPreviewPortrait, portraitSprite,
                                         StudioPortraitWidth, StudioPreviewPortraitHeight);

            if (studioPortraitFrame != null)
            {
                Color border = FrameColor();
                for (int i = 0; i < studioPortraitFrame.Length; i++)
                    if (studioPortraitFrame[i] != null) studioPortraitFrame[i].color = border;
            }

            bool inSquadron = !string.IsNullOrEmpty(draftPilot.Callsign) &&
                PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);
            if (studioStatusLabel != null)
            {
                studioStatusLabel.text = inSquadron
                    ? "IN SQUADRON · Saving updates this pilot"
                    : "NOT IN SQUADRON · Save & Recruit adds this pilot";
                studioStatusLabel.color = inSquadron ? WingUi.RailEmerald : Dim();
            }

            if (studioPreviewName != null)
                studioPreviewName.text = AvTheme.Truncate(draftPilot.Callsign + " · " + draftPilot.Name, 18);
            if (studioPreviewDetail != null)
                studioPreviewDetail.text = PersonnelFacade.Roster.RankName(rank) + " · " + draftPilot.Xp + " XP";
            if (studioPreviewState != null)
            {
                studioPreviewState.text = inSquadron ? "IN SQUADRON" : "SAVED PILOT";
                studioPreviewState.color = inSquadron ? WingUi.RailEmerald : Dim();
            }
            if (studioPreviewFrame != null)
                studioPreviewFrame.color = inSquadron ? WingUi.RailEmerald : FrameColor();
            if (studioPreviewRail != null)
                studioPreviewRail.color = inSquadron ? WingUi.RailEmerald : WingUi.RailCyan;

            RefreshStudioActions();
        }

        private static void CycleBody(int dir)
        {
            PortraitSelection current = draftPilot.Selection;
            PortraitBody body = ((int)current.Body + dir + 2) % 2 == 0 ? PortraitBody.Male : PortraitBody.Female;
            draftPilot.ApplySelection(new PortraitSelection(body, current.Face, current.Hair, current.Uniform,
                                                            current.Accessory, current.Backdrop));
            RefreshDraftVisual();
        }

        private static void CycleFace(int dir)
        {
            PortraitSelection current = draftPilot.Selection;
            int face = (current.Face + dir + PilotPortraitGenerator.FacesPerBody) % PilotPortraitGenerator.FacesPerBody;
            draftPilot.ApplySelection(new PortraitSelection(current.Body, face, current.Hair, current.Uniform,
                                                            current.Accessory, current.Backdrop));
            RefreshDraftVisual();
        }

        private static void CycleHair(int dir)
        {
            PortraitSelection current = draftPilot.Selection;
            int hair = (current.Hair + dir + PilotPortraitGenerator.HairCount) % PilotPortraitGenerator.HairCount;
            draftPilot.ApplySelection(new PortraitSelection(current.Body, current.Face, hair, current.Uniform,
                                                            current.Accessory, current.Backdrop));
            RefreshDraftVisual();
        }

        private static void CycleUniform(int dir)
        {
            PortraitSelection current = draftPilot.Selection;
            int uniform = (current.Uniform + dir + PilotPortraitGenerator.UniformCount) % PilotPortraitGenerator.UniformCount;
            draftPilot.ApplySelection(new PortraitSelection(current.Body, current.Face, current.Hair, uniform,
                                                            current.Accessory, current.Backdrop));
            RefreshDraftVisual();
        }

        private static void CycleBackdrop(int dir)
        {
            PortraitSelection current = draftPilot.Selection;
            int backdrop = (current.Backdrop + dir + PilotPortraitGenerator.BackdropCount) % PilotPortraitGenerator.BackdropCount;
            draftPilot.ApplySelection(new PortraitSelection(current.Body, current.Face, current.Hair, current.Uniform,
                                                            current.Accessory, backdrop));
            RefreshDraftVisual();
        }

        private static void CyclePersona(int dir)
        {
            int count = Enum.GetValues(typeof(ChatterPersona)).Length;
            draftPilot.Persona = (ChatterPersona)(((int)draftPilot.Persona + dir + count) % count);
            RefreshDraftVisual();
        }

        private static void CycleRank(int dir)
        {
            WingRank current = PersonnelFacade.Roster.RankFor(draftPilot.Xp);
            int next = Mathf.Clamp((int)current + dir, 0, (int)WingRank.Legend);
            draftPilot.Xp = PersonnelFacade.Roster.XpForRank((WingRank)next);
            RefreshDraftVisual();
        }

        private static void OnRerollLooks()
        {
            RandomizeDraftAppearance();
            RefreshDraftVisual();
        }

        private static void RandomizeDraftAppearance()
        {
            draftPilot.ApplySelection(new PortraitSelection(
                UnityEngine.Random.Range(0, 2) == 0 ? PortraitBody.Male : PortraitBody.Female,
                UnityEngine.Random.Range(0, PilotPortraitGenerator.FacesPerBody),
                UnityEngine.Random.Range(0, PilotPortraitGenerator.HairCount),
                UnityEngine.Random.Range(0, PilotPortraitGenerator.UniformCount),
                0,
                UnityEngine.Random.Range(0, PilotPortraitGenerator.BackdropCount)));
        }

        private static void OnRandomizeCallsign()
        {
            draftPilot.Callsign = PilotIdentity.RandomCallsign();
            if (studioCallsignField != null) studioCallsignField.text = draftPilot.Callsign;
            RefreshDraftVisual();
        }

        private static void OnRandomizeName()
        {
            draftPilot.Name = PilotIdentity.RandomName();
            if (studioNameField != null) studioNameField.text = draftPilot.Name;
            RefreshDraftVisual();
        }

        private static void OnGenerateBio()
        {
            draftPilot.Background = PilotIdentity.RandomBackground(draftPilot.Persona);
            if (studioBioField != null) studioBioField.text = draftPilot.Background;
        }

        private static void OnRandomizeAll()
        {
            draftPilot.Callsign = PilotIdentity.RandomCallsign();
            draftPilot.Name = PilotIdentity.RandomName();
            draftPilot.Persona = (ChatterPersona)UnityEngine.Random.Range(0, Enum.GetValues(typeof(ChatterPersona)).Length);
            draftPilot.Xp = 0;
            RandomizeDraftAppearance();
            draftPilot.Background = PilotIdentity.RandomBackground(draftPilot.Persona);

            if (studioCallsignField != null) studioCallsignField.text = draftPilot.Callsign;
            if (studioNameField != null) studioNameField.text = draftPilot.Name;
            if (studioBioField != null) studioBioField.text = draftPilot.Background;

            selectedCatalogIndex = -1;
            studioDeleteConfirm.Clear();
            studioDischargeConfirm.Clear();
            RefreshDraftVisual();
            RefreshStudioCatalog();
        }

        private static void OnStudioNewPilot()
        {
            draftPilot = new CustomPilotRecord
            {
                Callsign = PilotIdentity.RandomCallsign(),
                Name = PilotIdentity.RandomName(),
                Persona = ChatterPersona.Professional,
                Xp = 0,
                Background = "",
            };
            RandomizeDraftAppearance();
            draftPilot.Background = PilotIdentity.RandomBackground(draftPilot.Persona);

            if (studioCallsignField != null) studioCallsignField.text = draftPilot.Callsign;
            if (studioNameField != null) studioNameField.text = draftPilot.Name;
            if (studioBioField != null) studioBioField.text = draftPilot.Background;

            selectedCatalogIndex = -1;
            studioDeleteConfirm.Clear();
            studioDischargeConfirm.Clear();
            RefreshDraftVisual();
            RefreshStudioCatalog();
        }

        private static void OnStudioClonePilot()
        {
            if (string.IsNullOrWhiteSpace(draftPilot.Callsign)) return;
            string stem = draftPilot.Callsign;
            string newCall = stem + "-B";
            int idx = 2;
            while (studioPilotsList.Exists(p => string.Equals(p.Callsign, newCall, StringComparison.OrdinalIgnoreCase)))
            {
                newCall = stem + "-" + idx;
                idx++;
            }

            var cloned = draftPilot.Clone(newCall);
            PersonnelFacade.CustomPilots.SaveOrUpdatePilot(cloned);
            ReloadCustomPilotsList();
            int newIdx = studioPilotsList.FindIndex(p => string.Equals(p.Callsign, newCall, StringComparison.OrdinalIgnoreCase));
            if (newIdx >= 0)
            {
                studioCatalogPage = newIdx / StudioRowsPerPage;
                SelectStudioPilot(studioPilotsList[newIdx], newIdx);
            }
            WingCommandManager.Instance?.Toast($"Cloned pilot as {newCall}");
        }

        /// <summary>First press arms the three-second confirmation, second press deletes.</summary>
        private static void OnStudioDeletePressed()
        {
            if (string.IsNullOrWhiteSpace(draftPilot.Callsign)) return;
            if (!studioDeleteConfirm.IsArmedFor(draftPilot.Callsign))
            {
                studioDeleteConfirm.Arm(draftPilot.Callsign);
                RefreshStudioActions();
                return;
            }

            studioDeleteConfirm.Clear();
            OnStudioDeletePilot();
        }

        private static void OnStudioDeletePilot()
        {
            if (string.IsNullOrWhiteSpace(draftPilot.Callsign)) return;
            string call = draftPilot.Callsign;
            bool deleted = PersonnelFacade.CustomPilots.DeleteCustomPilot(call);
            ReloadCustomPilotsList();
            if (studioPilotsList.Count > 0)
            {
                SelectStudioPilot(studioPilotsList[0], 0);
            }
            else
            {
                OnStudioNewPilot();
            }
            WingCommandManager.Instance?.Toast(deleted ? $"Deleted pilot {call}" : $"Could not delete {call}");
        }

        /// <summary>Discharge keeps the three-second two-press confirmation; recruit is immediate.</summary>
        private static void OnStudioToggleRecruitSelected()
        {
            if (string.IsNullOrWhiteSpace(draftPilot.Callsign)) return;
            bool inSquadron = PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);
            if (inSquadron && !studioDischargeConfirm.IsArmedFor(draftPilot.Callsign))
            {
                studioDischargeConfirm.Arm(draftPilot.Callsign);
                RefreshStudioActions();
                return;
            }

            studioDischargeConfirm.Clear();
            if (inSquadron)
            {
                var pilot = PersonnelFacade.Roster.FindByCallsign(draftPilot.Callsign);
                if (pilot != null)
                {
                    PersonnelFacade.Roster.RemoveFromSquadron(pilot);
                    WingCommandManager.Instance?.Toast($"Discharged {draftPilot.Callsign} from squadron");
                }
            }
            else
            {
                PersonnelFacade.Roster.ImportCustom(draftPilot);
                WingCommandManager.Instance?.Toast($"Recruited {draftPilot.Callsign} to squadron");
            }
            RefreshDraftVisual();
            RefreshStudioCatalog();
        }

        private static void OnStudioSavePilot()
        {
            SaveStudioPilot();
        }

        private static bool SaveStudioPilot()
        {
            // Read fields before validating so a click straight from an active editor cannot save
            // stale draft data or reject a callsign the user has just entered.
            if (studioCallsignField != null) draftPilot.Callsign = studioCallsignField.text.Trim().ToUpperInvariant();
            if (studioNameField != null) draftPilot.Name = studioNameField.text.Trim();
            if (studioBioField != null) draftPilot.Background = studioBioField.text.Trim();

            if (string.IsNullOrWhiteSpace(draftPilot.Callsign))
            {
                WingCommandManager.Instance?.Toast("Callsign cannot be empty");
                return false;
            }

            PersonnelFacade.CustomPilots.SaveOrUpdatePilot(draftPilot);

            // Update live squadron record if pilot is currently in squadron
            var live = PersonnelFacade.Roster.FindByCallsign(draftPilot.Callsign);
            if (live != null)
            {
                live.Name = draftPilot.Name;
                live.Persona = draftPilot.Persona;
                live.Background = draftPilot.Background;
                live.Xp = draftPilot.Xp;
                live.PortraitSelection = draftPilot.Selection;
            }

            ReloadCustomPilotsList();
            int idx = studioPilotsList.FindIndex(p => string.Equals(p.Callsign, draftPilot.Callsign, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                studioCatalogPage = idx / StudioRowsPerPage;
                SelectStudioPilot(studioPilotsList[idx], idx);
            }
            WingCommandManager.Instance?.Toast($"Saved pilot {draftPilot.Callsign}");
            return true;
        }

        private static void OnStudioRecruitToSquadron()
        {
            if (!SaveStudioPilot()) return;
            if (!PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign))
            {
                PersonnelFacade.Roster.ImportCustom(draftPilot);
                WingCommandManager.Instance?.Toast($"Recruited {draftPilot.Callsign} to squadron");
            }
            RefreshDraftVisual();
            RefreshStudioCatalog();
        }

        private static void OnStudioImportAll()
        {
            int count = PersonnelFacade.CustomPilots.ImportAll(out int chatters, out string msg);
            ReloadCustomPilotsList();
            WingCommandManager.Instance?.Toast($"Imported {count} pilots ({chatters} chatter lines)");
        }

        private static void OnStudioExportAll()
        {
            var roster = PersonnelFacade.Roster.DisplayRoster();
            if (roster.Count == 0)
            {
                WingCommandManager.Instance?.Toast("No pilots in squadron to export");
                return;
            }

            var records = new List<CustomPilotRecord>();
            for (int i = 0; i < roster.Count; i++)
            {
                WingPilot p = roster[i];
                var record = new CustomPilotRecord
                {
                    Callsign = p.Callsign,
                    Name = p.Name,
                    Persona = p.Persona,
                    Background = p.Background,
                    Xp = p.Xp,
                    Kills = p.Kills,
                    Sorties = p.Sorties,
                };
                if (p.PortraitSelection.HasValue) record.ApplySelection(p.PortraitSelection.Value);
                records.Add(record);
            }

            bool saved = PersonnelFacade.CustomPilots.SaveCustomPilots(records, "exported_squadron.json");
            WingCommandManager.Instance?.Toast(saved
                ? $"Exported {records.Count} pilots to exported_squadron.json"
                : "Failed to export squadron");
        }

        private static void OnStudioRecruitAll()
        {
            int count = 0;
            for (int i = 0; i < studioPilotsList.Count; i++)
            {
                CustomPilotRecord rec = studioPilotsList[i];
                if (!PersonnelFacade.Roster.ContainsCallsign(rec.Callsign))
                {
                    PersonnelFacade.Roster.ImportCustom(rec);
                    count++;
                }
            }
            ReloadCustomPilotsList();
            WingCommandManager.Instance?.Toast($"Recruited {count} pilots to squadron");
        }
    }
}
