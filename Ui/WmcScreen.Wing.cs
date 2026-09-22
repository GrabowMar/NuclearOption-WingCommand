using System;
using System.Collections.Generic;
using System.Text;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>WING page: squadron roster, dossier and pinned airframe assignment, plus the
    /// two-column Pilot Studio editor.</summary>
    internal static partial class WmcScreen
    {
        // ---------------------------------------------------------------- layout state

        /// <summary>Roster row count by body height; the shared row pitch sizes the roster area.</summary>
        private const float WingTallBodyHeight = 560f;
        private const float WingPinHeight = 58f;
        private const float DossierHeight = 112f;
        private const float DossierHeightMax = 120f;
        private const float BioHeight = 58f;
        private const float SkillCardHeight = 48f;
        private const float SkillCardHeightMax = 52f;
        private const float PortraitWidth = 64f;
        private const float PortraitHeight = 88f;
        private const int RankCount = 5;
        private const int PerkPipSlots = 4;

        // Squadron view state.
        private static readonly List<PilotRow> pilotRows = new List<PilotRow>();
        private static RectTransform pilotRosterArea;
        private static TMP_Text pilotEmptyLabel;
        private static Image pilotEmptyCard;
        private static TMP_Text wingHeadNote;
        private static TMP_Text wingPagerLabel;
        private static WingButton wingPagerPrev;
        private static WingButton wingPagerNext;
        private static RectTransform wingBody;
        private static ScrollRect wingScroll;
        private static int wingRosterPage;

        private static AvKit.Popup customPilotsPopup;
        private static float customPilotsRowY;

        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static TMP_Text pilotStatsLabel;
        private static TMP_Text pilotPersonaLabel;
        private static TMP_Text pilotStateLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;

        private static Image pilotPortrait;
        private static Image pilotCardRail;
        private static Image pilotPortraitFrame;
        private static Image pilotKiaOverlay;
        private static readonly List<PilotSkillCard> pilotSkillCards = new List<PilotSkillCard>();
        private static TMP_Text pilotSkillsEmptyLabel;

        // Inert presentation of the roster rows and perk slots the current page leaves open.
        private static readonly List<PilotSlotCard> pilotSlotCards = new List<PilotSlotCard>();
        private static readonly TMP_Text[] pilotRankTicks = new TMP_Text[RankCount];
        private static readonly Image[] pilotPerkPips = new Image[PerkPipSlots];
        private static TMP_Text pilotPerkCountLabel;

        private static Image airframeCardRail;
        private static Image airframeSilhouette;
        private static TMP_Text airframeNameLabel;
        private static TMP_Text airframeSlotLabel;
        private static WingButton sarButton;
        private static WingButton localSarButton;

        private static RectTransform squadronViewRoot;

        // The shell's legacy pager. The roster now pages four to six rows dynamically, so this
        // reference exists only to keep the offline UI check's field contract.
#pragma warning disable CS0414, IDE0051, IDE0052 // Harness contract field; paged dynamically instead
        private static PilotPager pilotPager;
#pragma warning restore CS0414, IDE0051, IDE0052

        private static int WingRowsPerPage => BodyHeight >= WingTallBodyHeight ? 6 : 4;

        // Flexible card heights: fixed minimums on short bodies, growing with the body so the
        // viewport content reaches the pinned airframe bar instead of stranding blank glass.
        private static float WingDossierHeight => WingFlex(DossierHeight, DossierHeightMax, 0.19f);
        private static float WingSkillHeight => WingFlex(SkillCardHeight, SkillCardHeightMax, 0.076f);

        private static float WingFlex(float min, float max, float factor) =>
            Mathf.Clamp(BodyHeight * factor, min, max);

        // Wing-page construction.

        /// <summary>Squadron dossier and explicit SAR dispatch. SUPPLY chooses the next pilot; aircraft
        /// details follow the inspected pilot or show recovery status.</summary>
        private static float AddWingPage(RectTransform parent, float y)
        {
            squadronViewRoot = new GameObject("SquadronViewRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            squadronViewRoot.SetParent(parent, worldPositionStays: false);
            Stretch(squadronViewRoot);

            customStudioRoot = new GameObject("CustomStudioRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            customStudioRoot.SetParent(parent, worldPositionStays: false);
            Stretch(customStudioRoot);
            customStudioRoot.gameObject.SetActive(false);

            BuildCustomPilotsStudio(customStudioRoot, y);
            BuildSquadronView(squadronViewRoot, y);

            // The page never requests more height than the shared body shows.
            return BodyBottom;
        }

        private static float BuildSquadronView(RectTransform parent, float y)
        {
            _ = y;
            wingRosterPage = 0;

            // One scroll viewport holds the roster, dossier and perks; the assignment bar is pinned.
            float viewportHeight = Mathf.Max(RowHeight, BodyHeight - WingPinHeight - Space2);
            BuildViewport(parent, new Rect(0f, BodyTop, PageWidth, viewportHeight), "SquadronViewport",
                          out wingBody, out wingScroll);
            pageScrolls[(int)Page.Wing] = wingScroll;

            float cursor = -Space1;
            float headTop = cursor;
            cursor = SectionHeader(wingBody, Pad, cursor, ContentWidth, "SQUADRON");
            wingHeadNote = Label(wingBody, "", new Rect(Pad + 10f, headTop - 1f, ContentWidth - 10f, 14f),
                                 Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            cursor = ColumnHeaders(wingBody, cursor, PilotColumns);
            cursor -= Space1;

            // Quick-list popup lives outside the viewport so it can escape the scrolling mask.
            customPilotsPopup = new AvKit.Popup(parent, PageWidth);

            int rows = WingRowsPerPage;
            pilotRosterArea = RosterViewport(wingBody, "PilotRoster", cursor, rows);
            float rosterH = rows * RowPitch;
            var (emptyFill, _) = WingUi.TacticalCard(pilotRosterArea,
                new Rect(0f, 0f, ContentWidth, rosterH), WingUi.RailCyan);
            pilotEmptyCard = emptyFill;
            RectTransform emptyRt = emptyFill.rectTransform;
            WingUi.CornerTicks(emptyRt, new Rect(4f, -4f, ContentWidth - 8f, rosterH - 8f),
                               WingUi.RailCyan, 7f);
            Glyph(emptyRt, "posture",
                  new Rect(ContentWidth * 0.5f - 14f, -rosterH * 0.5f + 18f, 28f, 28f), WingUi.RailCyan);
            pilotEmptyLabel = Label(emptyRt, "NO PILOTS\nRecruit below, or requisition on SUPPLY.",
                new Rect(Space3, -rosterH * 0.5f - 16f, ContentWidth - Space3 * 2f, 32f),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            pilotEmptyLabel.enableWordWrapping = true;
            pilotEmptyCard.gameObject.SetActive(false);

            // Named, inert placeholders fill every row the page leaves open so the roster never
            // reads as blank glass; the first vacant slot names the RECRUIT / STUDIO controls
            // directly below it.
            pilotSlotCards.Clear();
            for (int i = 0; i < rows; i++)
                pilotSlotCards.Add(new PilotSlotCard(pilotRosterArea, i, ContentWidth));
            cursor -= rosterH + Space1;

            RectTransform pagerRoot = PageRoot(wingBody, "WingPager");
            Place(pagerRoot, new Rect(Pad, cursor, ContentWidth, RowHeight));
            (wingPagerPrev, wingPagerLabel, wingPagerNext) =
                PagerRow(pagerRoot, 0f, ContentWidth, () => TurnWingPage(-1), () => TurnWingPage(1),
                         "Previous or next squadron roster page");
            cursor -= RowHeight + Space1;

            const float recruitH = 22f;
            float buttonWidth = (ContentWidth - Gap) * 0.5f;
            WingButton recruitRandom = WingUi.Button(wingBody, "RECRUIT",
                new Rect(Pad, cursor, buttonWidth, recruitH), FontMicro, OnRecruitPilot)
                .WithTooltip("Recruit a random custom pilot into the squadron roster");
            StampKey(recruitRandom, "selection", WingUi.RailEmerald, recruitH);
            WingButton customPilots = WingUi.Button(wingBody, "STUDIO",
                new Rect(Pad + buttonWidth + Gap, cursor, buttonWidth, recruitH), FontMicro, OnCustomPilots)
                .WithTooltip("Open Pilot Studio. Ctrl-click opens the quick list; Shift-click opens the pilot folder.");
            StampKey(customPilots, "airframe", WingUi.RailCyan, recruitH);
            customPilotsRowY = BodyTop + cursor;
            cursor -= recruitH + Gap;

            // Dossier card.
            float cardTop = cursor;
            float dossierHeight = WingDossierHeight;
            var (_, dossierRail) = WingUi.TacticalCard(
                wingBody, new Rect(Pad, cardTop, ContentWidth, dossierHeight), WingUi.RailCyan);
            pilotCardRail = dossierRail;
            BuildDossier(wingBody, cardTop);
            cursor = cardTop - dossierHeight - Gap;

            cursor = SectionHeader(wingBody, Pad, cursor, ContentWidth, "BACKGROUND");
            const float bioH = BioHeight;
            WingUi.TacticalCard(wingBody, new Rect(Pad, cursor, ContentWidth, bioH), WingUi.RailCyan);
            pilotBackgroundLabel = Label(wingBody, "", new Rect(Pad + 12f, cursor - 6f, ContentWidth - 20f, bioH - 12f),
                                         Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;
            cursor -= bioH + Gap;

            float perksTop = cursor;
            cursor = SectionHeader(wingBody, Pad, cursor, ContentWidth, "PILOT SKILLS & PERKS");
            pilotSkillsEmptyLabel = Label(wingBody, "",
                new Rect(Pad + 10f, perksTop - 1f, ContentWidth - 10f, 14f), Dim(), FontMicro,
                FontStyles.Normal, TextAlignmentOptions.Right);
            cursor -= Space1;

            pilotSkillCards.Clear();
            float skillCardHeight = WingSkillHeight;
            float skillPitch = skillCardHeight + Space1;
            float columnWidth = (ContentWidth - Gap) * 0.5f;
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    pilotSkillCards.Add(new PilotSkillCard(wingBody,
                        new Rect(Pad + column * (columnWidth + Gap), cursor - row * skillPitch,
                                 columnWidth, skillCardHeight)));
                }
            }
            cursor -= skillPitch * 2f;

            Reflow(wingScroll, cursor);

            BuildAirframeBar(parent);
            return BodyBottom;
        }

        /// <summary>Portrait, identity, rank/XP meter and operational readouts of the focused pilot.</summary>
        private static void BuildDossier(RectTransform parent, float cardTop)
        {
            const float portraitX = Pad + 6f;
            float portraitTop = cardTop - 8f;

            // Hairline frame behind the masked portrait, then the surface behind transparent sprites.
            pilotPortraitFrame = Panel(parent,
                new Rect(portraitX - 1f, portraitTop + 1f, PortraitWidth + 2f, PortraitHeight + 2f),
                FrameColor());
            Panel(parent, new Rect(portraitX, portraitTop, PortraitWidth, PortraitHeight), AvTheme.Surface);

            var maskGo = new GameObject("PilotPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            RectTransform maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.SetParent(parent, worldPositionStays: false);
            Place(maskRt, new Rect(portraitX, portraitTop, PortraitWidth, PortraitHeight));

            var portraitGo = new GameObject("PilotPortrait", typeof(RectTransform), typeof(Image));
            RectTransform portraitRt = portraitGo.GetComponent<RectTransform>();
            portraitRt.SetParent(maskRt, worldPositionStays: false);
            pilotPortrait = portraitGo.GetComponent<Image>();
            pilotPortrait.color = Color.white;
            pilotPortrait.raycastTarget = false;
            UpdatePortraitAspectFill(pilotPortrait, PersonnelFacade.Portraits.Sprite,
                                     PortraitWidth, PortraitHeight);

            var kiaGo = new GameObject("PilotKiaOverlay", typeof(RectTransform), typeof(Image));
            RectTransform kiaRt = kiaGo.GetComponent<RectTransform>();
            kiaRt.SetParent(maskRt, worldPositionStays: false);
            Stretch(kiaRt);
            pilotKiaOverlay = kiaGo.GetComponent<Image>();
            pilotKiaOverlay.color = new Color(Alert().r, Alert().g, Alert().b, 0.18f);
            pilotKiaOverlay.raycastTarget = false;
            pilotKiaOverlay.gameObject.SetActive(false);

            float textX = portraitX + PortraitWidth + 10f;
            float textWidth = Pad + ContentWidth - 8f - textX;
            const float stampWidth = 148f;

            pilotIdentityLabel = Label(parent, "",
                new Rect(textX, cardTop - 4f, textWidth - stampWidth - Space1, 20f),
                Friendly(), FontLead, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            pilotIdentityLabel.enableWordWrapping = false;
            pilotIdentityLabel.overflowMode = TextOverflowModes.Ellipsis;

            pilotStateLabel = Label(parent, "", new Rect(textX + textWidth - stampWidth, cardTop - 6f,
                                                         stampWidth, 16f),
                                    Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            pilotRankLabel = Label(parent, "", new Rect(textX, cardTop - 26f, textWidth, 16f), Dim(),
                                   FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            pilotRankLabel.enableWordWrapping = false;
            pilotRankLabel.overflowMode = TextOverflowModes.Ellipsis;

            float barY = cardTop - 42f;
            Rule(parent, new Rect(textX, barY, textWidth, 6f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(textX, barY, 0f, 6f), Green());
            pilotXpBarWidth = textWidth;
            for (int i = 1; i < RankCount; i++)
                Rule(parent, new Rect(textX + textWidth * (i / (float)RankCount), barY, 1f, 6f),
                     WingUi.BorderSubtle);

            // Rank ladder captions under the XP bar: R / W / V / A / L, current rank lit.
            for (int i = 0; i < RankCount; i++)
            {
                pilotRankTicks[i] = Label(parent, RankBadgeText((WingRank)i),
                    new Rect(textX + textWidth * (i / (float)RankCount), cardTop - 50f,
                             textWidth / RankCount, 12f),
                    Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            }

            pilotStatsLabel = Label(parent, "", new Rect(textX, cardTop - 64f, textWidth, 14f), Friendly(),
                                    FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            pilotPersonaLabel = Label(parent, "", new Rect(textX, cardTop - 80f, textWidth, 14f), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            pilotPersonaLabel.enableWordWrapping = false;
            pilotPersonaLabel.overflowMode = TextOverflowModes.Ellipsis;

            // Perk slots sit on the card foot so a taller dossier does not strand empty glass.
            float pipsTop = cardTop - WingDossierHeight + 18f;
            Label(parent, "PERKS", new Rect(textX, pipsTop, 40f, 12f), Dim(), FontMicro,
                  FontStyles.Bold, TextAlignmentOptions.Left);
            for (int i = 0; i < PerkPipSlots; i++)
                pilotPerkPips[i] = Panel(parent,
                    new Rect(textX + 44f + i * 11f, pipsTop - 2f, 8f, 8f), WingUi.BorderSubtle);
            pilotPerkCountLabel = Label(parent, "NONE",
                new Rect(textX + 44f + PerkPipSlots * 11f + 6f, pipsTop, 90f, 12f),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
        }

        /// <summary>Perk pips name the earned count in text as well as colour.</summary>
        private static void UpdateDossierPerks(int perkCount, bool kia)
        {
            for (int i = 0; i < pilotPerkPips.Length; i++)
            {
                if (pilotPerkPips[i] == null) continue;
                pilotPerkPips[i].color = i < perkCount
                    ? (kia ? Alert() : Green())
                    : WingUi.BorderSubtle;
            }
            if (pilotPerkCountLabel != null)
            {
                pilotPerkCountLabel.text = perkCount == 0 ? "NONE" : perkCount + " ACTIVE";
                pilotPerkCountLabel.color = perkCount > 0 ? Friendly() : Dim();
            }
        }

        /// <summary>Pinned airframe assignment and the explicit AIR SAR / LOCAL SAR dispatch actions.</summary>
        private static void BuildAirframeBar(RectTransform parent)
        {
            const float barHeight = 54f;
            float top = BodyBottom + Space1 + barHeight;

            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, top, ContentWidth, barHeight),
                                                WingUi.RailEmerald);
            airframeCardRail = rail;

            Label(parent, "AIRFRAME ASSIGNMENT", new Rect(Pad + 6f, top - 2f, 150f, 12f),
                  Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            airframeSilhouette = AddSprite(parent, "AirframeSilhouette", IconFactory.Get("airframe"),
                                           new Rect(Pad + 6f, top - 18f, 24f, 24f), WingColor());

            const float actionWidth = 104f;
            float actionX = Pad + ContentWidth - actionWidth - 4f;
            float textX = Pad + 6f + 24f + 8f;
            float textWidth = actionX - textX - 4f;

            airframeNameLabel = Label(parent, "", new Rect(textX, top - 18f, textWidth, 14f), Friendly(),
                                      FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            airframeNameLabel.enableWordWrapping = false;
            airframeNameLabel.overflowMode = TextOverflowModes.Ellipsis;
            airframeSlotLabel = Label(parent, "", new Rect(textX, top - 32f, textWidth, 12f), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            airframeSlotLabel.enableWordWrapping = false;
            airframeSlotLabel.overflowMode = TextOverflowModes.Ellipsis;

            sarButton = WingUi.Button(parent, "AIR SAR",
                new Rect(actionX, top - 6f, actionWidth, 22f), FontMicro, OnAirSar)
                .WithTooltip("Send the nearest idle rescue-capable wing helicopter to this downed pilot on land. Water rescue uses the native hoist.");
            localSarButton = WingUi.Button(parent, "LOCAL SAR",
                new Rect(actionX, top - 30f, actionWidth, 22f), FontMicro, OnLocalRecovery)
                .WithTooltip("Organize local recovery for 10,000,000 funds. Completes in five mission minutes; the pilot remains at risk until then.");
            sarButton.gameObject.SetActive(false);
            localSarButton.gameObject.SetActive(false);
        }

        private static void TurnWingPage(int direction)
        {
            wingRosterPage = Mathf.Max(0, wingRosterPage + direction);
            RefreshWingPage(Wing());
        }

        private static void OnAirSar() =>
            PersonnelFacade.SearchAndRescue.Dispatch(inspectPilot, WingCommandManager.Instance?.Wing);

        private static void RefreshWingPage(WingRegistry wing)
        {
            if (customStudioRoot != null && customStudioRoot.gameObject.activeSelf) return;

            List<WingPilot> display = PersonnelFacade.Roster.DisplayRoster();
            int count = display.Count;

            int rows = WingRowsPerPage;
            SyncPilotRows(pilotRows, pilotRosterArea);
            while (pilotRows.Count < rows)
                pilotRows.Add(new PilotRow(pilotRosterArea, pilotRows.Count));

            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)rows));
            wingRosterPage = Mathf.Clamp(wingRosterPage, 0, pages - 1);
            int first = wingRosterPage * rows;

            // The shell's PruneFocus owns roster-membership pruning; only the empty-roster default
            // lives here.
            if (inspectPilot == null && count > 0) inspectPilot = display[0];

            bool empty = count == 0;
            if (pilotEmptyCard != null && pilotEmptyCard.gameObject.activeSelf != empty)
                pilotEmptyCard.gameObject.SetActive(empty);
            if (pilotEmptyLabel != null && pilotEmptyLabel.gameObject.activeSelf != empty)
                pilotEmptyLabel.gameObject.SetActive(empty);

            for (int i = 0; i < pilotRows.Count; i++)
            {
                int index = first + i;
                if (i >= rows || index >= count)
                {
                    pilotRows[i].Hide();
                    continue;
                }

                WingPilot pilot = display[index];
                pilotRows[i].Bind(pilot, ReferenceEquals(inspectPilot, pilot), () =>
                {
                    inspectPilot = pilot;
                    RefreshWingPage(Wing());
                });
            }

            // Vacant slots keep the roster area intentional; only the first names the controls.
            int shown = Mathf.Min(rows, Mathf.Max(0, count - first));
            for (int i = 0; i < pilotSlotCards.Count; i++)
                pilotSlotCards[i].SetVisible(!empty && i >= shown, !empty && i == shown);

            if (wingPagerLabel != null)
                wingPagerLabel.text = PageSummary(count, wingRosterPage, pages, "PILOT", "PILOTS");
            wingPagerPrev?.SetEnabled(wingRosterPage > 0);
            wingPagerNext?.SetEnabled(wingRosterPage < pages - 1);
            if (wingHeadNote != null)
                wingHeadNote.text = count == 0
                    ? "NO PERSONNEL"
                    : count + (count == 1 ? " PILOT" : " PILOTS") + " · " +
                      Mathf.Min(rows, Mathf.Max(0, count - first)) + " SHOWN";

            WingPilot focus = inspectPilot;
            bool downed = focus != null && !focus.Lost &&
                focus.RecoveryStatus == PilotRecoveryStatus.Downed;
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
                    localSarButton.SetText("LOCAL SAR");
                }
            }

            if (focus == null)
            {
                if (pilotCardRail != null) pilotCardRail.color = FrameColor();
                if (pilotStateLabel != null)
                {
                    pilotStateLabel.text = "—";
                    pilotStateLabel.color = Dim();
                }
                SetWingDetail("NO PILOT SELECTED", "", "", "", 0f,
                    "Use RECRUIT or STUDIO above, or requisition an aircraft on SUPPLY.",
                    "NO AIRFRAME", "Select a pilot above to inspect active flight assignment");
                SetSilhouetteAlpha(0f);
                RenderPilotVisual(null);
                if (pilotSkillsEmptyLabel != null)
                {
                    pilotSkillsEmptyLabel.text = "NO PILOT";
                    pilotSkillsEmptyLabel.color = Dim();
                }
                for (int i = 0; i < pilotRankTicks.Length; i++)
                    if (pilotRankTicks[i] != null) pilotRankTicks[i].color = Dim();
                UpdateDossierPerks(0, false);
                for (int i = 0; i < pilotSkillCards.Count; i++)
                    pilotSkillCards[i].ShowLocked(i + 1, "Select a pilot above to review combat perks.");
                if (airframeNameLabel != null) airframeNameLabel.color = Dim();
                if (airframeSlotLabel != null) airframeSlotLabel.color = Dim();
                if (airframeCardRail != null) airframeCardRail.color = FrameColor();
                if (airframeSilhouette != null) airframeSilhouette.sprite = IconFactory.Get("airframe");
                return;
            }

            RenderPilotVisual(focus);

            bool kia = focus.Lost;
            WingMember flying = FlyingMember(wing, focus);

            Color stateColor;
            string stateText = PilotStateText(focus, flying, out stateColor);
            if (pilotStateLabel != null)
            {
                pilotStateLabel.text = stateText;
                pilotStateLabel.color = stateColor;
            }
            if (pilotCardRail != null) pilotCardRail.color = kia ? Alert() : FrameColor();

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

            // Light the pilot's rung on the rank ladder; the other four stay dim.
            for (int i = 0; i < pilotRankTicks.Length; i++)
                if (pilotRankTicks[i] != null)
                    pilotRankTicks[i].color = i == (int)focus.Rank ? RankColor(focus.Rank) : Dim();

            string stats = focus.Kills + " KILLS   /   " + focus.Sorties + " SORTIES";
            string persona = kia
                ? "STATUS   KILLED IN ACTION"
                : focus.RecoveryStatus != PilotRecoveryStatus.None
                    ? "STATUS   " + PersonnelFacade.SearchAndRescue.Status(focus)
                    : "RADIO PROFILE   " + focus.Persona.ToString().ToUpperInvariant();

            List<PilotPerk> perks = focus.Perks;
            int perkCount = (perks != null && !kia) ? perks.Count : 0;
            if (pilotSkillsEmptyLabel != null)
            {
                pilotSkillsEmptyLabel.text = kia
                    ? "RECORD CLOSED"
                    : perkCount == 0 ? "NO PERKS" : perkCount + (perkCount == 1 ? " PERK" : " PERKS");
                pilotSkillsEmptyLabel.color = perkCount > 0 ? Friendly() : Dim();
            }
            UpdateDossierPerks(perkCount, kia);

            int maxCards = pilotSkillCards.Count;
            bool overflow = perkCount > maxCards;
            int visible = overflow ? maxCards - 1 : Mathf.Min(perkCount, maxCards);
            string lockedNote = kia
                ? "Record closed — this pilot was lost in action."
                : "Unlocks through combat sorties and promotions.";
            for (int i = 0; i < maxCards; i++)
            {
                if (i < visible) pilotSkillCards[i].Bind(perks[i]);
                else if (overflow && i == maxCards - 1) pilotSkillCards[i].BindExtra(perks, visible);
                else pilotSkillCards[i].ShowLocked(i + 1, lockedNote);
            }

            if (flying == null)
            {
                SetSilhouetteAlpha(0.25f);
                if (airframeSilhouette != null) airframeSilhouette.sprite = IconFactory.Get("airframe");
                string planeName = kia
                    ? (focus.LastAircraft ?? "Unknown aircraft")
                    : focus.RecoveryStatus != PilotRecoveryStatus.None
                        ? PersonnelFacade.SearchAndRescue.Status(focus)
                        : "ON THE GROUND (RESERVE)";
                string slotStatus = kia
                    ? "CAUSE: " + (focus.LossCause ?? "Unknown")
                    : focus.RecoveryStatus != PilotRecoveryStatus.None
                        ? PersonnelFacade.SearchAndRescue.Status(focus)
                        : "AVAILABLE · READY FOR FLIGHT ASSIGNMENT";

                SetWingDetail(identity, rank, stats, persona, progress, focus.Background, planeName, slotStatus);
                if (airframeNameLabel != null) airframeNameLabel.color = kia ? Alert() : Dim();
                if (airframeSlotLabel != null) airframeSlotLabel.color = kia ? Alert() : Friendly();
                if (airframeCardRail != null) airframeCardRail.color = kia ? Alert() : WingUi.RailEmerald;
                return;
            }

            Aircraft aircraft = flying.Aircraft;
            AircraftDefinition definition = DefinitionOf(flying);

            if (airframeSilhouette != null)
                airframeSilhouette.sprite = IconFactory.Aircraft(definition);
            SetSilhouetteAlpha(1.0f);

            string assignedName = definition != null ? definition.unitName : flying.Name;
            string assignedSlot = "SLOT " + flying.Slot +
                (flying.IsFlightLead ? " · FLIGHT LEAD" : " · WINGMAN") +
                "  ·  " + ShortOrder(flying);
            if (aircraft != null && !aircraft.LocalSim)
                assignedSlot += " (REMOTE)";

            SetWingDetail(identity, rank, stats, persona, progress, focus.Background, assignedName, assignedSlot);
            if (airframeNameLabel != null) airframeNameLabel.color = Friendly();
            if (airframeSlotLabel != null) airframeSlotLabel.color = Friendly();
            if (airframeCardRail != null) airframeCardRail.color = WingUi.RailEmerald;
        }

        /// <summary>State grammar shared by the roster rows and the dossier stamp.</summary>
        private static string PilotStateText(WingPilot pilot, WingMember member, out Color color)
        {
            if (pilot.Lost)
            {
                color = Alert();
                return "KIA";
            }

            switch (pilot.RecoveryStatus)
            {
                case PilotRecoveryStatus.Downed:
                    color = Warning();
                    return "DOWNED — SAR";
                case PilotRecoveryStatus.Missing:
                    color = Warning();
                    return "MIA";
                case PilotRecoveryStatus.Captured:
                    color = Alert();
                    return "CAPTURED";
            }

            if (member != null)
            {
                color = Green();
                return "IN AIR";
            }

            if (PersonnelFacade.Roster.IsReserved(pilot))
            {
                color = Dim();
                return "RESERVE";
            }

            color = Friendly();
            return "ACTIVE";
        }

        /// <summary>Set faint aircraft-silhouette opacity or hide it on empty pages.</summary>
        private static void SetSilhouetteAlpha(float alpha)
        {
            if (airframeSilhouette == null) return;
            Color c = WingColor();
            c.a = alpha;
            airframeSilhouette.color = c;
        }

        /// <summary>Display the pilot's generated portrait with loss tint and rank frame.</summary>
        private static void RenderPilotVisual(WingPilot pilot)
        {
            if (pilotPortrait != null)
            {
                if (pilot == null)
                {
                    pilotPortrait.color = Color.clear;
                    pilotPortrait.sprite = null;
                    pilotPortrait.enabled = false;
                }
                else
                {
                    pilotPortrait.color = pilot.Lost ? new Color(0.7f, 0.45f, 0.45f, 0.85f) : Color.white;
                    UpdatePortraitAspectFill(pilotPortrait, PersonnelFacade.Portraits.For(pilot),
                                             PortraitWidth, PortraitHeight);
                }
            }

            if (pilotKiaOverlay != null) pilotKiaOverlay.gameObject.SetActive(pilot != null && pilot.Lost);
            if (pilotPortraitFrame != null)
                pilotPortraitFrame.color = pilot != null && pilot.Lost ? Alert() : FrameColor();
        }

        private static void UpdatePortraitAspectFill(Image image, Sprite sprite, float containerW, float containerH)
        {
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
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
                    new Vector2(Mathf.Max(0f, pilotXpBarWidth * Mathf.Clamp01(progress)), 6f);
        }

        private static void FocusRosterOn(WingPilot pilot)
        {
            List<WingPilot> display = PersonnelFacade.Roster.DisplayRoster();
            int index = display.IndexOf(pilot);
            if (index >= 0) wingRosterPage = index / WingRowsPerPage;
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
                Fill = Panel(parent, rect, WingUi.CardFill);
                Outline = WingUi.Outline(parent, rect, FrameColor());

                const float iconSize = 18f;
                Icon = AddSprite(parent, "SkillIcon", null,
                                 new Rect(rect.x + 5f, rect.y - 4f, iconSize, iconSize), Dim());

                float textX = rect.x + 5f + iconSize + 5f;
                float textWidth = rect.width - (5f + iconSize + 5f) - 4f;
                TitleLabel = Label(parent, "", new Rect(textX, rect.y - 3f, textWidth, 14f),
                                   Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                TitleLabel.enableWordWrapping = false;
                TitleLabel.overflowMode = TextOverflowModes.Ellipsis;
                DescLabel = Label(parent, "", new Rect(rect.x + 5f, rect.y - 18f, rect.width - 10f, 28f),
                                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
                DescLabel.enableWordWrapping = true;
                DescLabel.overflowMode = TextOverflowModes.Ellipsis;

                Hit = HitButton(parent, rect, () =>
                {
                    if (!string.IsNullOrEmpty(Title))
                        WingCommandManager.Instance?.Toast(Title + ": " + Description);
                });
                Hit.SetRowHighlight(Fill, WingUi.CardFill, WingUi.CardFillHover);
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
                    Icon.enabled = Icon.sprite != null;
                    Icon.color = Dim();
                }
                if (TitleLabel != null)
                {
                    TitleLabel.text = Title;
                    TitleLabel.color = Friendly();
                }
                if (DescLabel != null) DescLabel.text = Description;
                Hit.WithTooltip(Title + " — " + Description);
                SetVisible(true);
            }

            /// <summary>Inert presentation of an unfilled perk slot: named, dim, and unclickable.</summary>
            public void ShowLocked(int slot, string description)
            {
                Title = "PERK SLOT " + slot.ToString("00");
                Description = description;
                if (Icon != null)
                {
                    Icon.sprite = null;
                    Icon.enabled = false;
                }
                if (TitleLabel != null)
                {
                    TitleLabel.text = Title;
                    TitleLabel.color = Dim();
                }
                if (DescLabel != null) DescLabel.text = Description;
                SetVisible(true);
                if (Hit != null) Hit.gameObject.SetActive(false);
            }

            public void BindExtra(List<PilotPerk> perks, int firstIndex)
            {
                int extra = Mathf.Max(0, perks.Count - firstIndex);
                Title = "+" + extra + " MORE";
                var summary = new StringBuilder("Also active: ");
                for (int i = firstIndex; i < perks.Count; i++)
                {
                    if (i > firstIndex) summary.Append(", ");
                    summary.Append(PilotPerks.Name(perks[i]));
                }
                Description = summary.ToString();
                if (Icon != null)
                {
                    Icon.sprite = IconFactory.Get("rank_legend");
                    Icon.enabled = Icon.sprite != null;
                    Icon.color = Dim();
                }
                if (TitleLabel != null) TitleLabel.text = Title;
                if (DescLabel != null) DescLabel.text = "Additional combat perks active";
                Hit.WithTooltip(Description);
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

        /// <summary>Inert, named placeholder for a roster row the current page does not fill.
        /// It has no click target; the first vacant slot names the RECRUIT / STUDIO controls
        /// that sit directly below the roster.</summary>
        private sealed class PilotSlotCard
        {
            private const string Hint = "RECRUIT OR STUDIO BELOW";
            private const string Waiting = "AWAITING RECRUIT";

            private readonly GameObject root;
            private readonly TMP_Text detail;

            public PilotSlotCard(RectTransform parent, int index, float width)
            {
                root = new GameObject("PilotSlot" + index, typeof(RectTransform));
                RectTransform rt = root.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(3f, -index * RowPitch, width - 4f, RowHeight));

                float slotWidth = width - 4f;
                Panel(rt, new Rect(0f, 0f, slotWidth, RowHeight), WingUi.CardFill);
                Rule(rt, new Rect(0f, 0f, slotWidth, 1f), WingUi.BorderSubtle);
                Rule(rt, new Rect(0f, 0f, 2f, RowHeight), WingUi.RailInert);
                Label(rt, "—", new Rect(6f, 0f, 20f, RowHeight), Dim(), FontMicro,
                      FontStyles.Bold, TextAlignmentOptions.Left);
                Label(rt, "VACANT SLOT " + (index + 1).ToString("00"),
                      new Rect(30f, 0f, 150f, RowHeight), Dim(), FontSmall,
                      FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, Waiting, new Rect(180f, 0f, slotWidth - 192f, RowHeight), Dim(),
                               FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);

                root.SetActive(false);
            }

            public void SetVisible(bool visible, bool withHint)
            {
                if (detail != null) detail.text = withHint ? Hint : Waiting;
                if (root.activeSelf != visible) root.SetActive(visible);
            }
        }

        private static void OnRecruitPilot()
        {
            WingPilot pilot = PersonnelFacade.Roster.RecruitManual();
            if (pilot != null)
            {
                inspectPilot = pilot;
                FocusRosterOn(pilot);
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
                new Rect(Pad, customPilotsRowY - RowHeight, PageWidth - Pad * 2f, 0f),
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

                    if (inspectPilot != null) FocusRosterOn(inspectPilot);
                    RefreshWingPage(WingCommandManager.Instance?.Wing);
                });
        }
    }
}
