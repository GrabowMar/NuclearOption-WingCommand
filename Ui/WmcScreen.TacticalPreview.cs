using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private static TMP_Text doctrineTitleLabel, doctrineProfileLabel, doctrineRulesLabel, doctrineWeaponsLabel;
        private static float formationRadarCenterY;
        private static readonly List<RectTransform> formationWingmenDots = new List<RectTransform>();
        private static readonly List<Image> formationVectorLines = new List<Image>();

        // Shared by both geometry pages so formation and current behaviour never disappear.
        private static float AddTacticalPreview(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            const float radarW = 108f;
            const float boxH = 88f;

            // Formation preview.
            WingUi.TacticalCard(parent, new Rect(Pad, y, radarW, boxH), WingUi.RailEmerald);

            // Low-opacity preview crosshairs.
            Color crosshairCol = new Color(WingUi.RailEmerald.r, WingUi.RailEmerald.g, WingUi.RailEmerald.b, 0.25f);
            Rule(parent, new Rect(Pad + radarW * 0.5f, y - 6f, 1f, boxH - 28f), crosshairCol);
            Rule(parent, new Rect(Pad + 6f, y - (boxH - 20f) * 0.5f, radarW - 12f, 1f), crosshairCol);

            float radarCenterX = Pad + radarW * 0.5f;
            formationRadarCenterY = y - 22f;

            // Centred leader symbol.
            Label(parent, "^", new Rect(radarCenterX - 10f, formationRadarCenterY - 6f, 20f, 16f),
                  Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            Label(parent, "LDR", new Rect(radarCenterX - 15f, formationRadarCenterY + 10f, 30f, 10f),
                  Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            // Three follower markers with slot connections.
            formationWingmenDots.Clear();
            formationVectorLines.Clear();

            for (int i = 0; i < 3; i++)
            {
                var line = Rule(parent, new Rect(radarCenterX, formationRadarCenterY, 1f, 1f),
                                new Color(WingUi.RailEmerald.r, WingUi.RailEmerald.g, WingUi.RailEmerald.b, 0.35f));
                formationVectorLines.Add(line);

                var dotGo = new GameObject("WingmanDot_" + i, typeof(RectTransform));
                var rt = dotGo.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Label(rt, (i + 1).ToString(), new Rect(0f, 0f, 16f, 16f), Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
                Place(rt, new Rect(radarCenterX, formationRadarCenterY, 16f, 16f));
                formationWingmenDots.Add(rt);
            }

            // Combat-doctrine readouts.
            float docX = Pad + radarW + Gap;
            float docW = w - radarW - Gap;

            WingUi.TacticalCard(parent, new Rect(docX, y, docW, boxH), WingUi.RailCyan);

            float lineY = y - 4f;
            doctrineTitleLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, 16f),
                Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            lineY -= 18f;

            doctrineProfileLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 20f;

            doctrineRulesLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            lineY -= 20f;

            doctrineWeaponsLabel = Label(parent, "",
                new Rect(docX + Space2, lineY, docW - Space3, LineHeight),
                WingUi.TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);

            return y - boxH - Space2;

        }

        private static void RefreshTacticalPreview()
        {
            WingRegistry wing = Wing();
            FormationShape shape = WingFormation.Shape;
            WingWeaponPreference? shared = WingCommandManager.Instance?.ScopeWeaponPreference();
            const float radarW = 108f;
            float radarCenterX = Pad + radarW * 0.5f;

            int totalInWing = (wing != null ? wing.Count : 0) + WingShopDelivery.PendingCount;
            for (int i = 0; i < formationWingmenDots.Count; i++)
            {
                Vector3 coord = FormationSolver.SlotCoordinates(i + 1, shape, 1f, 1f);
                float px = radarCenterX + Mathf.Clamp(coord.x * 16f, -44f, 44f);
                float py = formationRadarCenterY + Mathf.Clamp(coord.z * 16f, -48f, 12f);

                RectTransform dot = formationWingmenDots[i];
                if (dot != null)
                {
                    Place(dot, new Rect(px - 8f, py + 8f, 16f, 16f));
                    bool inWing = i < totalInWing;
                    dot.gameObject.SetActive(true);
                    var lbl = dot.GetComponentInChildren<TMP_Text>();
                    if (lbl != null) lbl.color = inWing ? Green() : new Color(0.4f, 0.6f, 0.55f, 0.45f);
                }

                if (i < formationVectorLines.Count && formationVectorLines[i] != null)
                {
                    Image line = formationVectorLines[i];
                    float dx = px - radarCenterX;
                    float dy = py - formationRadarCenterY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                    RectTransform lineRt = line.rectTransform;
                    lineRt.sizeDelta = new Vector2(dist, 1f);
                    lineRt.anchoredPosition = new Vector2(radarCenterX, formationRadarCenterY);
                    lineRt.localRotation = Quaternion.Euler(0f, 0f, angle);
                    bool inWing = i < totalInWing;
                    line.color = inWing ? new Color(0.2f, 0.65f, 0.45f, 0.45f) : new Color(0.2f, 0.35f, 0.3f, 0.2f);
                }
            }

            string roeName = wing != null ? CombatFacade.Roe.Label(wing.Roe) : "HOLD";
            string wepName = shared.HasValue ? WingWeaponPreferences.Label(shared.Value) : "MIXED";
            if (wing == null || wing.Count == 0 || WingCommandManager.Instance?.Selection.IsNone == true)
                wepName = "NONE";
            string shapeName = FormationShapes.Pretty(shape).ToUpperInvariant();

            if (doctrineTitleLabel != null)
                doctrineTitleLabel.text = shapeName;

            if (doctrineProfileLabel != null)
                doctrineProfileLabel.text = "ROE: " + roeName + "  /  WHOLE FLIGHT";

            if (doctrineRulesLabel != null)
            {
                doctrineRulesLabel.text = "WEAPONS: " + wepName + "  /  SELECTED";
            }

            if (doctrineWeaponsLabel != null)
            {
                var scope = WingCommandManager.Instance?.Commands.Scope(wholeWing: false);
                string state = "NO SELECTION";
                if (scope != null && scope.Count > 0)
                {
                    state = ShortOrder(scope[0]);
                    foreach (WingMember member in scope)
                        if (ShortOrder(member) != state) { state = "MIXED ORDERS"; break; }
                }
                doctrineWeaponsLabel.text = "STATE: " + state;
            }
        }

        private static void ResetTacticalPreview()
        {
            doctrineTitleLabel = doctrineProfileLabel = doctrineRulesLabel = doctrineWeaponsLabel = null;
            formationWingmenDots.Clear();
            formationVectorLines.Clear();
        }
    }
}
