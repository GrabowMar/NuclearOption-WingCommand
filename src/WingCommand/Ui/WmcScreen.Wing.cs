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
    /// <summary>The WMC panel's WING tab: one wingman's pilot record and airframe state.</summary>
    internal static partial class WmcScreen
    {
        // --- Wing page ---
        private static readonly List<PickRow> wingRows = new List<PickRow>();
        private static RectTransform wingRosterArea;
        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static TMP_Text pilotStatsLabel;
        private static TMP_Text pilotPersonaLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;
        private static TMP_Text airframeTypeLabel;
        private static TMP_Text airframeStateLabel;
        private static TMP_Text airframeOrderLabel;
        private static TMP_Text airframeLoadoutLabel;
        private static TMP_Text airframeWeaponsLabel;

        // -------------------------------------------------------------------- wing page

        /// <summary>
        /// Who is flying, how they are doing, and what state their aircraft is in.
        ///
        /// Read-only by design. Everything that can be changed about a wingman already has
        /// a control somewhere else, and duplicating those here would give the player two
        /// places to look for the same switch.
        /// </summary>
        private static float AddWingPage(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");
            y = ColumnHeaders(parent, y, WingInspectColumns);

            wingRosterArea = RosterViewport(parent, "WingRoster", y);
            y -= RowPitch * RosterRowsPerPage + Gap;

            wingPager = new RosterPager(parent, y);
            y -= RowHeight + Gap;

            y = Heading(parent, y, "PILOT");
            float w = PanelWidth - Pad * 2f;

            const float portrait = 82f;
            const float portraitGap = Space3;
            Panel(parent, new Rect(Pad, y, portrait, portrait), WingUi.CardFill);
            Outline(parent, new Rect(Pad, y, portrait, portrait), FrameColor());
            AddSprite(parent, "PilotPortrait", PilotPortrait.Sprite,
                      new Rect(Pad + 3f, y - 3f, portrait - 6f, portrait - 6f), Color.white);

            float dossierX = Pad + portrait + portraitGap;
            float dossierW = w - portrait - portraitGap;

            // The pilot's name is the one line on this page that is read first, so it is a
            // step up from the readouts beneath it rather than a pixel up from them.
            pilotIdentityLabel = Label(parent, "", new Rect(dossierX, y, dossierW, Space5), Green(), FontLead,
                                       FontStyles.Normal, TextAlignmentOptions.Left);
            float detailY = y - Space5;

            pilotRankLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                   FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            // Track first, fill second: the fill is resized every refresh, so it must be the
            // later sibling or a full bar would be drawn underneath its own background.
            Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), Accent());
            pilotXpBarWidth = dossierW;
            detailY -= Space3;

            pilotStatsLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                    FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            pilotPersonaLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            y -= portrait + Space2;

            Label(parent, "BIO", new Rect(Pad, y, 34f, Space4), WingColor(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            pilotBackgroundLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 38f, Space6 + Space3),
                                         Friendly(), FontMicro, FontStyles.Italic,
                                         TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;
            y -= Space6 + Space4;

            y = Heading(parent, y, "AIRFRAME");

            // A quiet aircraft silhouette turns the empty lower card into an airframe
            // dossier without competing with the live numbers drawn over it.
            Color ghost = WingColor();
            ghost.a = 0.075f;
            AddSprite(parent, "AirframeSilhouette", IconFactory.Get("airframe"),
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
            return y - (Space6 + Space2) - Space5;
        }

        private static void RefreshWingPage(WingRegistry wing)
        {
            SyncPickRows(wingRows, wingRosterArea);
            int first = wingPager != null ? wingPager.Refresh(wing) : 0;

            for (int i = 0; i < wingRows.Count; i++)
            {
                int index = first + i;
                if (index >= wing.Count)
                {
                    wingRows[i].Hide();
                    continue;
                }

                WingMember member = wing.Members[index];
                wingRows[i].Bind(member, ShortOrder(member));
            }

            WingMember focus = focusMember;
            if (focus == null)
            {
                SetWingDetail("NO WINGMEN ASSIGNED", "", "", "", 0f, "", "", "", "", "", "");
                return;
            }

            WingPilot crew = focus.Crew;
            string identity = crew != null
                ? crew.Callsign + "  ·  " + crew.Name
                : "UNASSIGNED CREW";

            string rank;
            float progress;
            if (crew == null)
            {
                rank = "";
                progress = 0f;
            }
            else
            {
                WingRank crewRank = crew.Rank;
                if (crewRank >= WingPilotRoster.TopRank)
                {
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + crew.Xp + "   MAX RANK";
                    progress = 1f;
                }
                else
                {
                    int floor = WingPilotRoster.XpForRank(crewRank);
                    int ceiling = WingPilotRoster.XpForRank(crewRank + 1);
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + crew.Xp + " / " + ceiling;
                    progress = ceiling > floor
                        ? Mathf.Clamp01((crew.Xp - floor) / (float)(ceiling - floor))
                        : 0f;
                }
            }

            Aircraft aircraft = focus.Aircraft;
            AircraftDefinition definition = DefinitionOf(focus);

            string type = definition != null
                ? UiTheme.Truncate(definition.unitName, 22) + "   SLOT " + focus.Slot
                : "AIRFRAME   SLOT " + focus.Slot;

            // These properties aggregate live component collections. Keep the values
            // coherent within this refresh and avoid repeating the same scans below.
            float fuel = focus.Fuel;
            int ammo = focus.Ammo;
            float integrity = focus.Integrity;
            string state =
                "FUEL " + Mathf.RoundToInt(fuel * 100f) + "%" +
                "   AMMO " + ammo +
                "   HULL " + Mathf.RoundToInt(integrity * 100f) + "%" +
                (focus.CanDeliverCargo ? "   CARGO " + focus.CargoAmmo : "");

            string order =
                "ORDER " + WingOrderCatalog.ShortLabel(focus.Order) +
                "   WEAPONS " + WingWeaponPreferences.Label(focus.WeaponPreference) +
                (focus.DeliveryPending ? "   (DEPARTING)" : "") +
                (focus.IsPanicking ? "   (DEFENSIVE)" : "");

            string loadout = focus.LoadoutKnown
                ? "LOADOUT " + WingLoadoutCatalog.Label(focus.Loadout) +
                  " - fitted at requisition"
                : "LOADOUT as found - assigned mission aircraft keep their own fit";

            string stats = crew != null
                ? "COMBAT RECORD   " + crew.Kills + " KILL(S)   /   " + crew.Sorties + " SORTIE(S)"
                : "";
            string persona = crew != null
                ? "RADIO PROFILE   " + crew.Persona.ToString().ToUpperInvariant()
                : "";

            SetWingDetail(identity, rank, stats, persona, progress,
                          crew != null ? crew.Background : "", type, state, order, loadout,
                          WeaponManifest(aircraft));

            if (airframeStateLabel != null)
            {
                bool poor = fuel <= WingTuning.BingoFuel ||
                            ammo <= 0 || integrity < 0.75f;
                airframeStateLabel.color = poor ? Warning() : Friendly();
            }

            // Nothing on this page writes to the aircraft, so an unreachable one is worth
            // saying rather than worth disabling controls over.
            if (airframeTypeLabel != null && aircraft != null && !aircraft.LocalSim)
                airframeTypeLabel.text = type + "   (NOT LOCALLY SIMULATED)";
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
                    new Vector2(Mathf.Max(1f, pilotXpBarWidth * Mathf.Clamp01(progress)), 3f);
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

    }
}
