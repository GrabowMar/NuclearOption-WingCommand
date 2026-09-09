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
        private static readonly List<PilotSkillIcon> pilotSkillIcons = new List<PilotSkillIcon>();
        private static Image airframeCardRail;

        private static TMP_Text airframeTypeLabel;
        private static TMP_Text airframeStateLabel;
        private static TMP_Text airframeOrderLabel;
        private static TMP_Text airframeLoadoutLabel;
        private static TMP_Text airframeWeaponsLabel;
        private static Image airframeSilhouette;

        // Wing-page construction.

        /// <summary>Squadron dossier and explicit SAR dispatch. SUPPLY chooses the next pilot; aircraft
        /// details follow the inspected pilot or show recovery status.</summary>
        private static float AddWingPage(RectTransform parent, float y)
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
                .WithTooltip("Select custom pilots to recruit from files (Shift-click opens folder)");

            y -= RowHeight + Gap;

            y = Heading(parent, y, "PILOT DOSSIER");
            float w = PanelWidth - Pad * 2f;

            WingUi.TacticalCard(parent, new Rect(Pad, y, w, 154f), WingUi.RailCyan);
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
            UpdatePortraitAspectFill(pilotPortrait, PilotPortrait.Sprite, PortraitWidth, PortraitHeight);

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
            pilotCardRail = null;

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

            // Skill icons with descriptive hover help.
            const float skillSize = 20f;
            const float skillGap = 4f;
            pilotSkillIcons.Clear();
            int perkColumns = Mathf.Max(1, Mathf.FloorToInt((dossierW + skillGap) / (skillSize + skillGap)));
            string[] perkIcons = {
                "maneuver", "cover", "rejoin", "attack", "cargo", "rejoin", "cover", "cover",
                "land", "rejoin", "orbit", "move", "land", "jam", "maneuver", "maneuver",
                "jam", "maneuver", "jam", "tasking", "attack", "attack", "cargo", "move"
            };
            for (int i = 0; i < PilotPerks.Count; i++)
                pilotSkillIcons.Add(new PilotSkillIcon(parent,
                    new Rect(dossierX + (skillSize + skillGap) * (i % perkColumns),
                        detailY - (skillSize + skillGap) * (i / perkColumns), skillSize, skillSize),
                    perkIcons[i], PilotPerks.Name((PilotPerk)i), PilotPerks.Description((PilotPerk)i)));
            detailY -= ((PilotPerks.Count + perkColumns - 1) / perkColumns) * (skillSize + skillGap);

            // Biography within the dossier column.
            pilotBackgroundLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, 38f),
                                         Friendly(), FontMicro, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;

            y = Mathf.Min(y - PortraitHeight, detailY - 38f) - Space4;

            y = Heading(parent, y, "AIRFRAME");
            float airframeRailY = y;
            WingUi.TacticalCard(parent, new Rect(Pad, y, w, 170f), WingUi.RailEmerald);
            y -= Space2;

            float airframeTextX = Pad + 8f;
            float airframeTextW = w - 116f;

            Color ghost = WingColor();
            ghost.a = 0f;
            airframeSilhouette = AddSprite(parent, "AirframeSilhouette",
                      IconFactory.Get("airframe"),
                      new Rect(PanelWidth - Pad - 104f, y - 26f, 96f, 96f), ghost);

            airframeTypeLabel = Label(parent, "", new Rect(airframeTextX, y, airframeTextW, LineHeight), Friendly(),
                                      FontLead, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeStateLabel = Label(parent, "", new Rect(airframeTextX, y, airframeTextW, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeOrderLabel = Label(parent, "", new Rect(airframeTextX, y, airframeTextW, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeLoadoutLabel = Label(parent, "", new Rect(airframeTextX, y, airframeTextW, LineHeight), Dim(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeWeaponsLabel = Label(parent, "", new Rect(airframeTextX, y, airframeTextW, 64f), Friendly(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            airframeTypeLabel.enableAutoSizing = true;
            airframeTypeLabel.fontSizeMin = FontMicro;
            airframeStateLabel.enableAutoSizing = true;
            airframeStateLabel.fontSizeMin = FontMicro;
            airframeOrderLabel.enableAutoSizing = true;
            airframeOrderLabel.fontSizeMin = FontMicro;
            airframeWeaponsLabel.enableWordWrapping = true;
            airframeWeaponsLabel.overflowMode = TextOverflowModes.Ellipsis;

            float airframeBottom = airframeRailY - 170f;
            airframeCardRail = Rule(parent,
                new Rect(Pad, airframeRailY, 3f, airframeRailY - airframeBottom),
                FrameColor());
            sarButton = WingUi.Button(parent, "DISPATCH SAR",
                new Rect(Pad, airframeBottom - Gap, PanelWidth - Pad * 2f, RowHeight), FontSmall,
                () => WingSearchAndRescue.Dispatch(inspectPilot, WingCommandManager.Instance?.Wing))
                .WithTooltip("Send the nearest idle rescue-capable wing helicopter to this downed pilot on land. Water rescue uses the native hoist.");
            return airframeBottom - Gap - RowHeight;
        }

        private static void RefreshWingPage(WingRegistry wing)
        {
            List<WingPilot> display = WingPilotRoster.DisplayRoster();
            int count = display.Count;

            SyncPilotRows(pilotRows, pilotRosterArea);
            int first = pilotPager != null ? pilotPager.Refresh(count) : 0;

            if (inspectPilot != null && !WingPilotRoster.Contains(inspectPilot))
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
            sarButton?.SetEnabled(focus != null && !focus.Lost && focus.RecoveryStatus == PilotRecoveryStatus.Downed);
            if (focus == null)
            {
                SetWingDetail("NO PILOT", "", "", "", 0f,
                    "Pick a pilot from the squadron list above, or requisition aircraft " +
                    "on the SUPPLY tab.",
                    "NO AIRFRAME", "", "", "", "");
                SetSilhouetteAlpha(0f);
                RenderPilotVisual(null);
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
                rank = WingPilotRoster.RankName(focus.Rank) + "   LOST IN ACTION" +
                       (flying != null ? "   IN AIR" : "");
                progress = 0f;
            }
            else
            {
                WingRank crewRank = focus.Rank;
                if (crewRank >= WingPilotRoster.TopRank)
                {
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + focus.Xp + "   MAX RANK";
                    progress = 1f;
                }
                else
                {
                    int floor = WingPilotRoster.XpForRank(crewRank);
                    int ceiling = WingPilotRoster.XpForRank(crewRank + 1);
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + focus.Xp + " / " + ceiling;
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
                    ? "STATUS   " + WingSearchAndRescue.Status(focus)
                    : "RADIO PROFILE   " + focus.Persona.ToString().ToUpperInvariant();

            SetWingDetail(identity, rank, stats, persona, progress,
                          focus.Background, kia ? focus.LastAircraft ?? "Unknown aircraft" : "",
                          "", kia ? "KILLED BY   " + (focus.KilledBy ?? "Unknown") : "", "", "");

            if (pilotIdentityLabel != null) pilotIdentityLabel.color = kia ? Alert() : Green();

            if (flying == null)
            {
                SetSilhouetteAlpha(0f);
                if (airframeSilhouette != null) airframeSilhouette.sprite = IconFactory.Get("airframe");
                if (airframeTypeLabel != null)
                {
                    airframeTypeLabel.text = kia ? "AIRCRAFT   " + (focus.LastAircraft ?? "Unknown aircraft") : "NO AIRFRAME";
                    airframeTypeLabel.color = kia ? Alert() : Dim();
                }
                if (airframeStateLabel != null)
                {
                    airframeStateLabel.text = kia
                        ? "CAUSE   " + (focus.LossCause ?? "Unknown")
                        : focus.RecoveryStatus != PilotRecoveryStatus.None
                            ? WingSearchAndRescue.Status(focus)
                            : "ON THE GROUND  ·  AWAITING AN AIRFRAME";
                    airframeStateLabel.color = kia ? Alert() : Friendly();
                }
                return;
            }

            Aircraft aircraft = flying.Aircraft;
            AircraftDefinition definition = DefinitionOf(flying);

            if (airframeSilhouette != null)
            {
                Sprite planeSprite = IconFactory.Aircraft(definition);
                airframeSilhouette.sprite = planeSprite;
            }

            SetSilhouetteAlpha(0.45f);

            string type = definition != null
                ? AvTheme.Truncate(definition.unitName, 22) + "   SLOT " + flying.Slot
                : "AIRFRAME   SLOT " + flying.Slot;

            float fuel = flying.Fuel;
            int ammo = flying.Ammo;
            float integrity = flying.Integrity;
            string state =
                "FUEL " + Mathf.RoundToInt(fuel * 100f) + "%" +
                "   AMMO " + ammo +
                "   HULL " + Mathf.RoundToInt(integrity * 100f) + "%" +
                (flying.CanDeliverCargo ? "   CARGO " + flying.CargoAmmo : "");

            string order =
                "ORDER " + WingOrderCatalog.ShortLabel(flying.Order) +
                "   WEAPONS " + WingWeaponPreferences.Label(flying.WeaponPreference) +
                (flying.DeliveryPending ? "   (DEPARTING)" : "") +
                (flying.IsPanicking ? "   (DEFENSIVE)" : "");

            string loadout = flying.LoadoutKnown
                ? "LOADOUT " + WingLoadoutCatalog.Label(flying.Loadout) +
                  " - fitted at requisition"
                : "LOADOUT as found - assigned mission aircraft keep their own fit";

            SetWingDetail(identity, rank, stats, persona, progress,
                          focus.Background, type, state, order, loadout,
                          WeaponManifest(aircraft));

            if (airframeStateLabel != null)
            {
                bool poor = fuel <= (Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel) ||
                            ammo <= 0 || integrity < 0.75f;
                airframeStateLabel.color = poor ? Warning() : Friendly();
                if (airframeCardRail != null)
                    airframeCardRail.color = poor ? Warning() : MemberFrameColor();
            }

            if (airframeTypeLabel != null && aircraft != null && !aircraft.LocalSim)
                airframeTypeLabel.text = type + "   (NOT LOCALLY SIMULATED)";
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
                UpdatePortraitAspectFill(pilotPortrait, PilotPortrait.For(pilot), PortraitWidth, PortraitHeight);
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

            if (pilotSkillIcons.Count > 0)
            {
                for (int i = 0; i < pilotSkillIcons.Count; i++)
                {
                    pilotSkillIcons[i].SetActive(pilot != null && pilot.Perks.Contains((PilotPerk)i));
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
                                          string type, string state, string order, string loadout,
                                          string weapons)
        {
            if (pilotIdentityLabel != null) pilotIdentityLabel.text = identity;
            if (pilotRankLabel != null) pilotRankLabel.text = rank;
            if (pilotStatsLabel != null) pilotStatsLabel.text = stats;
            if (pilotPersonaLabel != null) pilotPersonaLabel.text = persona;
            if (pilotBackgroundLabel != null) pilotBackgroundLabel.text = background;
            if (airframeTypeLabel != null)
            {
                airframeTypeLabel.text = type;
                airframeTypeLabel.color = Friendly();
            }
            if (airframeStateLabel != null) airframeStateLabel.text = state;
            if (airframeOrderLabel != null) airframeOrderLabel.text = order;
            if (airframeLoadoutLabel != null) airframeLoadoutLabel.text = loadout;
            if (airframeWeaponsLabel != null) airframeWeaponsLabel.text = weapons;

            if (pilotXpBar != null)
                pilotXpBar.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, pilotXpBarWidth * Mathf.Clamp01(progress)), 3f);
        }

        /// <summary>Summarise live stores grouped by weapon definition.</summary>
        private static string WeaponManifest(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return "WEAPONS   —";

            var names = new List<string>();
            var ammo = new List<int>();
            var stations = new List<int>();

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo) continue;

                string name = station.WeaponInfo != null ? station.WeaponInfo.name : "STORE";
                if (string.IsNullOrEmpty(name)) name = "STORE";
                name = name.Replace("(Clone)", "").Replace("_", " ").Trim().ToUpperInvariant();

                int index = names.IndexOf(name);
                if (index < 0)
                {
                    names.Add(name);
                    ammo.Add(Mathf.Max(0, station.Ammo));
                    stations.Add(1);
                }
                else
                {
                    ammo[index] += Mathf.Max(0, station.Ammo);
                    stations[index]++;
                }
            }

            if (names.Count == 0) return "WEAPONS   UNARMED";

            string result = "WEAPONS   ";
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) result += "   ·   ";
                string count = stations[i] > 1 ? stations[i] + "x " : "";
                result += count + AvTheme.Truncate(names[i], 20) + "  [" + ammo[i] + "]";
            }
            return result;
        }

        private sealed class PilotSkillIcon
        {
            public readonly Image Fill;
            public readonly Image[] Outline;
            public readonly Image Icon;
            public readonly WingButton Hit;
            public readonly string Title;
            public readonly string Description;

            public PilotSkillIcon(RectTransform parent, Rect rect, string key, string title, string description)
            {
                Title = title;
                Description = description;

                Fill = Panel(parent, rect, WingUi.CardFill);
                Outline = WingUi.Outline(parent, rect, FrameColor());
                Icon = AddSprite(parent, "Skill_" + key, IconFactory.Get(key),
                                 new Rect(rect.x + 2f, rect.y - 2f, rect.width - 4f, rect.height - 4f),
                                 Dim());
                Hit = HitButton(parent, rect, () => WingCommandManager.Instance?.Toast(title + ": " + description));
                Hit.WithTooltip(title + " — " + description);
            }

            public void SetActive(bool active)
            {
                Hit.WithTooltip((active ? "EARNED: " : "NOT EARNED: ") + Title + " — " + Description);
                Icon.color = active ? Green() : new Color(0.35f, 0.5f, 0.45f, 0.35f);
                Color frame = active ? Green() : FrameColor();
                if (Outline != null)
                {
                    for (int i = 0; i < Outline.Length; i++)
                    {
                        if (Outline[i] != null) Outline[i].color = frame;
                    }
                }
            }
        }

        private static void OnRecruitPilot()
        {
            WingPilot pilot = WingPilotRoster.RecruitManual();
            if (pilot != null)
            {
                inspectPilot = pilot;
                WingCommandManager.Instance?.Toast("Recruited " + pilot.Callsign + " (" + pilot.Name + ")");
                RefreshWingPage(WingCommandManager.Instance?.Wing);
            }
        }

        private static void OnCustomPilots()
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                WingCustomPilots.OpenFolder();
                return;
            }

            OpenCustomPilotsDropdown();
        }

        private static void OpenCustomPilotsDropdown()
        {
            List<CustomPilotRecord> pilots = WingCustomPilots.LoadAllCustomPilots(out _);
            if (pilots.Count == 0)
            {
                WingCommandManager.Instance?.Toast("No custom pilots found in folder. Sample file created.");
                return;
            }

            int unrecruitedCount = 0;
            for (int i = 0; i < pilots.Count; i++)
            {
                if (!WingPilotRoster.ContainsCallsign(pilots[i].Callsign))
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

                bool inSquadron = WingPilotRoster.ContainsCallsign(record.Callsign);
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
                    WingRank rank = WingPilotRoster.RankFor(record.Xp);
                    detail = WingPilotRoster.RankName(rank) + " (" + record.Xp + " XP)";
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
                        WingCustomPilots.ImportAll(out _, out string message);
                        WingCommandManager.Instance?.Toast(message);
                        List<WingPilot> selectable = WingPilotRoster.SelectablePilots();
                        if (selectable.Count > 0 && inspectPilot == null)
                        {
                            inspectPilot = selectable[0];
                        }
                    }
                    else
                    {
                        if (WingPilotRoster.ContainsCallsign(chosen.Callsign))
                        {
                            WingPilot existing = WingPilotRoster.FindByCallsign(chosen.Callsign);
                            if (existing != null) inspectPilot = existing;
                            WingCommandManager.Instance?.Toast("Viewing " + chosen.Callsign + " (already recruited)");
                        }
                        else
                        {
                            WingPilot recruited = WingPilotRoster.ImportCustom(chosen);
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
