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
    /// <summary>WING page for pilot history and current aircraft details.</summary>
    internal static partial class WmcScreen
    {
        // Pilot inspection state.
        private static readonly List<PilotRow> pilotRows = new List<PilotRow>();
        private static RectTransform pilotRosterArea;
        private static TMP_Text pilotEmptyLabel;
        private static PilotPager pilotPager;
        private static AvKit.Popup customPilotsPopup;
        private static float customPilotsRowY;
        private static WingButton sarButton;
        private static WingButton localSarButton;

        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static TMP_Text pilotStatsLabel;
        private static TMP_Text pilotPersonaLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;

        private const float PortraitWidth = 92f;
        private const float PortraitHeight = 138f;

        private static Image pilotPortrait;
        private static Image pilotCardRail;
        private static Image[] pilotPortraitFrame;
        private static Image pilotKiaOverlay;
        private static readonly List<PilotSkillCard> pilotSkillCards = new List<PilotSkillCard>();
        private static TMP_Text pilotSkillsEmptyLabel;
        private static Image airframeCardRail;

        private static Image airframeSilhouette;
        private static TMP_Text airframeNameLabel;
        private static TMP_Text airframeSlotLabel;

        private static RectTransform squadronViewRoot;

        // Wing-page construction.

        /// <summary>Squadron dossier and explicit SAR dispatch. SUPPLY chooses the next pilot; aircraft
        /// details follow the inspected pilot or show recovery status.</summary>
        private static float AddWingPage(RectTransform parent, float y)
        {
            squadronViewRoot = new GameObject("SquadronViewRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            squadronViewRoot.SetParent(parent, worldPositionStays: false);
            squadronViewRoot.anchorMin = Vector2.zero;
            squadronViewRoot.anchorMax = Vector2.one;
            squadronViewRoot.offsetMin = Vector2.zero;
            squadronViewRoot.offsetMax = Vector2.zero;

            customStudioRoot = new GameObject("CustomStudioRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            customStudioRoot.SetParent(parent, worldPositionStays: false);
            customStudioRoot.anchorMin = Vector2.zero;
            customStudioRoot.anchorMax = Vector2.one;
            customStudioRoot.offsetMin = Vector2.zero;
            customStudioRoot.offsetMax = Vector2.zero;
            customStudioRoot.gameObject.SetActive(false);

            float studioBottom = BuildCustomPilotsStudio(customStudioRoot, y);
            float squadronBottom = BuildSquadronView(squadronViewRoot, y);

            return Mathf.Min(squadronBottom, studioBottom);
        }

        private static float BuildSquadronView(RectTransform parent, float y)
        {
            y = Heading(parent, y, "SQUADRON");
            y = ColumnHeaders(parent, y, PilotColumns);

            pilotRosterArea = RosterViewport(parent, "PilotRoster", y, SquadronRowsPerPage);
            pilotEmptyLabel = EmptyNote(pilotRosterArea,
                "No pilots in the squadron yet.");
            y -= RowPitch * SquadronRowsPerPage + Gap;

            pilotPager = new PilotPager(parent, y);
            y -= RowHeight + Gap;

            customPilotsPopup = new AvKit.Popup(parent, PanelWidth);
            customPilotsRowY = y;

            float buttonW = (PanelWidth - Pad * 2f - Gap) / 2f;
            WingUi.Button(parent, "RECRUIT RANDOM",
                new Rect(Pad, y, buttonW, RowHeight), FontSmall, OnRecruitPilot)
                .WithTooltip("Recruit a random custom pilot into the squadron roster");

            WingUi.Button(parent, "CUSTOM PILOTS",
                new Rect(Pad + buttonW + Gap, y, buttonW, RowHeight), FontSmall, OnCustomPilots)
                .WithTooltip("Open Pilot Studio. Ctrl-click opens the quick list; Shift-click opens the pilot folder.");

            y -= RowHeight + Gap;

            y = Heading(parent, y, "PILOT DOSSIER");
            float w = PanelWidth - Pad * 2f;

            var (_, dossierRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, 180f), WingUi.RailCyan);
            pilotCardRail = dossierRail;
            y -= 8f;
            const float portraitX = Pad + 8f;
            const float portraitGap = Space3;

            // Portrait column.
            Panel(parent, new Rect(portraitX, y, PortraitWidth, PortraitHeight), AvTheme.Surface);

            var maskGo = new GameObject("PilotPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            RectTransform maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.SetParent(parent, worldPositionStays: false);
            Place(maskRt, new Rect(portraitX, y, PortraitWidth, PortraitHeight));

            var portraitGo = new GameObject("PilotPortrait", typeof(RectTransform), typeof(Image));
            RectTransform pRt = portraitGo.GetComponent<RectTransform>();
            pRt.SetParent(maskRt, worldPositionStays: false);
            pilotPortrait = portraitGo.GetComponent<Image>();
            pilotPortrait.color = Color.white;
            pilotPortrait.raycastTarget = false;
            UpdatePortraitAspectFill(pilotPortrait, PersonnelFacade.Portraits.Sprite, PortraitWidth, PortraitHeight);

            // Overlay a subtle loss tint without covering the face.
            var kiaOverlayGo = new GameObject("PilotKiaOverlay", typeof(RectTransform), typeof(Image));
            RectTransform kiaRt = kiaOverlayGo.GetComponent<RectTransform>();
            kiaRt.SetParent(maskRt, worldPositionStays: false);
            Stretch(kiaRt);
            pilotKiaOverlay = kiaOverlayGo.GetComponent<Image>();
            pilotKiaOverlay.color = new Color(Alert().r, Alert().g, Alert().b, 0.18f);
            pilotKiaOverlay.raycastTarget = false;
            pilotKiaOverlay.gameObject.SetActive(false);

            pilotPortraitFrame = Outline(parent, new Rect(portraitX, y, PortraitWidth, PortraitHeight), RankColor(WingRank.Rookie));

            // Pilot identity, skills, and biography column.
            float dossierX = portraitX + PortraitWidth + portraitGap;
            float dossierW = (Pad + w - 8f) - dossierX;

            pilotIdentityLabel = Label(parent, "", new Rect(dossierX, y, dossierW, Space5), Green(), FontLead,
                                       FontStyles.Bold, TextAlignmentOptions.Left);
            float detailY = y - Space5;

            pilotRankLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                   FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), Green());
            pilotXpBarWidth = dossierW;
            detailY -= Space3;

            pilotStatsLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                    FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            pilotPersonaLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight + 3f;

            // Biography within the dossier column - full comfortable height without cramped skill icons!
            const float bioH = 78f;
            pilotBackgroundLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, bioH),
                                         Friendly(), FontMicro, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;

            y = Mathf.Min(y - PortraitHeight, detailY - bioH) - Space4;

            // Dedicated Pilot Skills & Perks Section
            y = Heading(parent, y, "PILOT SKILLS & PERKS");
            const float skillsCardH = 88f;
            WingUi.TacticalCard(parent, new Rect(Pad, y, w, skillsCardH), WingUi.RailCyan);

            pilotSkillsEmptyLabel = Label(parent, "NO ABILITIES EARNED YET\nCombat sorties and promotions unlock tactical perks & survival skills.",
                new Rect(Pad + Space3, y - 46f, w - Space6, 40f),
                Dim(), FontMicro, FontStyles.Italic, TextAlignmentOptions.Center);
            pilotSkillsEmptyLabel.gameObject.SetActive(false);

            pilotSkillCards.Clear();
            float colW = (w - Space4 - Gap) * 0.5f;
            const float cardPitchY = 40f;
            const float cardH = 36f;
            for (int r = 0; r < 2; r++)
            {
                float rowY = y - 4f - r * cardPitchY;
                for (int c = 0; c < 2; c++)
                {
                    float cardX = Pad + Space2 + c * (colW + Gap);
                    pilotSkillCards.Add(new PilotSkillCard(parent, new Rect(cardX, rowY, colW, cardH)));
                }
            }

            y -= skillsCardH + Gap;

            // Compact Deduplicated Airframe Assignment Section
            y = Heading(parent, y, "AIRFRAME ASSIGNMENT");
            const float airframeCardH = 46f;
            var (_, airframeRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, airframeCardH), WingUi.RailEmerald);
            airframeCardRail = airframeRail;

            const float silW = 38f;
            airframeSilhouette = AddSprite(parent, "AirframeSilhouette",
                      IconFactory.Get("airframe"),
                      new Rect(Pad + 6f, y - 4f, silW, silW), WingColor());

            float airframeTextX = Pad + 6f + silW + 8f;
            const float sarW = 82f;
            const float sarGap = 4f;
            float sarActionsW = sarW * 2f + sarGap;
            float airframeTextW = w - (6f + silW + 8f) - sarActionsW - Space2;

            airframeNameLabel = Label(parent, "", new Rect(airframeTextX, y - 4f, airframeTextW, 18f), Friendly(),
                                      FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            airframeSlotLabel = Label(parent, "", new Rect(airframeTextX, y - 24f, airframeTextW, 16f), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            float sarX = Pad + w - sarActionsW - 4f;
            sarButton = WingUi.Button(parent, "AIR SAR",
                new Rect(sarX, y - 9f, sarW, 28f), FontSmall, UiButtonStyle.Danger,
                () => PersonnelFacade.SearchAndRescue.Dispatch(inspectPilot, WingCommandManager.Instance?.Wing))
                .WithTooltip("Send the nearest idle rescue-capable wing helicopter to this downed pilot on land. Water rescue uses the native hoist.");
            sarButton.gameObject.SetActive(false);

            localSarButton = WingUi.Button(parent, "LOCAL 10M",
                new Rect(sarX + sarW + sarGap, y - 9f, sarW, 28f), FontSmall, UiButtonStyle.Default,
                OnLocalRecovery)
                .WithTooltip("Organize local recovery for 10,000,000 funds. Completes in five mission minutes; the pilot remains at risk until then.");
            localSarButton.gameObject.SetActive(false);

            y -= airframeCardH + Gap;
            return y;
        }

        private static void RefreshWingPage(WingRegistry wing)
        {
            if (customStudioRoot != null && customStudioRoot.gameObject.activeSelf) return;

            List<WingPilot> display = PersonnelFacade.Roster.DisplayRoster();
            int count = display.Count;

            SyncPilotRows(pilotRows, pilotRosterArea);
            int first = pilotPager != null ? pilotPager.Refresh(count) : 0;

            if (inspectPilot != null && !PersonnelFacade.Roster.Contains(inspectPilot))
                inspectPilot = count > 0 ? display[0] : null;
            if (inspectPilot == null && count > 0)
                inspectPilot = display[0];

            bool empty = count == 0;
            if (pilotEmptyLabel != null && pilotEmptyLabel.gameObject.activeSelf != empty)
                pilotEmptyLabel.gameObject.SetActive(empty);

            for (int i = 0; i < pilotRows.Count; i++)
            {
                int index = first + i;
                if (index >= count)
                {
                    pilotRows[i].Hide();
                    continue;
                }

                WingPilot pilot = display[index];
                pilotRows[i].Bind(pilot, inspectPilot == pilot, () => inspectPilot = pilot);
            }

            WingPilot focus = inspectPilot;
            bool downed = focus != null && !focus.Lost && focus.RecoveryStatus == PilotRecoveryStatus.Downed;
            bool recoverable = downed || (focus != null && !focus.Lost &&
                focus.RecoveryStatus == PilotRecoveryStatus.Missing);
            float localRemaining = recoverable
                ? PersonnelFacade.SearchAndRescue.LocalRecoveryRemaining(focus)
                : -1f;
            if (sarButton != null)
            {
                sarButton.gameObject.SetActive(recoverable);
                sarButton.SetEnabled(downed);
                sarButton.SetLatched(false);
                sarButton.WithTooltip(downed
                    ? "Send an idle rescue-capable wing helicopter to this downed pilot on land."
                    : "AIR SAR requires a confirmed survivor location. Use LOCAL SAR to search for a missing pilot.");
            }
            if (localSarButton != null)
            {
                bool active = localRemaining >= 0f;
                localSarButton.gameObject.SetActive(recoverable);
                localSarButton.SetEnabled(recoverable && !active &&
                    EconomyFacade.Shop.Allocation >= PersonnelFacade.SearchAndRescue.LocalRecoveryCost);
                localSarButton.SetLatched(active);
                if (active)
                {
                    int seconds = Mathf.CeilToInt(localRemaining);
                    localSarButton.SetText((seconds / 60).ToString("00") + ":" +
                        (seconds % 60).ToString("00"));
                }
                else
                {
                    localSarButton.SetText("LOCAL 10M");
                }
            }

            if (focus == null)
            {
                SetWingDetail("NO PILOT SELECTED", "", "", "", 0f,
                    "Recruit a pilot using RECRUIT RANDOM or CUSTOM PILOTS above, or requisition an aircraft on the SUPPLY tab.",
                    "NO AIRFRAME", "Select a pilot above to inspect active flight assignment");
                SetSilhouetteAlpha(0f);
                RenderPilotVisual(null);
                if (pilotSkillsEmptyLabel != null) pilotSkillsEmptyLabel.gameObject.SetActive(false);
                for (int i = 0; i < pilotSkillCards.Count; i++) pilotSkillCards[i].Hide();
                return;
            }

            RenderPilotVisual(focus);

            bool kia = focus.Lost;
            WingMember flying = FlyingMember(wing, focus);

            Color frameColor = kia ? Alert() : RankColor(focus.Rank);
            if (pilotCardRail != null)
                pilotCardRail.color = frameColor;
            if (pilotPortraitFrame != null)
            {
                for (int i = 0; i < pilotPortraitFrame.Length; i++)
                {
                    if (pilotPortraitFrame[i] != null)
                        pilotPortraitFrame[i].color = frameColor;
                }
            }

            string identity = (kia ? "†  " : "") + focus.Callsign + "  ·  " + focus.Name;
            string rank;
            float progress;
            if (kia)
            {
                rank = PersonnelFacade.Roster.RankName(focus.Rank) + "   LOST IN ACTION" +
                       (flying != null ? "   IN AIR" : "");
                progress = 0f;
            }
            else
            {
                WingRank crewRank = focus.Rank;
                if (crewRank >= PersonnelFacade.Roster.TopRank)
                {
                    rank = PersonnelFacade.Roster.RankName(crewRank) + "   XP " + focus.Xp + "   MAX RANK";
                    progress = 1f;
                }
                else
                {
                    int floor = PersonnelFacade.Roster.XpForRank(crewRank);
                    int ceiling = PersonnelFacade.Roster.XpForRank(crewRank + 1);
                    rank = PersonnelFacade.Roster.RankName(crewRank) + "   XP " + focus.Xp + " / " + ceiling;
                    progress = ceiling > floor
                        ? Mathf.Clamp01((focus.Xp - floor) / (float)(ceiling - floor))
                        : 0f;
                }
                if (flying != null) rank += "   ·   IN AIR";
            }

            string stats = "COMBAT RECORD   " + focus.Kills + " KILL(S)   /   " +
                           focus.Sorties + " SORTIE(S)" + (kia ? "   —   KIA" : "");
            string persona = kia
                ? "STATUS   KILLED IN ACTION"
                : focus.RecoveryStatus != PilotRecoveryStatus.None
                    ? "STATUS   " + PersonnelFacade.SearchAndRescue.Status(focus)
                    : "RADIO PROFILE   " + focus.Persona.ToString().ToUpperInvariant();

            if (pilotIdentityLabel != null) pilotIdentityLabel.color = kia ? Alert() : Green();

            // Bind dedicated pilot skills & perks
            List<PilotPerk> perks = focus.Perks;
            int perkCount = (perks != null && !kia) ? perks.Count : 0;
            if (pilotSkillsEmptyLabel != null)
                pilotSkillsEmptyLabel.gameObject.SetActive(perkCount == 0);

            int maxCards = pilotSkillCards.Count;
            for (int i = 0; i < maxCards; i++)
            {
                if (i < maxCards - 1 && i < perkCount)
                {
                    pilotSkillCards[i].Bind(perks[i]);
                }
                else if (i == maxCards - 1 && perkCount > maxCards)
                {
                    pilotSkillCards[i].BindExtra(perkCount - (maxCards - 1));
                }
                else if (i < perkCount)
                {
                    pilotSkillCards[i].Bind(perks[i]);
                }
                else
                {
                    pilotSkillCards[i].Hide();
                }
            }

            if (flying == null)
            {
                SetSilhouetteAlpha(0.25f);
                if (airframeSilhouette != null) airframeSilhouette.sprite = IconFactory.Get("airframe");
                string planeName = kia ? (focus.LastAircraft ?? "Unknown aircraft") :
                    focus.RecoveryStatus == PilotRecoveryStatus.Missing ? "LOCATION UNKNOWN" : "ON THE GROUND";
                string slotStatus = kia
                    ? "CAUSE: " + (focus.LossCause ?? "Unknown")
                    : focus.RecoveryStatus != PilotRecoveryStatus.None
                        ? PersonnelFacade.SearchAndRescue.Status(focus)
                        : "AWAITING AIRFRAME ASSIGNMENT";

                SetWingDetail(identity, rank, stats, persona, progress, focus.Background, planeName, slotStatus);
                if (airframeNameLabel != null)
                    airframeNameLabel.color = kia ? Alert() : Dim();
                if (airframeSlotLabel != null)
                    airframeSlotLabel.color = kia ? Alert() : Friendly();
                if (airframeCardRail != null)
                    airframeCardRail.color = kia ? Alert() : WingUi.RailEmerald;
                return;
            }

            Aircraft aircraft = flying.Aircraft;
            AircraftDefinition definition = DefinitionOf(flying);

            if (airframeSilhouette != null)
            {
                Sprite planeSprite = IconFactory.Aircraft(definition);
                airframeSilhouette.sprite = planeSprite;
            }

            SetSilhouetteAlpha(1.0f);

            string assignedName = definition != null
                ? definition.unitName
                : flying.Name;
            string assignedSlot = "SLOT " + flying.Slot + (flying.IsFlightLead ? " · FLIGHT LEAD" : "");
            if (aircraft != null && !aircraft.LocalSim)
                assignedSlot += " (REMOTE)";

            SetWingDetail(identity, rank, stats, persona, progress, focus.Background, assignedName, assignedSlot);
            if (airframeNameLabel != null)
                airframeNameLabel.color = Green();
            if (airframeSlotLabel != null)
                airframeSlotLabel.color = Friendly();
            if (airframeCardRail != null)
                airframeCardRail.color = WingUi.RailEmerald;
        }

        /// <summary>Set faint aircraft-silhouette opacity or hide it on empty pages.</summary>
        private static void SetSilhouetteAlpha(float alpha)
        {
            if (airframeSilhouette == null) return;
            Color c = WingColor();
            c.a = alpha;
            airframeSilhouette.color = c;
        }

        /// <summary>
        /// Display the pilot's generated portrait with loss tint and rank frame.
        /// </summary>
        private static void RenderPilotVisual(WingPilot pilot)
        {
            if (pilotPortrait != null)
            {
                pilotPortrait.color = pilot == null
                    ? Color.white
                    : pilot.Lost ? new Color(0.7f, 0.45f, 0.45f, 0.85f) : Color.white;
                UpdatePortraitAspectFill(pilotPortrait, PersonnelFacade.Portraits.For(pilot), PortraitWidth, PortraitHeight);
            }

            if (pilotKiaOverlay != null) pilotKiaOverlay.gameObject.SetActive(pilot != null && pilot.Lost);

            Color frameColor = pilot == null
                ? FrameColor()
                : pilot.Lost ? Alert() : RankColor(pilot.Rank);

            if (pilotPortraitFrame != null)
            {
                for (int i = 0; i < pilotPortraitFrame.Length; i++)
                {
                    if (pilotPortraitFrame[i] != null)
                        pilotPortraitFrame[i].color = frameColor;
                }
            }
        }

        private static void UpdatePortraitAspectFill(Image image, Sprite sprite, float containerW, float containerH)
        {
            if (image == null) return;
            image.sprite = sprite;
            if (sprite == null) return;

            var (renderedW, renderedH) = MfdPresentationRules.CalculateAspectFill(
                containerW, containerH, sprite.rect.width, sprite.rect.height);

            RectTransform rt = image.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(renderedW, renderedH);
            image.preserveAspect = false;
        }

        private static void SetWingDetail(string identity, string rank, string stats,
                                          string persona, float progress, string background,
                                          string airframeName, string airframeSlot)
        {
            if (pilotIdentityLabel != null) pilotIdentityLabel.text = identity;
            if (pilotRankLabel != null) pilotRankLabel.text = rank;
            if (pilotStatsLabel != null) pilotStatsLabel.text = stats;
            if (pilotPersonaLabel != null) pilotPersonaLabel.text = persona;
            if (pilotBackgroundLabel != null) pilotBackgroundLabel.text = background;
            if (airframeNameLabel != null) airframeNameLabel.text = airframeName;
            if (airframeSlotLabel != null) airframeSlotLabel.text = airframeSlot;

            if (pilotXpBar != null)
                pilotXpBar.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, pilotXpBarWidth * Mathf.Clamp01(progress)), 3f);
        }

        private sealed class PilotSkillCard
        {
            public readonly Image Fill;
            public readonly Image[] Outline;
            public readonly Image Icon;
            public readonly TMP_Text TitleLabel;
            public readonly TMP_Text DescLabel;
            public readonly WingButton Hit;
            public string Title { get; private set; }
            public string Description { get; private set; }

            public PilotSkillCard(RectTransform parent, Rect rect)
            {
                Fill = Panel(parent, rect, WingUi.CardFillSelected);
                Outline = WingUi.Outline(parent, rect, Green());

                const float iconSize = 28f;
                Icon = AddSprite(parent, "SkillIcon", null,
                                 new Rect(rect.x + 4f, rect.y - 4f, iconSize, iconSize),
                                 WingUi.RailEmerald);

                float textX = rect.x + 4f + iconSize + 6f;
                float textW = rect.width - (4f + iconSize + 6f) - 4f;
                TitleLabel = Label(parent, "", new Rect(textX, rect.y - 2f, textW, 16f),
                                   Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
                DescLabel = Label(parent, "", new Rect(textX, rect.y - 18f, textW, 16f),
                                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                DescLabel.overflowMode = TextOverflowModes.Ellipsis;

                Hit = HitButton(parent, rect, () => {
                    if (!string.IsNullOrEmpty(Title))
                        WingCommandManager.Instance?.Toast(Title + ": " + Description);
                });
                SetVisible(false);
            }

            public void Bind(PilotPerk perk)
            {
                Title = PilotPerks.Name(perk);
                Description = PilotPerks.Description(perk);
                string key = PilotPerks.IconKey(perk);
                if (Icon != null)
                {
                    Icon.sprite = IconFactory.Get(key);
                    Icon.color = WingUi.RailEmerald;
                }
                if (TitleLabel != null) TitleLabel.text = Title;
                if (DescLabel != null) DescLabel.text = Description;
                Hit.WithTooltip(Title + " — " + Description);
                SetVisible(true);
            }

            public void BindExtra(int extraCount)
            {
                Title = $"+{extraCount} MORE";
                Description = "Additional combat perks active";
                if (Icon != null)
                {
                    Icon.sprite = IconFactory.Get("rank_legend");
                    Icon.color = WingUi.RailEmerald;
                }
                if (TitleLabel != null) TitleLabel.text = Title;
                if (DescLabel != null) DescLabel.text = Description;
                Hit.WithTooltip($"+{extraCount} additional combat perks active");
                SetVisible(true);
            }

            public void Hide()
            {
                SetVisible(false);
            }

            private void SetVisible(bool visible)
            {
                if (Fill != null && Fill.gameObject.activeSelf != visible) Fill.gameObject.SetActive(visible);
                if (Icon != null && Icon.gameObject.activeSelf != visible) Icon.gameObject.SetActive(visible);
                if (TitleLabel != null && TitleLabel.gameObject.activeSelf != visible) TitleLabel.gameObject.SetActive(visible);
                if (DescLabel != null && DescLabel.gameObject.activeSelf != visible) DescLabel.gameObject.SetActive(visible);
                if (Hit != null && Hit.gameObject.activeSelf != visible) Hit.gameObject.SetActive(visible);
                if (Outline != null)
                {
                    for (int i = 0; i < Outline.Length; i++)
                    {
                        if (Outline[i] != null && Outline[i].gameObject.activeSelf != visible)
                            Outline[i].gameObject.SetActive(visible);
                    }
                }
            }
        }

        private static void OnRecruitPilot()
        {
            WingPilot pilot = PersonnelFacade.Roster.RecruitManual();
            if (pilot != null)
            {
                inspectPilot = pilot;
                WingCommandManager.Instance?.Toast("Recruited " + pilot.Callsign + " (" + pilot.Name + ")");
                RefreshWingPage(WingCommandManager.Instance?.Wing);
            }
        }

        private static void OnLocalRecovery()
        {
            PersonnelFacade.SearchAndRescue.OrganizeLocalRecovery(inspectPilot);
            RefreshWingPage(WingCommandManager.Instance?.Wing);
        }

        private static void OnCustomPilots()
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                PersonnelFacade.CustomPilots.OpenFolder();
                return;
            }
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                OpenCustomPilotsDropdown();
                return;
            }

            ShowPilotStudioView();
        }

        private static void OpenCustomPilotsDropdown()
        {
            List<CustomPilotRecord> pilots = PersonnelFacade.CustomPilots.LoadAllCustomPilots(out _);
            if (pilots.Count == 0)
            {
                WingCommandManager.Instance?.Toast("No custom pilots found in folder. Sample file created.");
                return;
            }

            int unrecruitedCount = 0;
            for (int i = 0; i < pilots.Count; i++)
            {
                if (!PersonnelFacade.Roster.ContainsCallsign(pilots[i].Callsign))
                    unrecruitedCount++;
            }

            var choices = new List<CustomPilotRecord>(pilots.Count + 1);
            popupEntries.Clear();

            if (unrecruitedCount > 1)
            {
                choices.Add(null);
                popupEntries.Add(new AvKit.PopupEntry(
                    "RECRUIT ALL (" + unrecruitedCount + ")",
                    "all unrecruited",
                    false));
            }

            for (int i = 0; i < pilots.Count; i++)
            {
                CustomPilotRecord record = pilots[i];
                choices.Add(record);

                bool inSquadron = PersonnelFacade.Roster.ContainsCallsign(record.Callsign);
                bool isInspected = inspectPilot != null &&
                    string.Equals(inspectPilot.Callsign, record.Callsign, StringComparison.OrdinalIgnoreCase);

                string label = AvTheme.Truncate(record.Callsign + " · " + record.Name, 22);
                string detail;
                if (inSquadron)
                {
                    detail = "IN SQUADRON";
                }
                else
                {
                    WingRank rank = PersonnelFacade.Roster.RankFor(record.Xp);
                    detail = PersonnelFacade.Roster.RankName(rank) + " (" + record.Xp + " XP)";
                }

                popupEntries.Add(new AvKit.PopupEntry(label, detail, isInspected));
            }

            customPilotsPopup?.Show(
                new Rect(Pad, customPilotsRowY - RowHeight, PanelWidth - Pad * 2f, 0f),
                popupEntries,
                index =>
                {
                    if (index < 0 || index >= choices.Count) return;
                    CustomPilotRecord chosen = choices[index];

                    if (chosen == null)
                    {
                        PersonnelFacade.CustomPilots.ImportAll(out _, out string message);
                        WingCommandManager.Instance?.Toast(message);
                        List<WingPilot> selectable = PersonnelFacade.Roster.SelectablePilots();
                        if (selectable.Count > 0 && inspectPilot == null)
                        {
                            inspectPilot = selectable[0];
                        }
                    }
                    else
                    {
                        if (PersonnelFacade.Roster.ContainsCallsign(chosen.Callsign))
                        {
                            WingPilot existing = PersonnelFacade.Roster.FindByCallsign(chosen.Callsign);
                            if (existing != null) inspectPilot = existing;
                            WingCommandManager.Instance?.Toast("Viewing " + chosen.Callsign + " (already recruited)");
                        }
                        else
                        {
                            WingPilot recruited = PersonnelFacade.Roster.ImportCustom(chosen);
                            if (recruited != null)
                            {
                                inspectPilot = recruited;
                                WingCommandManager.Instance?.Toast("Recruited " + recruited.Callsign + " (" + recruited.Name + ")");
                            }
                        }
                    }

                    RefreshWingPage(WingCommandManager.Instance?.Wing);
                });
        }
    }
}
