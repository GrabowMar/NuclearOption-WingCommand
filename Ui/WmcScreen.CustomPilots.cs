using System;
using System.Collections.Generic;
using System.IO;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        // Custom Pilot Studio UI and lifecycle.

        private static RectTransform customStudioRoot;
        private static RectTransform studioTableArea;
        private static TMP_Text studioTableEmptyLabel;
        private static RectTransform studioHeaderPager;
        private static WingButton studioPrevButton;
        private static WingButton studioNextButton;
        private static TMP_Text studioPageLabel;

        private static readonly List<StudioPilotRow> studioRows = new List<StudioPilotRow>();
        private static readonly List<CustomPilotRecord> studioPilotsList = new List<CustomPilotRecord>();
        private static int studioCatalogPage;
        private const int StudioRowsPerPage = 4;

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

        private sealed class StudioPilotRow
        {
            public readonly Image Fill;
            public readonly Image[] Outline;
            public readonly TMP_Text CallLabel;
            public readonly TMP_Text NameLabel;
            public readonly TMP_Text RankLabel;
            public readonly TMP_Text StatusLabel;
            public readonly WingButton Hit;

            public StudioPilotRow(RectTransform parent, float y)
            {
                float w = PanelWidth - Pad * 2f;
                Rect rect = new Rect(Pad, y, w, 20f);
                Fill = Panel(parent, rect, WingUi.CardFill);
                Outline = WingUi.Outline(parent, rect, FrameColor());

                CallLabel = Label(parent, "", new Rect(Pad + 6f, y, 76f, 20f),
                    Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                NameLabel = Label(parent, "", new Rect(Pad + 86f, y, 114f, 20f),
                    Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                RankLabel = Label(parent, "", new Rect(Pad + 204f, y, 76f, 20f),
                    Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                StatusLabel = Label(parent, "", new Rect(Pad + 284f, y, w - 290f, 20f),
                    Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Right);

                Hit = HitButton(parent, rect, null);
                Hide();
            }

            public void Bind(CustomPilotRecord record, bool isSelected, Action onClick)
            {
                bool inSquadron = PersonnelFacade.Roster.ContainsCallsign(record.Callsign);
                Fill.color = isSelected ? WingUi.CardFillSelected : WingUi.CardFill;
                if (Outline != null)
                {
                    Color border = isSelected ? Green() : FrameColor();
                    for (int i = 0; i < Outline.Length; i++)
                        if (Outline[i] != null) Outline[i].color = border;
                }

                CallLabel.text = record.Callsign;
                CallLabel.color = isSelected ? Green() : Friendly();

                NameLabel.text = AvTheme.Truncate(record.Name, 18);

                WingRank rank = PersonnelFacade.Roster.RankFor(record.Xp);
                RankLabel.text = PersonnelFacade.Roster.RankName(rank).ToUpperInvariant();
                RankLabel.color = RankColor(rank);

                StatusLabel.text = inSquadron ? "IN SQUADRON" : "READY";
                StatusLabel.color = inSquadron ? WingUi.RailEmerald : WingUi.RailCyan;

                Hit.SetAction(onClick);
                Hit.SetRowHighlight(Fill, isSelected ? WingUi.CardFillSelected : WingUi.CardFill, WingUi.CardFillHover);
                Hit.WithTooltip($"Select {record.Callsign} to inspect and edit");
                SetVisible(true);
            }

            public void Hide() => SetVisible(false);

            private void SetVisible(bool visible)
            {
                if (Fill != null && Fill.gameObject.activeSelf != visible) Fill.gameObject.SetActive(visible);
                if (CallLabel != null && CallLabel.gameObject.activeSelf != visible) CallLabel.gameObject.SetActive(visible);
                if (NameLabel != null && NameLabel.gameObject.activeSelf != visible) NameLabel.gameObject.SetActive(visible);
                if (RankLabel != null && RankLabel.gameObject.activeSelf != visible) RankLabel.gameObject.SetActive(visible);
                if (StatusLabel != null && StatusLabel.gameObject.activeSelf != visible) StatusLabel.gameObject.SetActive(visible);
                if (Hit != null && Hit.gameObject.activeSelf != visible) Hit.gameObject.SetActive(visible);
                if (Outline != null)
                {
                    for (int i = 0; i < Outline.Length; i++)
                        if (Outline[i] != null && Outline[i].gameObject.activeSelf != visible) Outline[i].gameObject.SetActive(visible);
                }
            }
        }

        private static float BuildCustomPilotsStudio(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;

            // Header & Back Navigation
            Heading(parent, y, "PILOT STUDIO");
            WingUi.Button(parent, "< SQUADRON",
                new Rect(PanelWidth - Pad - 96f, y - 2f, 96f, 20f), FontSmall, ShowSquadronView)
                .WithTooltip("Return to squadron roster view");
            y -= 26f;

            // Batch Toolbar (4 buttons)
            float btnW = (w - Gap * 3f) / 4f;
            WingUi.Button(parent, "IMPORT ALL",
                new Rect(Pad, y, btnW, RowHeight), FontSmall, OnStudioImportAll)
                .WithTooltip("Reload custom pilots from folder");
            WingUi.Button(parent, "EXPORT ALL",
                new Rect(Pad + (btnW + Gap), y, btnW, RowHeight), FontSmall, OnStudioExportAll)
                .WithTooltip("Export active squadron roster to exported_squadron.json");
            WingUi.Button(parent, "OPEN FOLDER",
                new Rect(Pad + (btnW + Gap) * 2f, y, btnW, RowHeight), FontSmall, PersonnelFacade.CustomPilots.OpenFolder)
                .WithTooltip("Open Pilots folder in Windows Explorer");
            WingUi.Button(parent, "RECRUIT ALL",
                new Rect(Pad + (btnW + Gap) * 3f, y, btnW, RowHeight), FontSmall, OnStudioRecruitAll)
                .WithTooltip("Recruit all unrecruited custom pilots into squadron");
            y -= RowHeight + Gap;

            // Column Headers & Header Pager
            Label(parent, "CALL", new Rect(Pad + 6f, y, 76f, Space4), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "NAME", new Rect(Pad + 86f, y, 114f, Space4), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "RANK", new Rect(Pad + 204f, y, 76f, Space4), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "STATUS", new Rect(Pad + 284f, y, 40f, Space4), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            studioHeaderPager = HeaderPager(parent, y,
                () => TurnStudioCatalogPage(-1),
                () => TurnStudioCatalogPage(1),
                out studioPrevButton, out studioPageLabel, out studioNextButton);
            y -= Space4 + 2f;

            // Catalog Table Area
            studioTableArea = new GameObject("StudioTableArea", typeof(RectTransform)).GetComponent<RectTransform>();
            studioTableArea.SetParent(parent, worldPositionStays: false);
            Place(studioTableArea, new Rect(Pad, y, w, 22f * StudioRowsPerPage));

            studioTableEmptyLabel = EmptyNote(studioTableArea, "No custom pilots found in folder.");
            studioRows.Clear();
            for (int i = 0; i < StudioRowsPerPage; i++)
            {
                studioRows.Add(new StudioPilotRow(parent, y - 22f * i));
            }
            y -= 22f * StudioRowsPerPage + Gap;

            // List Action Row (4 buttons)
            WingUi.Button(parent, "+ NEW", new Rect(Pad, y, btnW, 20f), FontSmall, OnStudioNewPilot)
                .WithTooltip("Create a new blank pilot record in the editor");
            WingUi.Button(parent, "CLONE", new Rect(Pad + (btnW + Gap), y, btnW, 20f), FontSmall, OnStudioClonePilot)
                .WithTooltip("Duplicate currently selected pilot with a new callsign");
            WingUi.Button(parent, "DELETE", new Rect(Pad + (btnW + Gap) * 2f, y, btnW, 20f), FontSmall, OnStudioDeletePilot)
                .WithTooltip("Delete currently selected custom pilot from file");
            studioRecruitSelectedButton = WingUi.Button(parent, "RECRUIT",
                new Rect(Pad + (btnW + Gap) * 3f, y, btnW, 20f), FontSmall, OnStudioToggleRecruitSelected);
            y -= 24f + Gap;

            // Tactical Card: Portrait & Identity Studio
            const float cardHeight = 150f;
            WingUi.TacticalCard(parent, new Rect(Pad, y, w, cardHeight), WingUi.RailCyan);

            // Left: Portrait (76 x 96) and Looks Reroll
            float pX = Pad + 8f;
            Panel(parent, new Rect(pX, y - 4f, 76f, 96f), AvTheme.Surface);

            var maskGo = new GameObject("StudioPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            var maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.SetParent(parent, worldPositionStays: false);
            Place(maskRt, new Rect(pX, y - 4f, 76f, 96f));

            var pGo = new GameObject("StudioPortrait", typeof(RectTransform), typeof(Image));
            var pRt = pGo.GetComponent<RectTransform>();
            pRt.SetParent(maskRt, worldPositionStays: false);
            studioPortraitImage = pGo.GetComponent<Image>();
            studioPortraitImage.color = Color.white;
            studioPortraitImage.raycastTarget = false;
            UpdatePortraitAspectFill(studioPortraitImage, PersonnelFacade.Portraits.Sprite, 76f, 96f);

            studioPortraitFrame = Outline(parent, new Rect(pX, y - 4f, 76f, 96f), RankColor(WingRank.Rookie));

            WingUi.Button(parent, "REROLL LOOKS", new Rect(pX, y - 128f, 76f, 18f), FontMicro, OnRerollLooks)
                .WithTooltip("Randomize body, face, hair, faction uniform, and backdrop");

            // Center: body-specific paper-doll steppers.
            float stX = pX + 76f + 8f;
            const float stW = 96f;
            const float stPitch = 21f;

            CreateStudioStepper(parent, stX, y - 4f, stW, "BODY", out bodyValueLabel,
                () => CycleBody(-1), () => CycleBody(1));

            CreateStudioStepper(parent, stX, y - 4f - stPitch, stW, "FACE", out faceValueLabel,
                () => CycleFace(-1), () => CycleFace(1));

            CreateStudioStepper(parent, stX, y - 4f - stPitch * 2f, stW, "HAIR", out hairValueLabel,
                () => CycleHair(-1), () => CycleHair(1));

            CreateStudioStepper(parent, stX, y - 4f - stPitch * 3f, stW, "SUIT", out uniformValueLabel,
                () => CycleUniform(-1), () => CycleUniform(1));

            CreateStudioStepper(parent, stX, y - 4f - stPitch * 4f, stW, "BACK", out backdropValueLabel,
                () => CycleBackdrop(-1), () => CycleBackdrop(1));

            Label(parent, "APPEARANCE", new Rect(stX, y - 129f, stW, 18f), Dim(), FontMicro, FontStyles.Italic, TextAlignmentOptions.Center);

            // Right: Identity & Characteristics
            float idX = stX + stW + 10f;
            float idW = w - (idX - Pad) - 6f;

            // Row 1: Callsign
            Label(parent, "CALL", new Rect(idX, y - 4f, 36f, 20f), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            studioCallsignField = WingUi.InputField(parent, new Rect(idX + 38f, y - 4f, idW - 74f, 20f), 14,
                val => { draftPilot.Callsign = val.Trim().ToUpperInvariant(); RefreshDraftVisual(); },
                placeholderText: "CALLSIGN");
            WingUi.Button(parent, "RND", new Rect(idX + idW - 32f, y - 4f, 32f, 20f), FontMicro, OnRandomizeCallsign)
                .WithTooltip("Roll random callsign");

            // Row 2: Name
            Label(parent, "NAME", new Rect(idX, y - 4f - stPitch, 36f, 20f), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            studioNameField = WingUi.InputField(parent, new Rect(idX + 38f, y - 4f - stPitch, idW - 74f, 20f), 24,
                val => { draftPilot.Name = val.Trim(); },
                placeholderText: "NAME");
            WingUi.Button(parent, "RND", new Rect(idX + idW - 32f, y - 4f - stPitch, 32f, 20f), FontMicro, OnRandomizeName)
                .WithTooltip("Roll random name");

            // Row 3: Persona / Radio Style
            Label(parent, "STYLE", new Rect(idX, y - 4f - stPitch * 2f, 36f, 20f), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            CreateStudioStepper(parent, idX + 38f, y - 4f - stPitch * 2f, idW - 38f, null, out personaValueLabel,
                () => CyclePersona(-1), () => CyclePersona(1));

            // Row 4: Rank / XP
            Label(parent, "RANK", new Rect(idX, y - 4f - stPitch * 3f, 36f, 20f), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            CreateStudioStepper(parent, idX + 38f, y - 4f - stPitch * 3f, idW - 38f, null, out rankValueLabel,
                () => CycleRank(-1), () => CycleRank(1));

            // Row 5: Status Tag
            studioStatusLabel = Label(parent, "", new Rect(idX + 38f, y - 128f, idW - 38f, 18f),
                Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            y -= cardHeight + Gap + 2f;

            // Background & Lore Box
            Label(parent, "BACKGROUND / LORE", new Rect(Pad, y, w, 14f), Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 16f;

            studioBioField = WingUi.InputField(parent, new Rect(Pad, y, w, 72f), 280,
                val => { draftPilot.Background = val; },
                placeholderText: "Enter pilot background notes or military history...",
                lineType: TMP_InputField.LineType.MultiLineNewline);
            y -= 76f;

            float bioBtnW = (w - Gap) / 2f;
            WingUi.Button(parent, "GENERATE BIO", new Rect(Pad, y, bioBtnW, 20f), FontSmall, OnGenerateBio)
                .WithTooltip("Generate background text matching pilot persona and callsign");
            WingUi.Button(parent, "RANDOMIZE ALL", new Rect(Pad + bioBtnW + Gap, y, bioBtnW, 20f), FontSmall, OnRandomizeAll)
                .WithTooltip("Generate a completely fresh random pilot (identity, looks, and lore)");
            y -= 24f + Gap;

            // Footer Action Buttons
            float footW = (w - Gap) / 2f;
            WingUi.Button(parent, "SAVE PILOT", new Rect(Pad, y, footW, 24f), FontSmall, OnStudioSavePilot)
                .WithTooltip("Save or update this custom pilot record in custom_pilots.json");
            WingUi.Button(parent, "RECRUIT TO SQUADRON", new Rect(Pad + footW + Gap, y, footW, 24f), FontSmall, OnStudioRecruitToSquadron)
                .WithTooltip("Recruit this pilot into active squadron roster");

            return y - 28f;
        }

        private static void CreateStudioStepper(RectTransform parent, float x, float y, float w,
            string labelPrefix, out TMP_Text valueLabel, Action onPrev, Action onNext)
        {
            Panel(parent, new Rect(x, y, w, 20f), RowColor());
            Outline(parent, new Rect(x, y, w, 20f), FrameColor());
            const float arrow = 18f;

            WingUi.Button(parent, "<", new Rect(x + 1f, y - 1f, arrow, 18f),
                FontSmall, UiButtonStyle.Quiet, onPrev);
            valueLabel = Label(parent, "", new Rect(x + arrow, y, w - arrow * 2f, 20f),
                Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            WingUi.Button(parent, ">", new Rect(x + w - arrow - 1f, y - 1f, arrow, 18f),
                FontSmall, UiButtonStyle.Quiet, onNext);
        }

        public static void ShowPilotStudioView()
        {
            if (squadronViewRoot != null) squadronViewRoot.gameObject.SetActive(false);
            if (customStudioRoot != null) customStudioRoot.gameObject.SetActive(true);

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
            RefreshHeaderPager(studioHeaderPager, studioPrevButton, studioPageLabel, studioNextButton, studioCatalogPage, pageCount);

            bool empty = total == 0;
            if (studioTableEmptyLabel != null) studioTableEmptyLabel.gameObject.SetActive(empty);

            int startIndex = studioCatalogPage * StudioRowsPerPage;
            for (int i = 0; i < studioRows.Count; i++)
            {
                int itemIndex = startIndex + i;
                if (itemIndex < total)
                {
                    CustomPilotRecord rec = studioPilotsList[itemIndex];
                    bool isSelected = (itemIndex == selectedCatalogIndex);
                    int captureIndex = itemIndex;
                    studioRows[i].Bind(rec, isSelected, () => SelectStudioPilot(rec, captureIndex));
                }
                else
                {
                    studioRows[i].Hide();
                }
            }

            // Update recruit/discharge selected button
            if (studioRecruitSelectedButton != null)
            {
                bool inSquadron = !string.IsNullOrEmpty(draftPilot.Callsign) &&
                    PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);
                studioRecruitSelectedButton.SetText(inSquadron ? "DISCHARGE" : "RECRUIT");
                studioRecruitSelectedButton.WithTooltip(inSquadron
                    ? $"Remove {draftPilot.Callsign} from squadron"
                    : $"Recruit {draftPilot.Callsign} into squadron");
            }
        }

        private static void TurnStudioCatalogPage(int dir)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(studioPilotsList.Count / (float)StudioRowsPerPage));
            studioCatalogPage = Mathf.Clamp(studioCatalogPage + dir, 0, pageCount - 1);
            RefreshStudioCatalog();
        }

        private static void SelectStudioPilot(CustomPilotRecord record, int index = -1)
        {
            selectedCatalogIndex = index;
            draftPilot = record.Clone();
            if (!draftPilot.HasCustomPortrait) draftPilot.ApplySelection(PilotPortraitGenerator.DefaultSelection);

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
            if (faceValueLabel != null) faceValueLabel.text = $"FACE {selection.Face + 1}/{PilotPortraitGenerator.FacesPerBody}";
            if (hairValueLabel != null) hairValueLabel.text = selection.Hair == 0 ? "BALD" : $"HAIR {selection.Hair}";
            if (uniformValueLabel != null) uniformValueLabel.text = PilotPortraitGenerator.UniformLabel(selection.Uniform);
            if (backdropValueLabel != null) backdropValueLabel.text = $"BACK {selection.Backdrop + 1}/{PilotPortraitGenerator.BackdropCount}";

            if (personaValueLabel != null) personaValueLabel.text = draftPilot.Persona.ToString().ToUpperInvariant();

            WingRank rank = PersonnelFacade.Roster.RankFor(draftPilot.Xp);
            if (rankValueLabel != null) rankValueLabel.text = $"{PersonnelFacade.Roster.RankName(rank).ToUpperInvariant()} ({draftPilot.Xp} XP)";

            if (studioPortraitImage != null)
            {
                Sprite portraitSprite = PersonnelFacade.Portraits.ForSelection(selection);
                UpdatePortraitAspectFill(studioPortraitImage, portraitSprite, 76f, 96f);
            }

            if (studioPortraitFrame != null)
            {
                Color border = RankColor(rank);
                for (int i = 0; i < studioPortraitFrame.Length; i++)
                    if (studioPortraitFrame[i] != null) studioPortraitFrame[i].color = border;
            }

            if (studioStatusLabel != null)
            {
                bool inSquadron = !string.IsNullOrEmpty(draftPilot.Callsign) &&
                    PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);
                studioStatusLabel.text = inSquadron ? "STATUS: [IN SQUADRON]" : "STATUS: [READY TO RECRUIT]";
                studioStatusLabel.color = inSquadron ? WingUi.RailEmerald : WingUi.RailCyan;
            }
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

        private static void OnStudioToggleRecruitSelected()
        {
            if (string.IsNullOrWhiteSpace(draftPilot.Callsign)) return;
            bool inSquadron = PersonnelFacade.Roster.ContainsCallsign(draftPilot.Callsign);
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
