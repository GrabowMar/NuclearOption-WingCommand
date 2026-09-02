using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace WingCommand
{
    /// <summary>The WMC panel's WING tab: one pilot's record and the aircraft they are flying.</summary>
    internal static partial class WmcScreen
    {
        // --- Wing page ---
        private static readonly List<PilotRow> pilotRows = new List<PilotRow>();
        private static RectTransform pilotRosterArea;
        private static TMP_Text pilotEmptyLabel;
        private static PilotPager pilotPager;

        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static TMP_Text pilotStatsLabel;
        private static TMP_Text pilotPersonaLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;

        private static Image pilotPortrait;
        private static Image pilotCardRail;
        private static Image pilotKiaOverlay;
        private static readonly List<PilotSkillIcon> pilotSkillIcons = new List<PilotSkillIcon>();
        private static Image airframeCardRail;

        private static TMP_Text airframeTypeLabel;
        private static TMP_Text airframeStateLabel;
        private static TMP_Text airframeOrderLabel;
        private static TMP_Text airframeLoadoutLabel;
        private static TMP_Text airframeWeaponsLabel;
        private static Image airframeSilhouette;

        // -------------------------------------------------------------------- wing page

        /// <summary>
        /// The squadron roster and one person's record.
        ///
        /// Read-only by design. Everything that can be changed about a pilot already has a
        /// control somewhere else — the SUPPLY tab picks who flies next — and duplicating
        /// those here would give the player two places to look for the same switch. The
        /// airframe half of the dossier is whatever the inspected pilot is flying, or an
        /// explicit "on the ground" note when they are not.
        /// </summary>
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

            y = Heading(parent, y, "PILOT");
            float w = PanelWidth - Pad * 2f;

            const float portraitW = 92f;
            const float portraitH = 138f;
            const float portraitGap = Space3;

            // --- Left Column: Tall Portrait Photo Card ---
            Panel(parent, new Rect(Pad, y, portraitW, portraitH), WingUi.CardFill);
            Outline(parent, new Rect(Pad, y, portraitW, portraitH), FrameColor());
            pilotPortrait = AddSprite(parent, "PilotPortrait", PilotPortrait.Sprite,
                       new Rect(Pad + 3f, y - 3f, portraitW - 6f, portraitH - 6f), Color.white);

            pilotCardRail = Rule(parent, new Rect(Pad, y, 3f, portraitH), RankColor(WingRank.Rookie));

            // A subtle red wash over the portrait for a lost pilot (no face-covering badge)
            var kiaOverlayGo = new GameObject("PilotKiaOverlay", typeof(RectTransform), typeof(Image));
            RectTransform kiaRt = kiaOverlayGo.GetComponent<RectTransform>();
            kiaRt.SetParent(parent, worldPositionStays: false);
            Place(kiaRt, new Rect(Pad, y, portraitW, portraitH));
            pilotKiaOverlay = kiaOverlayGo.GetComponent<Image>();
            pilotKiaOverlay.color = new Color(Alert().r, Alert().g, Alert().b, 0.18f);
            pilotKiaOverlay.raycastTarget = false;
            pilotKiaOverlay.gameObject.SetActive(false);

            // --- Right Column: Pilot Data, Skills & Bio Grid ---
            float dossierX = Pad + portraitW + portraitGap;
            float dossierW = w - portraitW - portraitGap;

            pilotIdentityLabel = Label(parent, "", new Rect(dossierX, y, dossierW, Space5), Green(), FontLead,
                                       FontStyles.Bold, TextAlignmentOptions.Left);
            float detailY = y - Space5;

            pilotRankLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                   FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), Accent());
            pilotXpBarWidth = dossierW;
            detailY -= Space3;

            pilotStatsLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                    FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            pilotPersonaLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight + 3f;

            // Pilot specialization skills (icons only with full tactical tooltips)
            const float skillSize = 20f;
            const float skillGap = 4f;
            pilotSkillIcons.Clear();
            pilotSkillIcons.Add(new PilotSkillIcon(parent, new Rect(dossierX + (skillSize + skillGap) * 0, detailY, skillSize, skillSize),
                "attack", "ACE COMBATANT", "Enhanced gun-lead tracking & rapid missile lock acquisition"));
            pilotSkillIcons.Add(new PilotSkillIcon(parent, new Rect(dossierX + (skillSize + skillGap) * 1, detailY, skillSize, skillSize),
                "maneuver", "HIGH-G TOLERANCE", "Sustained maximum turn rate without pilot blackout"));
            pilotSkillIcons.Add(new PilotSkillIcon(parent, new Rect(dossierX + (skillSize + skillGap) * 2, detailY, skillSize, skillSize),
                "cargo", "PRECISION STRIKE", "High-accuracy CCIP dive bombing and standoff release"));
            pilotSkillIcons.Add(new PilotSkillIcon(parent, new Rect(dossierX + (skillSize + skillGap) * 3, detailY, skillSize, skillSize),
                "cover", "FUEL DISCIPLINE", "10% reduced throttle fuel consumption at cruise speeds"));
            pilotSkillIcons.Add(new PilotSkillIcon(parent, new Rect(dossierX + (skillSize + skillGap) * 4, detailY, skillSize, skillSize),
                "jam", "AVIONICS SPECIALIST", "Extended ECM radar jamming reach and rapid flare countermeasure bursts"));
            detailY -= skillSize + 4f;

            // Integrated bio narrative in right data column
            pilotBackgroundLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, 38f),
                                         Friendly(), FontMicro, FontStyles.Italic,
                                         TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;

            y -= portraitH + Space2;

            y = Heading(parent, y, "AIRFRAME");
            float airframeRailY = y;

            Color ghost = WingColor();
            ghost.a = 0.075f;
            airframeSilhouette = AddSprite(parent, "AirframeSilhouette",
                      IconFactory.Get("airframe"),
                      new Rect(PanelWidth - Pad - 132f, y - 8f, 128f, 128f), ghost);

            airframeTypeLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                      FontLead, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeStateLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeOrderLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeLoadoutLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Dim(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeWeaponsLabel = Label(parent, "", new Rect(Pad, y, w, Space6 + Space2), Friendly(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            airframeWeaponsLabel.enableWordWrapping = true;
            airframeWeaponsLabel.overflowMode = TextOverflowModes.Ellipsis;

            float airframeBottom = y - (Space6 + Space2) - Space5;
            airframeCardRail = Rule(parent,
                new Rect(Pad, airframeRailY, 3f, airframeRailY - airframeBottom),
                FrameColor());
            return airframeBottom;
        }

        private static void RefreshWingPage(WingRegistry wing)
        {
            List<WingPilot> display = WingPilotRoster.DisplayRoster();
            int count = display.Count;

            SyncPilotRows(pilotRows, pilotRosterArea);
            int first = pilotPager != null ? pilotPager.Refresh(count) : 0;

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

            if (pilotCardRail != null)
                pilotCardRail.color = kia ? Alert() : RankColor(focus.Rank);

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
                           focus.Sorties + " SORTIE(S)" + (kia ? "   —   MIA" : "");
            string persona = kia
                ? "STATUS   MISSING IN ACTION"
                : "RADIO PROFILE   " + focus.Persona.ToString().ToUpperInvariant();

            SetWingDetail(identity, rank, stats, persona, progress,
                          focus.Background, kia ? "NO AIRFRAME" : "",
                          "", "", "", "");

            if (pilotIdentityLabel != null) pilotIdentityLabel.color = kia ? Alert() : Green();

            if (flying == null)
            {
                SetSilhouetteAlpha(0f);
                if (airframeTypeLabel != null)
                {
                    airframeTypeLabel.text = kia ? "NO AIRFRAME   (GROUNDED)" : "NO AIRFRAME";
                    airframeTypeLabel.color = kia ? Alert() : Dim();
                }
                if (airframeStateLabel != null)
                {
                    airframeStateLabel.text = kia
                        ? "LOST IN ACTION  ·  WILL NOT BE RECOVERED"
                        : "ON THE GROUND  ·  AWAITING AN AIRFRAME";
                    airframeStateLabel.color = kia ? Alert() : Friendly();
                }
                return;
            }

            SetSilhouetteAlpha(0.075f);

            Aircraft aircraft = flying.Aircraft;
            AircraftDefinition definition = DefinitionOf(flying);

            string type = definition != null
                ? UiTheme.Truncate(definition.unitName, 22) + "   SLOT " + flying.Slot
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
                bool poor = fuel <= WingTuning.BingoFuel ||
                            ammo <= 0 || integrity < 0.75f;
                airframeStateLabel.color = poor ? Warning() : Friendly();
                if (airframeCardRail != null)
                    airframeCardRail.color = poor ? Warning() : MemberFrameColor();
            }

            if (airframeTypeLabel != null && aircraft != null && !aircraft.LocalSim)
                airframeTypeLabel.text = type + "   (NOT LOCALLY SIMULATED)";
        }

        /// <summary>Keep the ghost airframe faint, or hide it entirely on the empty page.</summary>
        private static void SetSilhouetteAlpha(float alpha)
        {
            if (airframeSilhouette == null) return;
            Color c = WingColor();
            c.a = alpha;
            airframeSilhouette.color = c;
        }

        /// <summary>
        /// Dress the portrait and corner badge for whoever is being inspected.
        ///
        /// Alive pilots get a subtle tinting by rank and a rank letter; a lost pilot gets a
        /// red wash, a centred KIA stencil, and a badge turned to the alert colour. The
        /// shared placeholder sprite is tinted rather than swapped, since the roster has one
        /// portrait asset — a per-pilot art path would only need to feed this a different
        /// sprite.
        /// </summary>
        private static void RenderPilotVisual(WingPilot pilot)
        {
            if (pilotPortrait != null)
                pilotPortrait.color = pilot == null
                    ? Color.white
                    : pilot.Lost ? new Color(0.7f, 0.45f, 0.45f, 0.85f) : Color.white;

            if (pilotKiaOverlay != null) pilotKiaOverlay.gameObject.SetActive(pilot != null && pilot.Lost);

            if (pilotSkillIcons.Count > 0)
            {
                int unlocked = 0;
                if (pilot != null && !pilot.Lost)
                {
                    switch (pilot.Rank)
                    {
                        case WingRank.Rookie: unlocked = 1; break;
                        case WingRank.Wingman: unlocked = 2; break;
                        case WingRank.Veteran: unlocked = 3; break;
                        case WingRank.Ace: unlocked = 5; break;
                    }
                }
                for (int i = 0; i < pilotSkillIcons.Count; i++)
                {
                    pilotSkillIcons[i].SetActive(i < unlocked);
                }
            }
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

        /// <summary>A compact live inventory, grouped by the weapon definition on each station.</summary>
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
                result += count + UiTheme.Truncate(names[i], 20) + "  [" + ammo[i] + "]";
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
    }
}
