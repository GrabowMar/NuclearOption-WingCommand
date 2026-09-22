using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private static TMP_Text bentoScopeLabel;

        // Scoped target, stores and flight readouts share fixed, independently readable lanes.
        private static Image bentoTargetRail;
        private static TMP_Text bentoTargetTitle;
        private static TMP_Text bentoTargetDetail;
        private static TMP_Text bentoTargetKinematics;
        private static TMP_Text bentoThreatLabel;

        // Right Bento Card: Scoped Stores
        private static Image bentoStoresRail;
        private static TMP_Text bentoStoresTitle;
        private static TMP_Text bentoStoresLine1;
        private static TMP_Text bentoStoresLine2;
        private static TMP_Text bentoStoresLine3;
        private static TMP_Text bentoStoresFooter;
        private static readonly Image[] bentoStoresPips = new Image[3];

        // Bottom Bento Strip: Telemetry & Formation
        private static Image bentoTelemRail;
        private static TMP_Text bentoTelemAlt;
        private static TMP_Text bentoTelemSpd;
        private static TMP_Text bentoTelemSlot;
        private static TMP_Text bentoTelemFuel;
        private static TMP_Text bentoTelemHull;
        private static Image bentoTelemAltFill;
        private static Image bentoTelemSpdFill;
        private static Image bentoTelemFuelFill;
        private static Image bentoTelemHullFill;

        private static readonly List<BentoRawStore> bentoStoresCache = new List<BentoRawStore>(16);

        private const float BentoStripHeight = 28f;

        /// <summary>Cards stay tall enough for the target table and three store rows.</summary>
        private const float BentoCardMinHeight = 88f;

        /// <summary>Instrument range used to scale the altitude and speed lanes.</summary>
        private const float TelemAltitudeFullScale = 8000f;
        private const float TelemSpeedFullScaleKnots = 600f;

        private static float AddTacticalBento(RectTransform parent, float y)
        {
            // Kept for the refresh pass. The roster already names the selection.
            bentoScopeLabel = Label(parent, "", new Rect(0f, 0f, 0f, 0f),
                                    Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            float remain = tacticalDeckAvail + y;
            // The cards absorb whatever height the command rows leave, so the deck reaches the
            // viewport foot at the tallest panel; a short panel keeps the minimum and scrolls.
            float cardH = Mathf.Max(BentoCardMinHeight, remain - BentoStripHeight - TacticalGap);
            float cardW = (ContentWidth - TacticalGap) * 0.5f;

            // Target card: title and three readouts on an equal-pitch table over hairlines.
            var (_, targetRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, cardW, cardH), WingUi.RailCyan);
            bentoTargetRail = targetRail;

            float targetX = Pad + Space2;
            float targetW = cardW - Space3;
            float targetPitch = Mathf.Max(14f, (cardH - 6f) / 4f);
            float targetTextH = targetPitch - 2f;
            bentoTargetTitle = Label(parent, "TARGET", new Rect(targetX, y - 3f, targetW, targetTextH),
                                     WingUi.TextPrimary, FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            bentoTargetDetail = Label(parent, "NO TARGET DESIGNATED",
                                      new Rect(targetX, y - 3f - targetPitch, targetW, targetTextH),
                                      Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            bentoTargetKinematics = Label(parent, "SCANNING SECTOR",
                                          new Rect(targetX, y - 3f - targetPitch * 2f, targetW, targetTextH),
                                          Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            bentoThreatLabel = Label(parent, "THREAT —",
                                     new Rect(targetX, y - 3f - targetPitch * 3f, targetW, targetTextH),
                                     Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < 3; i++)
                Rule(parent, new Rect(targetX, y - 2f - (i + 1) * targetPitch, targetW, 1f), WingUi.BorderSubtle);

            // Stores card: title, three station or pool rows over hairlines, footer total.
            float rightX = Pad + cardW + TacticalGap;
            var (_, storesRail) = WingUi.TacticalCard(parent, new Rect(rightX, y, cardW, cardH),
                                                      WingUi.RailEmerald);
            bentoStoresRail = storesRail;

            const float storesTitleH = 16f;
            const float storesFooterH = 12f;
            float storesX = rightX + Space2;
            float storesW = cardW - Space3;
            float storesTitleTop = y - 3f;
            bentoStoresTitle = Label(parent, "STORES", new Rect(storesX, storesTitleTop, storesW, storesTitleH),
                                     WingUi.TextPrimary, FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Rule(parent, new Rect(storesX, storesTitleTop - storesTitleH + 1f, storesW, 1f), WingUi.BorderSubtle);

            float rowsHeight = Mathf.Max(3f * 12f, cardH - storesTitleH - storesFooterH - 7f);
            float rowPitch = rowsHeight / 3f;
            float rowTextH = Mathf.Min(22f, rowPitch - 2f);
            TMP_Text[] storesLines = { bentoStoresLine1, bentoStoresLine2, bentoStoresLine3 };
            for (int i = 0; i < storesLines.Length; i++)
            {
                float rowTop = storesTitleTop - storesTitleH - i * rowPitch;
                bentoStoresPips[i] = Panel(parent, new Rect(storesX, rowTop - rowPitch * 0.5f - 4f, 8f, 8f),
                                           WingUi.RailInert);
                bentoStoresPips[i].sprite = AvSprites.Slot;
                bentoStoresPips[i].type = Image.Type.Sliced;
                storesLines[i] = Label(parent, "—",
                                       new Rect(storesX + 10f, rowTop - rowPitch * 0.5f - rowTextH * 0.5f,
                                                storesW - 10f, rowTextH),
                                       WingUi.TextPrimary, FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                Rule(parent, new Rect(storesX, rowTop - rowPitch + 1f, storesW, 1f), WingUi.BorderSubtle);
            }
            bentoStoresLine1 = storesLines[0];
            bentoStoresLine2 = storesLines[1];
            bentoStoresLine3 = storesLines[2];
            foreach (TMP_Text line in storesLines)
            {
                line.enableAutoSizing = true;
                line.fontSizeMin = FontMicro;
                line.fontSizeMax = FontSmall;
            }
            bentoStoresFooter = Label(parent, "", new Rect(storesX, y - cardH + 15f, storesW, storesFooterH),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            y -= cardH + TacticalGap;

            // Bottom strip: 5-column flight telemetry & aircraft vital state
            var (_, telemRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, ContentWidth, BentoStripHeight),
                                                     WingUi.RailEmerald);
            bentoTelemRail = telemRail;

            float availableW = ContentWidth - Space2;
            const float altW = 88f;
            const float spdW = 74f;
            const float slotW = 120f;
            const float fuelW = 82f;
            float hullW = availableW - altW - spdW - slotW - fuelW;
            float telemX = Pad + Space1;
            const float telemY = -2f;
            float altX = telemX;
            bentoTelemAlt = Label(parent, "ALT: —", new Rect(telemX, y + telemY, altW, LineHeight),
                                  Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += altW;
            float spdX = telemX;
            bentoTelemSpd = Label(parent, "SPD: —", new Rect(telemX, y + telemY, spdW, LineHeight),
                                  Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += spdW;
            bentoTelemSlot = Label(parent, "FORM: —", new Rect(telemX, y + telemY, slotW, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += slotW;
            float fuelX = telemX;
            bentoTelemFuel = Label(parent, "FUEL: —", new Rect(telemX, y + telemY, fuelW, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += fuelW;
            float hullX = telemX;
            bentoTelemHull = Label(parent, "HULL: —", new Rect(telemX, y + telemY, hullW - Space1, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);
            foreach (TMP_Text field in new[] { bentoTelemAlt, bentoTelemSpd, bentoTelemSlot,
                                                bentoTelemFuel, bentoTelemHull })
            {
                field.enableAutoSizing = true;
                field.fontSizeMin = FontMicro;
                field.fontSizeMax = FontSmall;
            }

            // One hairline meter per numeric lane so the strip reads as instruments, not text.
            MeterBar(parent, new Rect(altX, y - 22f, 72f, 3f), out bentoTelemAltFill, WingUi.RailCyan);
            MeterBar(parent, new Rect(spdX, y - 22f, 58f, 3f), out bentoTelemSpdFill, WingUi.RailEmerald);
            MeterBar(parent, new Rect(fuelX, y - 22f, 60f, 3f), out bentoTelemFuelFill, Green());
            MeterBar(parent, new Rect(hullX, y - 22f, 56f, 3f), out bentoTelemHullFill, Green());

            y -= BentoStripHeight;
            return y;
        }

        private static void RefreshTacticalBento(WingRegistry wing, List<WingMember> scope)
        {
            if (bentoScopeLabel == null) return;
            bentoStoresCache.Clear();

            int totalWingCount = wing != null ? wing.Count : 0;
            if (wing == null || totalWingCount == 0)
            {
                bentoScopeLabel.text = "NO ACTIVE WINGMEN IN FLIGHT";
                bentoTargetTitle.text = "TARGET";
                bentoTargetDetail.text = "RADAR LINK STANDBY";
                bentoTargetKinematics.text = "AWAITING FLIGHT";
                bentoThreatLabel.text = "THREAT: —";
                bentoThreatLabel.color = Dim();

                bentoStoresTitle.text = "STORES";
                SetStoresRow(0, "NO ACTIVE CRAFT", Dim());
                SetStoresRow(1, "REQUISITION ON SUPPLY", Dim());
                SetStoresRow(2, "OR CONFLICT MAP RECRUIT", Dim());
                SetStoresFooter("STORES — NO AIRCRAFT");

                bentoTelemAlt.text = "ALT: —";
                bentoTelemSpd.text = "SPD: —";
                bentoTelemSlot.text = "FORM: —";
                bentoTelemFuel.text = "FUEL: —";
                bentoTelemFuel.color = Dim();
                bentoTelemHull.text = "HULL: —";
                bentoTelemHull.color = Dim();
                SetMeter(bentoTelemAltFill, 72f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemSpdFill, 58f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemFuelFill, 60f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemHullFill, 56f, 0f, WingUi.RailInert);

                if (bentoTargetRail != null) bentoTargetRail.color = WingUi.RailInert;
                if (bentoStoresRail != null) bentoStoresRail.color = WingUi.RailInert;
                if (bentoTelemRail != null) bentoTelemRail.color = WingUi.RailInert;
                return;
            }

            if (bentoTelemRail != null) bentoTelemRail.color = WingUi.RailEmerald;

            // Case 1: Single wingman selected in command scope
            if (scope != null && scope.Count == 1)
            {
                WingMember m = scope[0];
                string planeCode = !string.IsNullOrEmpty(m.Aircraft?.definition?.code)
                    ? m.Aircraft.definition.code
                    : m.Name;
                string callsign = m.Crew?.Callsign ?? "AI";
                bentoScopeLabel.text = $"SELECTED: {m.Slot} · {planeCode} · {callsign}";

                // Target telemetry
                Unit tgt = m.AssignedTarget;
                if (tgt != null && !tgt.disabled)
                {
                    string targetName = !string.IsNullOrEmpty(tgt.definition?.code) ? tgt.definition.code : tgt.unitName;
                    float dist = Vector3.Distance(m.Aircraft.transform.position, tgt.transform.position);

                    Vector3 relVel = (m.Aircraft.rb != null && tgt.rb != null)
                        ? m.Aircraft.rb.velocity - tgt.rb.velocity
                        : Vector3.zero;
                    Vector3 toTgt = (tgt.transform.position - m.Aircraft.transform.position).normalized;
                    float closing = Vector3.Dot(relVel, toTgt);
                    float tgtAlt = tgt.transform.position.y;

                    string badge = TargetClassBadge(tgt);
                    bentoTargetTitle.text = "TGT: " + AvTheme.Truncate(targetName, 13);
                    bentoTargetDetail.text = $"{badge} · RNG {TacticalBentoRules.FormatDistance(dist)}";
                    bentoTargetKinematics.text = $"CLOSING {TacticalBentoRules.FormatClosingSpeed(closing)} · ALT {TacticalBentoRules.FormatAltitude(tgtAlt)}";
                    if (bentoTargetRail != null) bentoTargetRail.color = WingUi.RailCyan;
                }
                else
                {
                    bentoTargetTitle.text = "TARGET";
                    bentoTargetDetail.text = "NO TARGET DESIGNATED";
                    bentoTargetKinematics.text = ShortOrder(m);
                    if (bentoTargetRail != null) bentoTargetRail.color = WingUi.RailEmerald;
                }

                // Threat & defense
                MissileWarning mws = m.Aircraft != null ? m.Aircraft.GetMissileWarningSystem() : null;
                bool warned = mws != null && mws.IsWarning();
                string seeker = null;
                if (warned && mws.TryGetNearestIncoming(out Missile nearest) && nearest != null)
                    seeker = nearest.GetSeekerType();

                bool isDefending = m.Order == WingOrder.FallBack || m.IsPanicking;
                float integrity = m.Integrity;
                BentoThreatInfo threat = TacticalBentoRules.ResolveThreat(warned, seeker, integrity, isDefending);

                bentoThreatLabel.text = threat.StatusText;
                if (threat.Level == BentoThreatLevel.Danger)
                {
                    bentoThreatLabel.color = Alert();
                    if (bentoTargetRail != null) bentoTargetRail.color = Alert();
                }
                else if (threat.Level == BentoThreatLevel.Caution)
                {
                    bentoThreatLabel.color = Warning();
                    if (bentoTargetRail != null) bentoTargetRail.color = Warning();
                }
                else
                {
                    bentoThreatLabel.color = Friendly();
                }

                // Stores aboard: one named row per hardpoint, plus a mass and readiness footer.
                CollectStores(m.Aircraft, bentoStoresCache);
                TacticalBentoRules.FormatSingleMemberStores(bentoStoresCache,
                    out string emptyLine, out _, out _, out bool isWinchester, out int totalAmmo);

                bentoStoresTitle.text = isWinchester ? "STORES [EMPTY]" : $"STORES ABOARD [{totalAmmo}]";
                if (bentoStoresRail != null)
                    bentoStoresRail.color = isWinchester ? Warning() : WingUi.RailEmerald;

                int stationCount = 0;
                int readyStations = 0;
                int shownStations = 0;
                float storeMass = 0f;
                if (m.Aircraft != null && m.Aircraft.weaponStations != null)
                {
                    for (int i = 0; i < m.Aircraft.weaponStations.Count; i++)
                    {
                        WeaponStation station = m.Aircraft.weaponStations[i];
                        if (station == null || station.Cargo) continue;
                        stationCount++;
                        WeaponInfo info = station.WeaponInfo;
                        bool jammer = info != null && info.jammer;
                        bool ready = station.Ammo > 0 && !station.Reloading;
                        if (ready) readyStations++;
                        float stationMass = station.Ammo > 0 && info != null && info.massPerRound > 0f
                            ? info.massPerRound * station.Ammo : 0f;
                        storeMass += stationMass;
                        if (shownStations >= bentoStoresPips.Length) continue;

                        Color stateColor = jammer ? WingUi.RailCyan
                            : station.Reloading ? Warning()
                            : station.Ammo <= 0 ? Alert()
                            : Green();
                        string state = jammer ? "ECM" : station.Reloading ? "RELOAD" : station.Ammo <= 0 ? "EMPTY" : "READY";
                        SetStoresRow(shownStations, "ST" + station.Number + " " + StoreLabel(info) + " " +
                            station.Ammo + "/" + station.FullAmmo +
                            (stationMass >= 1f ? " " + Mathf.RoundToInt(stationMass) + " kg" : "") +
                            " " + state, stateColor);
                        shownStations++;
                    }
                }

                if (stationCount == 0)
                {
                    SetStoresRow(0, emptyLine, Dim());
                    SetStoresRow(1, "REARM VIA REFIT", Dim());
                    SetStoresRow(2, "", Dim());
                    SetStoresFooter("WINCHESTER · ORDER REFIT");
                }
                else
                {
                    for (int i = shownStations; i < bentoStoresPips.Length; i++) SetStoresRow(i, "", Dim());
                    SetStoresFooter(stationCount + (stationCount == 1 ? " STATION · " : " STATIONS · ") +
                        readyStations + " READY" + (storeMass >= 1f ? " · " + Grouped(storeMass) + " kg" : ""));
                }

                // Flight telemetry
                float radarAlt = m.Aircraft != null ? m.Aircraft.radarAlt : 0f;
                float speedKnots = m.Aircraft != null ? m.Aircraft.speed * 1.94384f : 0f;
                bentoTelemAlt.text = "ALT: " + TacticalBentoRules.FormatAltitude(radarAlt);
                bentoTelemSpd.text = "SPD: " + TacticalBentoRules.FormatSpeed(speedKnots);
                bentoTelemSlot.text = "FORM: " + TacticalBentoRules.FormatSlotDeviation(m.SlotError, m.IsFlightLead, m.Order == WingOrder.Formation);
                SetMeter(bentoTelemAltFill, 72f, Mathf.Clamp01(radarAlt / TelemAltitudeFullScale), WingUi.RailCyan);
                SetMeter(bentoTelemSpdFill, 58f, Mathf.Clamp01(speedKnots / TelemSpeedFullScaleKnots), WingUi.RailEmerald);

                float fuelFrac = m.Fuel;
                int fuelPct = Mathf.Clamp(Mathf.RoundToInt(fuelFrac * 100f), 0, 100);
                Color fuelColor = fuelFrac <= WingTuning.BingoFuel ? Alert() : fuelFrac <= 0.30f ? Warning() : Green();
                bentoTelemFuel.text = "FUEL: " + fuelPct + "%";
                bentoTelemFuel.color = fuelColor;
                SetMeter(bentoTelemFuelFill, 60f, fuelFrac, fuelColor);

                int hullPct = Mathf.Clamp(Mathf.RoundToInt(integrity * 100f), 0, 100);
                Color hullColor = integrity < 0.5f ? Alert() : integrity < 0.85f ? Warning() : Green();
                bentoTelemHull.text = "HULL: " + hullPct + "%";
                bentoTelemHull.color = hullColor;
                SetMeter(bentoTelemHullFill, 56f, integrity, hullColor);
                return;
            }

            // Case 2: Multi-wingman / ALL flight command scope
            int scopeCount = scope != null && scope.Count > 0 ? scope.Count : totalWingCount;
            bentoScopeLabel.text = $"COMMAND SCOPE: {scopeCount} OF {totalWingCount} AIRCRAFT";

            int targetsCount = 0;
            int threatCount = 0;
            int engagingCount = 0;
            int formationCount = 0;
            int defendingCount = 0;
            int otherCount = 0;
            float altSum = 0f;
            float spdSum = 0f;
            float hullSum = 0f;
            float fuelMin = 1f;
            int liveCount = 0;

            var memberList = scope != null && scope.Count > 0 ? scope : wing.Members;

            for (int i = 0; i < memberList.Count; i++)
            {
                WingMember mem = memberList[i];
                if (mem == null || !mem.Alive) continue;

                liveCount++;
                altSum += mem.Aircraft != null ? mem.Aircraft.radarAlt : 0f;
                spdSum += mem.Aircraft != null ? mem.Aircraft.speed : 0f;
                hullSum += mem.Integrity;
                float f = mem.Fuel;
                if (f < fuelMin) fuelMin = f;

                if (mem.AssignedTarget != null && !mem.AssignedTarget.disabled) targetsCount++;
                MissileWarning mw = mem.Aircraft != null ? mem.Aircraft.GetMissileWarningSystem() : null;
                if ((mw != null && mw.IsWarning()) || mem.IsPanicking) threatCount++;

                if (mem.Order == WingOrder.Attack || mem.Order == WingOrder.Engage ||
                    mem.Order == WingOrder.FireForEffect || mem.Order == WingOrder.SeekAndDestroy)
                    engagingCount++;
                else if (mem.Order == WingOrder.Formation)
                    formationCount++;
                else if (mem.Order == WingOrder.FallBack || mem.IsPanicking)
                    defendingCount++;
                else
                    otherCount++;

                CollectStores(mem.Aircraft, bentoStoresCache);
            }

            // Target card
            bentoTargetTitle.text = targetsCount > 0
                ? $"TARGETS: {targetsCount} DESIGNATED"
                : "TARGETS: NONE";
            bentoTargetDetail.text = TacticalBentoRules.FormatFlightPosture(
                engagingCount, formationCount, defendingCount, otherCount);
            bentoTargetKinematics.text = "DOCTRINE " + wing.Doctrine.PatternName;

            if (threatCount > 0)
            {
                bentoThreatLabel.text = $"ALERT: {threatCount} EVADING / DEFENDING!";
                bentoThreatLabel.color = Alert();
                if (bentoTargetRail != null) bentoTargetRail.color = Alert();
            }
            else
            {
                bentoThreatLabel.text = "THREAT: ALL CLEAR";
                bentoThreatLabel.color = Friendly();
                if (bentoTargetRail != null)
                    bentoTargetRail.color = targetsCount > 0 ? WingUi.RailCyan : WingUi.RailEmerald;
            }

            // Stores pool
            TacticalBentoRules.FormatFlightStores(bentoStoresCache,
                out string pool1, out string pool2, out string pool3, out bool flightWinchester, out int flightTotalAmmo);

            int jammerPods = 0;
            for (int i = 0; i < bentoStoresCache.Count; i++)
                if (bentoStoresCache[i].IsJammer) jammerPods++;

            bentoStoresTitle.text = flightWinchester ? "FLIGHT POOL [EMPTY]" : $"FLIGHT POOL [{flightTotalAmmo}]";
            SetStoresRow(0, pool1, WingUi.RailCyan);
            SetStoresRow(1, pool2, WingUi.RailEmerald);
            SetStoresRow(2, pool3, jammerPods > 0 ? WingUi.RailCyan : WingUi.RailEmerald);
            SetStoresFooter(liveCount + " AIRCRAFT · " + scopeCount + " IN SCOPE · " + jammerPods + " ECM");
            if (bentoStoresRail != null)
                bentoStoresRail.color = flightWinchester ? Warning() : WingUi.RailEmerald;

            // Telemetry averages
            if (liveCount > 0)
            {
                float avgAlt = altSum / liveCount;
                float avgSpeedKnots = (spdSum / liveCount) * 1.94384f;
                float avgHull = hullSum / liveCount;

                bentoTelemAlt.text = "ALT: " + TacticalBentoRules.FormatAltitude(avgAlt) + " AVG";
                bentoTelemSpd.text = "SPD: " + TacticalBentoRules.FormatSpeed(avgSpeedKnots);
                bentoTelemSlot.text = "FORM: " + FormationShapes.Pretty(WingFormation.Shape);
                SetMeter(bentoTelemAltFill, 72f, Mathf.Clamp01(avgAlt / TelemAltitudeFullScale), WingUi.RailCyan);
                SetMeter(bentoTelemSpdFill, 58f, Mathf.Clamp01(avgSpeedKnots / TelemSpeedFullScaleKnots), WingUi.RailEmerald);

                int minFuelPct = Mathf.Clamp(Mathf.RoundToInt(fuelMin * 100f), 0, 100);
                Color fuelColor = fuelMin <= WingTuning.BingoFuel ? Alert() : fuelMin <= 0.30f ? Warning() : Green();
                bentoTelemFuel.text = "FUEL: " + minFuelPct + "% MIN";
                bentoTelemFuel.color = fuelColor;
                SetMeter(bentoTelemFuelFill, 60f, fuelMin, fuelColor);

                int avgHullPct = Mathf.Clamp(Mathf.RoundToInt(avgHull * 100f), 0, 100);
                Color hullColor = avgHull < 0.5f ? Alert() : avgHull < 0.85f ? Warning() : Green();
                bentoTelemHull.text = "HULL: " + avgHullPct + "%";
                bentoTelemHull.color = hullColor;
                SetMeter(bentoTelemHullFill, 56f, avgHull, hullColor);
            }
            else
            {
                bentoTelemAlt.text = "ALT: —";
                bentoTelemSpd.text = "SPD: —";
                bentoTelemSlot.text = "FORM: —";
                bentoTelemFuel.text = "FUEL: —";
                bentoTelemFuel.color = Friendly();
                bentoTelemHull.text = "HULL: —";
                bentoTelemHull.color = Friendly();
                SetMeter(bentoTelemAltFill, 72f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemSpdFill, 58f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemFuelFill, 60f, 0f, WingUi.RailInert);
                SetMeter(bentoTelemHullFill, 56f, 0f, WingUi.RailInert);
            }
        }

        private static void CollectStores(Aircraft aircraft, List<BentoRawStore> destination)
        {
            if (aircraft == null || aircraft.weaponStations == null) return;

            for (int i = 0; i < aircraft.weaponStations.Count; i++)
            {
                WeaponStation st = aircraft.weaponStations[i];
                if (st == null || st.Cargo) continue;

                WeaponInfo info = st.WeaponInfo;
                string name = info != null ? info.name : "STORE";
                bool isGun = info != null && info.gun;
                bool isMissile = info != null && info.missile;
                bool isJammer = info != null && info.jammer;
                bool isBomb = info != null && (info.bomb || (!isMissile && !isGun && !isJammer));

                destination.Add(new BentoRawStore(name, st.Ammo, st.FullAmmo, isMissile, isGun, isJammer, isBomb));
            }
        }

        /// <summary>Write one stores row: text, colour, and the matching status pip.</summary>
        private static void SetStoresRow(int index, string text, Color color)
        {
            TMP_Text label = index == 0 ? bentoStoresLine1
                : index == 1 ? bentoStoresLine2
                : index == 2 ? bentoStoresLine3
                : null;
            if (label != null)
            {
                label.text = text;
                label.color = color;
            }
            if (index >= 0 && index < bentoStoresPips.Length && bentoStoresPips[index] != null)
                bentoStoresPips[index].color = color;
        }

        private static void SetStoresFooter(string text)
        {
            if (bentoStoresFooter != null) bentoStoresFooter.text = text;
        }

        /// <summary>Short, readable store name for a station row.</summary>
        private static string StoreLabel(WeaponInfo info)
        {
            if (info == null) return "STORE";
            string raw = !string.IsNullOrEmpty(info.shortName) ? info.shortName : info.name;
            if (string.IsNullOrWhiteSpace(raw)) return "STORE";
            string label = raw.Replace("(Clone)", "").Replace("_", " ").Trim().ToUpperInvariant();
            return label.Length > 12 ? label.Substring(0, 12) : label;
        }

        private static string TargetClassBadge(Unit tgt)
        {
            if (tgt == null) return "[TGT]";
            if (tgt is Missile) return "[MSL]";
            if (tgt is Aircraft) return "[AIR]";
            if (tgt.definition != null)
            {
                if (tgt.definition.typeIdentity.radar > 0.25f) return "[RADAR]";
                if (tgt.definition.typeIdentity.air > 0.5f) return "[AIR]";
            }
            return "[GND]";
        }

        private static void ResetTacticalBento()
        {
            bentoScopeLabel = null;
            bentoTargetRail = null;
            bentoTargetTitle = null;
            bentoTargetDetail = null;
            bentoTargetKinematics = null;
            bentoThreatLabel = null;
            bentoStoresRail = null;
            bentoStoresTitle = null;
            bentoStoresLine1 = null;
            bentoStoresLine2 = null;
            bentoStoresLine3 = null;
            bentoStoresFooter = null;
            Array.Clear(bentoStoresPips, 0, bentoStoresPips.Length);
            bentoTelemRail = null;
            bentoTelemAlt = null;
            bentoTelemSpd = null;
            bentoTelemSlot = null;
            bentoTelemFuel = null;
            bentoTelemHull = null;
            bentoTelemAltFill = null;
            bentoTelemSpdFill = null;
            bentoTelemFuelFill = null;
            bentoTelemHullFill = null;
            bentoStoresCache.Clear();
        }

    }
}
