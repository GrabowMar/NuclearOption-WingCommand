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

        // Left Bento Card: Target & Threat
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

        // Bottom Bento Strip: Telemetry & Formation
        private static Image bentoTelemRail;
        private static TMP_Text bentoTelemAlt;
        private static TMP_Text bentoTelemSpd;
        private static TMP_Text bentoTelemSlot;
        private static TMP_Text bentoTelemFuel;
        private static TMP_Text bentoTelemHull;

        private static readonly List<BentoRawStore> bentoStoresCache = new List<BentoRawStore>(16);

        private static float AddTacticalBento(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;

            y = Heading(parent, y, "COMBAT SITUATION & STORES");

            bentoScopeLabel = Label(parent, "", new Rect(Pad + 180f, y + 16f, w - 180f, Space4),
                                    Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            float cardW = (w - Gap) * 0.5f;
            const float cardH = 92f;

            // Left card: Target designation and threat
            var (_, targetRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, cardW, cardH), WingUi.RailCyan);
            bentoTargetRail = targetRail;

            float lineY = y - 4f;
            bentoTargetTitle = Label(parent, "TARGET", new Rect(Pad + Space2, lineY, cardW - Space3, 16f),
                                     Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoTargetDetail = Label(parent, "NO TARGET DESIGNATED", new Rect(Pad + Space2, lineY, cardW - Space3, LineHeight),
                                      Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoTargetKinematics = Label(parent, "SCANNING SECTOR", new Rect(Pad + Space2, lineY, cardW - Space3, LineHeight),
                                          Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoThreatLabel = Label(parent, "THREAT: CLEAR", new Rect(Pad + Space2, lineY, cardW - Space3, LineHeight),
                                     Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            // Right card: Scoped stores aboard
            float rightX = Pad + cardW + Gap;
            var (_, storesRail) = WingUi.TacticalCard(parent, new Rect(rightX, y, cardW, cardH), WingUi.RailEmerald);
            bentoStoresRail = storesRail;

            lineY = y - 4f;
            bentoStoresTitle = Label(parent, "STORES ABOARD", new Rect(rightX + Space2, lineY, cardW - Space3, 16f),
                                     Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoStoresLine1 = Label(parent, "—", new Rect(rightX + Space2, lineY, cardW - Space3, LineHeight),
                                     WingUi.TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoStoresLine2 = Label(parent, "—", new Rect(rightX + Space2, lineY, cardW - Space3, LineHeight),
                                     WingUi.TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 18f;
            bentoStoresLine3 = Label(parent, "—", new Rect(rightX + Space2, lineY, cardW - Space3, LineHeight),
                                     Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            foreach (TMP_Text line in new[] { bentoStoresLine1, bentoStoresLine2, bentoStoresLine3 })
            {
                line.enableAutoSizing = true;
                line.fontSizeMin = FontMicro;
                line.fontSizeMax = FontSmall;
            }

            y -= cardH + Gap;

            // Bottom strip: 5-column flight telemetry & aircraft vital state
            const float stripH = 32f;
            var (_, telemRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, stripH), WingUi.RailEmerald);
            bentoTelemRail = telemRail;

            float availableW = w - Space2;
            const float altW = 92f;
            const float spdW = 78f;
            const float slotW = 126f;
            const float fuelW = 84f;
            float hullW = availableW - altW - spdW - slotW - fuelW;
            float telemX = Pad + Space1;
            float telemY = y - 7f;
            bentoTelemAlt = Label(parent, "ALT: —", new Rect(telemX, telemY, altW, LineHeight),
                                  Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += altW;
            bentoTelemSpd = Label(parent, "SPD: —", new Rect(telemX, telemY, spdW, LineHeight),
                                  Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += spdW;
            bentoTelemSlot = Label(parent, "FORM: —", new Rect(telemX, telemY, slotW, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += slotW;
            bentoTelemFuel = Label(parent, "FUEL: —", new Rect(telemX, telemY, fuelW, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            telemX += fuelW;
            bentoTelemHull = Label(parent, "HULL: —", new Rect(telemX, telemY, hullW - Space1, LineHeight),
                                   Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);
            foreach (TMP_Text field in new[] { bentoTelemAlt, bentoTelemSpd, bentoTelemSlot,
                                                bentoTelemFuel, bentoTelemHull })
            {
                field.enableAutoSizing = true;
                field.fontSizeMin = FontMicro;
                field.fontSizeMax = FontSmall;
            }

            y -= stripH + Gap;
            return y;
        }

        private static void RefreshTacticalBento(WingRegistry wing, List<WingMember> scope)
        {
            if (bentoScopeLabel == null) return;
            bentoStoresCache.Clear();

            int totalWingCount = wing != null ? wing.Count : 0;
            if (wing == null || totalWingCount == 0)
            {
                bentoScopeLabel.text = "[ NO WINGMEN ]";
                bentoTargetTitle.text = "TARGET";
                bentoTargetDetail.text = "NO ACTIVE AIRCRAFT";
                bentoTargetKinematics.text = "STANDBY";
                bentoThreatLabel.text = "THREAT: CLEAR";
                bentoThreatLabel.color = Friendly();

                bentoStoresTitle.text = "STORES";
                bentoStoresLine1.text = "REQUISITION ON SUPPLY";
                bentoStoresLine2.text = "OR RECRUIT FROM MAP";
                bentoStoresLine3.text = "";

                bentoTelemAlt.text = "ALT: —";
                bentoTelemSpd.text = "SPD: —";
                bentoTelemSlot.text = "FORM: —";
                bentoTelemFuel.text = "FUEL: —";
                bentoTelemFuel.color = Friendly();
                bentoTelemHull.text = "HULL: —";
                bentoTelemHull.color = Friendly();

                if (bentoTargetRail != null) bentoTargetRail.color = WingUi.RailEmerald;
                if (bentoStoresRail != null) bentoStoresRail.color = WingUi.RailEmerald;
                if (bentoTelemRail != null) bentoTelemRail.color = WingUi.RailEmerald;
                return;
            }


            // Case 1: Single wingman selected in command scope
            if (scope != null && scope.Count == 1)
            {
                WingMember m = scope[0];
                string planeCode = !string.IsNullOrEmpty(m.Aircraft?.definition?.code)
                    ? m.Aircraft.definition.code
                    : m.Name;
                string callsign = m.Crew?.Callsign ?? "AI";
                bentoScopeLabel.text = $"[ >{m.Slot} {planeCode} {callsign} ]";

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
                    bentoTargetKinematics.text = $"CLS {TacticalBentoRules.FormatClosingSpeed(closing)} · ALT {TacticalBentoRules.FormatAltitude(tgtAlt)}";
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

                // Stores aboard
                CollectStores(m.Aircraft, bentoStoresCache);
                TacticalBentoRules.FormatSingleMemberStores(bentoStoresCache,
                    out string s1, out string s2, out string s3, out bool isWinchester, out int totalAmmo);

                bentoStoresTitle.text = isWinchester ? "STORES [EMPTY]" : $"STORES ABOARD [{totalAmmo}]";
                bentoStoresLine1.text = s1;
                bentoStoresLine2.text = s2;
                bentoStoresLine3.text = s3;
                if (bentoStoresRail != null)
                    bentoStoresRail.color = isWinchester ? Warning() : WingUi.RailEmerald;

                // Flight telemetry
                float radarAlt = m.Aircraft != null ? m.Aircraft.radarAlt : 0f;
                float speedKnots = m.Aircraft != null ? m.Aircraft.speed * 1.94384f : 0f;
                bentoTelemAlt.text = "ALT: " + TacticalBentoRules.FormatAltitude(radarAlt);
                bentoTelemSpd.text = "SPD: " + TacticalBentoRules.FormatSpeed(speedKnots);
                bentoTelemSlot.text = "FORM: " + TacticalBentoRules.FormatSlotDeviation(m.SlotError, m.IsFlightLead, m.Order == WingOrder.Formation);

                float fuelFrac = m.Fuel;
                int fuelPct = Mathf.Clamp(Mathf.RoundToInt(fuelFrac * 100f), 0, 100);
                bentoTelemFuel.text = "FUEL: " + fuelPct + "%";
                bentoTelemFuel.color = fuelFrac <= WingTuning.BingoFuel ? Alert() : fuelFrac <= 0.30f ? Warning() : Green();

                int hullPct = Mathf.Clamp(Mathf.RoundToInt(integrity * 100f), 0, 100);
                bentoTelemHull.text = "HULL: " + hullPct + "%";
                bentoTelemHull.color = integrity < 0.5f ? Alert() : integrity < 0.85f ? Warning() : Green();
                return;
            }

            // Case 2: Multi-wingman / ALL flight command scope
            int scopeCount = scope != null && scope.Count > 0 ? scope.Count : totalWingCount;
            bentoScopeLabel.text = $"[ SCOPE: {scopeCount} OF {totalWingCount} ]";

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
            float fuelSum = 0f;
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
                fuelSum += f;

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
            bentoTargetKinematics.text = $"ROE: {CombatFacade.Roe.Label(wing.Roe)}";

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

            bentoStoresTitle.text = flightWinchester ? "FLIGHT POOL [EMPTY]" : $"FLIGHT POOL [{flightTotalAmmo}]";
            bentoStoresLine1.text = pool1;
            bentoStoresLine2.text = pool2;
            bentoStoresLine3.text = pool3;
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

                int minFuelPct = Mathf.Clamp(Mathf.RoundToInt(fuelMin * 100f), 0, 100);
                bentoTelemFuel.text = "FUEL: " + minFuelPct + "% MIN";
                bentoTelemFuel.color = fuelMin <= WingTuning.BingoFuel ? Alert() : fuelMin <= 0.30f ? Warning() : Green();

                int avgHullPct = Mathf.Clamp(Mathf.RoundToInt(avgHull * 100f), 0, 100);
                bentoTelemHull.text = "HULL: " + avgHullPct + "%";
                bentoTelemHull.color = avgHull < 0.5f ? Alert() : avgHull < 0.85f ? Warning() : Green();
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
            bentoTelemRail = null;
            bentoTelemAlt = null;
            bentoTelemSpd = null;
            bentoTelemSlot = null;
            bentoTelemFuel = null;
            bentoTelemHull = null;
            bentoStoresCache.Clear();
        }

    }
}
