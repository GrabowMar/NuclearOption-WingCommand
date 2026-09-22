using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private static TMP_Text doctrineTitleLabel, doctrineProfileLabel, doctrineRulesLabel, doctrineWeaponsLabel;
        private static float formationRadarCenterY;
        private static TMP_Text formationSpacingLabel;
        private static readonly List<Image> formationLiveDots = new List<Image>();
        private static readonly List<RectTransform> formationWingmenDots = new List<RectTransform>();
        private static readonly List<Image> formationVectorLines = new List<Image>();

        private const float PreviewMinHeight = 96f;
        private const float PreviewRadarWidth = 168f;
        private const float GeometryHead = 12f;
        private const float GeometryRow = 26f;

        /// <summary>Gap after the plot, two heads, and two 2-row grids. The plot fills whatever is left.</summary>
        private const float GeometryControlsHeight =
            TacticalGap + GeometryHead + (GeometryRow * 2f + TacticalGap) + TacticalGap
            + GeometryHead + (GeometryRow * 2f + TacticalGap) + 2f;

        private static float TacticalPreviewHeight =>
            Mathf.Max(PreviewMinHeight, tacticalDeckAvail - GeometryControlsHeight);

        // Keep formation and current behaviour visible above the geometry controls.
        private static float AddTacticalPreview(RectTransform parent, float y, float previewH)
        {
            float w = ContentWidth;
            float docX = Pad + PreviewRadarWidth + TacticalGap;
            float docW = w - PreviewRadarWidth - TacticalGap;

            WingUi.TacticalCard(parent, new Rect(Pad, y, w, previewH), WingUi.RailEmerald);

            // Low-opacity preview crosshairs.
            float radarCenterX = Pad + PreviewRadarWidth * 0.5f;
            formationRadarCenterY = y - previewH * 0.5f - 4f;
            Rule(parent, new Rect(radarCenterX, y - 6f, 1f, previewH - 28f), WingUi.BorderSubtle);
            Rule(parent, new Rect(Pad + 6f, formationRadarCenterY, PreviewRadarWidth - 12f, 1f), WingUi.BorderSubtle);

            // Centred leader symbol.
            Label(parent, "^", new Rect(radarCenterX - 10f, formationRadarCenterY - 6f, 20f, 16f),
                  WingUi.TextPrimary, FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            Label(parent, "LDR", new Rect(radarCenterX - 15f, formationRadarCenterY + 10f, 30f, 10f),
                  WingUi.TextPrimary, FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            // Follower markers with slot connections matching maximum wing size.
            formationWingmenDots.Clear();
            formationVectorLines.Clear();
            formationLiveDots.Clear();

            for (int i = 0; i < WingFormation.MaxWingSize; i++)
            {
                var line = Rule(parent, new Rect(radarCenterX, formationRadarCenterY, 1f, 1f),
                                WingUi.BorderSubtle);
                formationVectorLines.Add(line);

                var dotGo = new GameObject("WingmanDot_" + i, typeof(RectTransform));
                var rt = dotGo.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Label(rt, (i + 1).ToString(), new Rect(0f, 0f, 16f, 16f), Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
                Place(rt, new Rect(radarCenterX, formationRadarCenterY, 16f, 16f));
                formationWingmenDots.Add(rt);
                formationLiveDots.Add(Rule(parent, new Rect(radarCenterX, formationRadarCenterY, 4f, 4f), WingUi.RailCyan));
            }

            Label(parent, "SLOT: 1 / [1] SELECTED\nDOT: LIVE / EDGE: FAR",
                new Rect(Pad + Space2, y - previewH + 30f, PreviewRadarWidth - Space4, 30f), Dim(), FontMicro,
                FontStyles.Normal, TextAlignmentOptions.Center);

            // Combat-doctrine readouts on an equal-pitch table so the card fills with height
            // instead of leaving a hole under a fixed stack.
            float tableTop = y - 4f;
            float tableH = Mathf.Max(56f, previewH - 26f);
            float tablePitch = tableH / 4f;
            float tableTextH = Mathf.Clamp(tablePitch - 3f, 14f, 30f);
            float tableOffset = (tablePitch - tableTextH) * 0.5f;
            doctrineTitleLabel = Label(parent, "",
                new Rect(docX + Space2, tableTop - tableOffset, docW - Space3, tableTextH),
                WingUi.TextPrimary, FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            doctrineProfileLabel = Label(parent, "",
                new Rect(docX + Space2, tableTop - tablePitch - tableOffset, docW - Space3, tableTextH),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            doctrineRulesLabel = Label(parent, "",
                new Rect(docX + Space2, tableTop - tablePitch * 2f - tableOffset, docW - Space3, tableTextH),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            doctrineWeaponsLabel = Label(parent, "",
                new Rect(docX + Space2, tableTop - tablePitch * 3f - tableOffset, docW - Space3, tableTextH),
                WingUi.TextPrimary, FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < 3; i++)
                Rule(parent, new Rect(docX + Space2, tableTop - (i + 1) * tablePitch + 1f, docW - Space3, 1f),
                     WingUi.BorderSubtle);

            doctrineProfileLabel.enableWordWrapping = true;
            doctrineRulesLabel.enableWordWrapping = true;
            doctrineWeaponsLabel.enableWordWrapping = true;
            formationSpacingLabel = Label(parent, "", new Rect(docX + Space2, y - previewH + 16f, docW - Space3, LineHeight),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - previewH - TacticalGap;
        }

        private static void RefreshTacticalPreview()
        {
            WingRegistry wing = Wing();
            FormationShape shape = WingFormation.Shape;
            WingWeaponPreference? shared = WingCommandManager.Instance?.ScopeWeaponPreference();
            float radarCenterX = Pad + PreviewRadarWidth * 0.5f;

            var selected = WingCommandManager.Instance?.Commands.Scope(wholeWing: false);
            float scale = 24f;
            for (int slot = 1; slot <= formationWingmenDots.Count; slot++)
            {
                Vector3 point = FormationSolver.SlotCoordinates(slot, shape, 1f, 1f);
                scale = Mathf.Min(scale, 72f / Mathf.Max(1f, Mathf.Abs(point.x)),
                    78f / Mathf.Max(1f, -point.z), 20f / Mathf.Max(1f, point.z));
            }
            for (int i = 0; i < formationWingmenDots.Count; i++)
            {
                Vector3 coord = FormationSolver.SlotCoordinates(i + 1, shape, 1f, 1f);
                float px = radarCenterX + coord.x * scale;
                float py = formationRadarCenterY + coord.z * scale;

                WingMember occupant = null;
                if (wing != null)
                    foreach (WingMember member in wing.Members)
                        if (member.Slot == i + 1) { occupant = member; break; }
                bool inWing = occupant != null;
                bool isSelected = inWing && selected != null && selected.Contains(occupant);
                RectTransform dot = formationWingmenDots[i];
                if (dot != null)
                {
                    Place(dot, new Rect(px - 12f, py + 8f, 24f, 16f));

                    dot.gameObject.SetActive(true);
                    var lbl = dot.GetComponentInChildren<TMP_Text>();
                    if (lbl != null)
                    {
                        lbl.text = isSelected ? "[" + (i + 1) + "]" : (i + 1).ToString();
                        lbl.color = isSelected ? Color.white : inWing ? Green() : Dim();
                        lbl.rectTransform.sizeDelta = new Vector2(24f, 16f);
                    }
                }

                Image live = formationLiveDots[i];
                bool showLive = occupant?.Aircraft != null && wing?.Leader != null && occupant.Leader == wing.Leader;
                live.gameObject.SetActive(showLive);
                if (showLive)
                {
                    Vector3 forward = wing.Leader.transform.forward;
                    forward.y = 0f;
                    forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    Vector3 offset = occupant.Aircraft.transform.position - wing.Leader.transform.position;
                    float metersToPixels = scale / Mathf.Max(1f, WingFormation.SlotSpacing);
                    float liveX = Mathf.Clamp(Vector3.Dot(offset, right) * metersToPixels, -76f, 76f);
                    float liveY = Mathf.Clamp(Vector3.Dot(offset, forward) * metersToPixels, -82f, 22f);
                    Place(live.rectTransform, new Rect(radarCenterX + liveX - 2f, formationRadarCenterY + liveY + 2f, 4f, 4f));
                    live.color = isSelected ? Color.white : WingUi.RailCyan;
                }

                if (i < formationVectorLines.Count && formationVectorLines[i] != null)
                {
                    Image line = formationVectorLines[i];
                    float dx = px - radarCenterX;
                    float dy = py - formationRadarCenterY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                    RectTransform lineRt = line.rectTransform;
                    lineRt.pivot = new Vector2(0f, 0.5f);
                    lineRt.sizeDelta = new Vector2(dist, 1f);
                    lineRt.anchoredPosition = new Vector2(radarCenterX, formationRadarCenterY);
                    lineRt.localRotation = Quaternion.Euler(0f, 0f, angle);

                    line.color = inWing ? WingUi.RailEmerald.WithAlpha(0.35f) : WingUi.BorderSubtle;
                }
            }

            if (formationSpacingLabel != null)
                formationSpacingLabel.text = "BASE SPACING " + WingFormation.SlotSpacing.ToString("0") + " m · PLAN VIEW";

            string roeName = wing != null ? wing.Doctrine.PatternName : "RESERVE";
            string shapeName = FormationShapes.Pretty(shape).ToUpperInvariant();
            string roleDesc = FormationShapes.Role(shape);

            if (doctrineTitleLabel != null)
                doctrineTitleLabel.text = shapeName;

            if (doctrineProfileLabel != null)
                doctrineProfileLabel.text = roleDesc;

            if (doctrineRulesLabel != null)
            {
                int count = (wing != null ? wing.Count : 0);
                doctrineRulesLabel.text = "FLIGHT " + count + " · SELECTED " + (selected?.Count ?? 0) + "\nFIRE POLICY " + roeName;
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
                doctrineWeaponsLabel.text = "ORDER " + state + "\nWEAPONS " + (shared.HasValue ? WingWeaponPreferences.Label(shared.Value) : "MIXED / NONE");
            }
        }

        private static void ResetTacticalPreview()
        {
            doctrineTitleLabel = doctrineProfileLabel = doctrineRulesLabel = doctrineWeaponsLabel = null;
            formationSpacingLabel = null;
            formationWingmenDots.Clear();
            formationVectorLines.Clear();
            formationLiveDots.Clear();
        }
    }
}
