using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>TACTICAL page for ROE, scoped weapon preference, orders, and flight roster.</summary>
    internal static partial class WmcScreen
    {
        internal static bool IsRosterEmpty => (Wing()?.Count ?? 0) + EconomyFacade.ShopDelivery.PendingCount == 0;
        private static WingButton pair12Button;
        private static WingButton pair34Button;
        private static WingButton allButton;
        private static RectTransform rosterEmptyCard;
        private static TMP_Text rosterHeaderLabel;
        private static Image ordersCueRail;
        private static TMP_Text ordersCueLabel;

        private const float ScopeBarHeight = 26f;
        private const float RosterHeadHeight = 18f;
        private const float CueButtonHeight = 28f;
        private const float OrderGap = 3f;
        private static float OrderButtonWidth => (ContentWidth - OrderGap * 3f) / 4f;

        private static float AddScopeBar(RectTransform parent, float y)
        {
            WingUi.TacticalCard(parent, new Rect(Pad, y, ContentWidth, ScopeBarHeight), WingUi.RailCyan);
            summaryLabel = Label(parent, "",
                                 new Rect(Pad + Space2, y, 180f, ScopeBarHeight),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            summaryLabel.enableWordWrapping = false;
            summaryLabel.overflowMode = TextOverflowModes.Ellipsis;

            float x = Pad + 200f;
            pair12Button = WingUi.Button(parent, "PAIR 1-2",
                          new Rect(x, y + 3f, 84f, ScopeBarHeight - 6f),
                          FontMicro, UiButtonStyle.Default,
                          () => WingCommandManager.Instance?.SelectPair(1, 2))
                .WithTooltip("Select lead element: slots 1 and 2");
            pair34Button = WingUi.Button(parent, "PAIR 3-4",
                          new Rect(x + 88f, y + 3f, 84f, ScopeBarHeight - 6f),
                          FontMicro, UiButtonStyle.Default,
                          () => WingCommandManager.Instance?.SelectPair(3, 4))
                .WithTooltip("Select second element: slots 3 and 4");
            allButton = WingUi.Button(parent, "ALL",
                          new Rect(x + 176f, y + 3f, Pad + ContentWidth - (x + 176f), ScopeBarHeight - 6f),
                          FontMicro, UiButtonStyle.Primary,
                          () => WingCommandManager.Instance?.SelectAllMembers())
                .WithTooltip(OrderHint.SelectAll);
            return y - ScopeBarHeight - TacticalGap;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            rosterHeaderLabel = Label(parent, "",
                                      new Rect(Pad, y, 150f, RosterHeadHeight),
                                      Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            rosterExpandButton = WingUi.Button(parent, "4/6",
                                  new Rect(Pad + 154f, y + 1f, 56f, RosterHeadHeight - 2f),
                                  FontMicro, UiButtonStyle.Quiet, ToggleRosterExpanded)
                .WithTooltip("Show four or six aircraft per page. Selection is preserved.");

            const float pagerW = 26f + TacticalGap + 56f + TacticalGap + 26f;
            float pagerX = Pad + ContentWidth - pagerW;
            rosterPrevButton = WingUi.Button(parent, "<",
                                  new Rect(pagerX, y + 1f, 26f, RosterHeadHeight - 2f),
                                  FontMicro, UiButtonStyle.Quiet, () => TurnRosterPage(-1))
                .WithTooltip("Previous flight page");
            rosterPageLabel = Label(parent, "",
                                    new Rect(pagerX + 30f, y, 56f, RosterHeadHeight),
                                    Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            rosterNextButton = WingUi.Button(parent, ">",
                                  new Rect(pagerX + 90f, y + 1f, 26f, RosterHeadHeight - 2f),
                                  FontMicro, UiButtonStyle.Quiet, () => TurnRosterPage(1))
                .WithTooltip("Next flight page");
            y -= RosterHeadHeight + TacticalGap;

            float h = TacticalRosterPitch * RosterRowsPerPage;
            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, ContentWidth, h));

            var (cardFill, cardRail) = WingUi.TacticalCard(rosterArea, new Rect(0f, 0f, ContentWidth, h), WingUi.RailCyan);
            if (cardRail != null) cardRail.color = WingUi.RailInert;
            rosterEmptyCard = cardFill.rectTransform;
            WingUi.CornerTicks(rosterEmptyCard, new Rect(4f, -4f, ContentWidth - 8f, h - 8f), WingUi.RailInert, 7f);
            WingUi.HitButton(rosterEmptyCard, new Rect(0f, 0f, ContentWidth, h), () => SetPage(Page.Supply))
                .WithTooltip("Go to SUPPLY to recruit pilots and requisition aircraft");
            // Inert card: identity block centred in the row, requisition action to the right.
            float groupTop = -(h - 46f) * 0.5f;
            Glyph(rosterEmptyCard, "airframe", new Rect(14f, -(h - 28f) * 0.5f, 28f, 28f), Dim());
            Label(rosterEmptyCard, "NO WINGMEN",
                  new Rect(52f, groupTop, 200f, 16f),
                  WingUi.TextPrimary, FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            rosterEmptyLabel = Label(rosterEmptyCard, "Map recruit  ·  or open SUPPLY",
                  new Rect(52f, groupTop - 18f, ContentWidth - 160f, 14f),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            for (int slot = 0; slot < 4; slot++)
            {
                float pipX = 52f + slot * 18f;
                float pipY = groupTop - 36f;
                Image pip = Panel(rosterEmptyCard, new Rect(pipX, pipY, 10f, 10f), WingUi.CardFill);
                pip.sprite = AvSprites.Slot;
                pip.type = Image.Type.Sliced;
                Label(rosterEmptyCard, (slot + 1).ToString(), new Rect(pipX, pipY, 10f, 10f),
                      Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            }
            WingUi.Button(rosterEmptyCard, "OPEN SUPPLY",
                          new Rect(ContentWidth - 100f, -(h - 22f) * 0.5f, 88f, 22f),
                          FontMicro, UiButtonStyle.Primary, () => SetPage(Page.Supply))
                .WithTooltip("Open the SUPPLY tab to requisition aircraft.");

            return y - h - TacticalGap;
        }

        /// <summary>Wing-wide doctrine plate, then the selected aircraft's weapon preference.</summary>
        private static float AddEngagementSection(RectTransform parent, float y)
        {
            const float rowH = 24f;
            const float presetH = 26f;
            const float gap = 3f;
            const float pad = 3f;
            const float head = 12f;
            float plateH = pad + head + gap + presetH + gap + 5f * (rowH + gap) + 2f;
            WingUi.TacticalCard(parent, new Rect(Pad, y, ContentWidth, plateH), WingUi.RailCyan);

            float rowY = y - pad;
            Label(parent, "DOCTRINE", new Rect(Pad + 8f, rowY, 140f, head),
                  Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Label(parent, "WING", new Rect(Pad, rowY, ContentWidth - 8f, head),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            rowY -= head + gap;

            rowY = SegmentRow(parent, rowY, presetH, new[] { "RESERVE", "ESCORT", "SWEEP" });
            reserveButton = LastSegment(0);
            escortButton = LastSegment(1);
            sweepButton = LastSegment(2);
            WireSegment(reserveButton, () => SetDoctrine(WingDoctrine.Reserve));
            WireSegment(escortButton, () => SetDoctrine(WingDoctrine.Escort));
            WireSegment(sweepButton, () => SetDoctrine(WingDoctrine.Sweep));

            rowY = LabeledRow(parent, rowY, rowH, "MISSILES", new[] { "OFF", "SELF", "WING", "LEAD" }, guardButtons,
                index => SetGuard((MissileGuard)index));
            rowY = LabeledRow(parent, rowY, rowH, "RESPONSE", new[] { "BREAK", "PRESS" }, null, null);
            breakButton = LastSegment(0);
            pressButton = LastSegment(1);
            WireSegment(breakButton, () => SetResponse(MissileResponse.Break));
            WireSegment(pressButton, () => SetResponse(MissileResponse.Press));

            rowY = LabeledRow(parent, rowY, rowH, "INTERVAL", new[] { "CLOSE", "STD", "OPEN", "SPREAD" }, null, null);
            for (int i = 0; i < 3; i++) intervalButtons[i] = LastSegment(i);
            spreadButton = LastSegment(3);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                WireSegment(intervalButtons[i], () => SetInterval((FormationInterval)index));
            }
            WireSegment(spreadButton, () => SetSpread(!(Wing()?.Doctrine.SpreadWhenThreatened ?? true)));

            rowY = LabeledRow(parent, rowY, rowH, "TARGETS",
                new[] { "HOLD FIRE", "AIR", "GROUND", "BOTH", "COVER" }, targetButtons,
                index => SetTargets((TargetPolicy)index));
            LabeledRow(parent, rowY, rowH, "REACH", new[] { "SLOT 6 KM", "LONG 12 KM" }, null, null);
            slotReachButton = LastSegment(0);
            longReachButton = LastSegment(1);
            WireSegment(slotReachButton, () => SetReach(EngagementReach.Slot));
            WireSegment(longReachButton, () => SetReach(EngagementReach.Long));

            y -= plateH + TacticalGap;
            y = LabeledRow(parent, y, rowH, "WEAPONS", new[] { "AUTO", "A-A", "A-G", "GUNS" }, preferenceButtons,
                index => WingCommandManager.Instance?.SetWeaponPreference(WingWeaponPreferences.All[index]));
            for (int i = 0; i < preferenceButtons.Length; i++)
            {
                WingWeaponPreference preference = WingWeaponPreferences.All[i];
                preferenceButtons[i]?.WithTooltip("WEAPON " + WingWeaponPreferences.Label(preference) + " — " +
                    WingWeaponPreferences.Hint(preference) + " Selected aircraft only.");
            }
            return y;
        }

        private static WingButton[] segmentScratch = new WingButton[6];

        private static float LabeledRow(RectTransform parent, float y, float rowH, string caption,
            string[] names, WingButton[] dest, Action<int> click)
        {
            Label(parent, caption, new Rect(Pad + 6f, y, 66f, rowH), Dim(), FontMicro,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            float rowY = y;
            float trackX = Pad + 74f;
            float gap = 2f;
            float trackW = ContentWidth - 80f;
            Image track = Panel(parent, new Rect(trackX, rowY, trackW, rowH), WingUi.CardFill);
            track.sprite = AvSprites.Slot;
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            float width = (trackW - gap * (names.Length - 1) - 4f) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                WingButton button = DoctrineButton(parent, names[i],
                    new Rect(trackX + 2f + i * (width + gap), rowY + 2f, width, rowH - 4f),
                    click == null ? (Action)null : () => click(index),
                    caption + "  " + names[i]);
                if (dest != null && i < dest.Length) dest[i] = button;
                if (i < segmentScratch.Length) segmentScratch[i] = button;
            }
            for (int i = names.Length; i < segmentScratch.Length; i++) segmentScratch[i] = null;
            return rowY - rowH - 3f;
        }

        private static float SegmentRow(RectTransform parent, float y, float rowH, string[] names)
        {
            float x = Pad + 6f;
            float trackW = ContentWidth - 12f;
            float gap = 2f;
            Image track = Panel(parent, new Rect(x, y, trackW, rowH), WingUi.CardFill);
            track.sprite = AvSprites.Slot;
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            float width = (trackW - gap * (names.Length - 1) - 4f) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                segmentScratch[i] = DoctrineButton(parent, names[i],
                    new Rect(x + 2f + i * (width + gap), y + 2f, width, rowH - 4f), null, names[i]);
            }
            for (int i = names.Length; i < segmentScratch.Length; i++) segmentScratch[i] = null;
            return y - rowH - 3f;
        }

        private static WingButton LastSegment(int index) =>
            index >= 0 && index < segmentScratch.Length ? segmentScratch[index] : null;

        private static void WireSegment(WingButton button, Action action)
        {
            if (button == null || action == null) return;
            button.SetAction(action);
        }

        private static WingButton DoctrineButton(RectTransform parent, string title, Rect rect,
            Action action, string tooltip)
        {
            WingButton button = WingUi.Button(parent, title, rect, FontMicro, UiButtonStyle.Toggle, action)
                .WithTooltip(tooltip);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            return button;
        }

        /// <summary>Icon and accent rail on a compact key, matching the tactical order buttons.</summary>
        private static void StampKey(WingButton button, string icon, Color color, float height)
        {
            if (button == null) return;
            var rt = (RectTransform)button.transform;
            Rule(rt, new Rect(0f, -3f, 3f, Mathf.Max(8f, height - 6f)), color);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(20f, 0f, 4f, 0f);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            Glyph(rt, icon, new Rect(5f, -(height - 12f) * 0.5f, 12f, 12f), color);
        }

        private static void Glyph(RectTransform parent, string key, Rect rect, Color color)
        {
            Sprite sprite = IconFactory.Get(key);
            if (sprite == null || parent == null) return;
            var iconGo = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.SetParent(parent, false);
            Place(iconRt, rect);
            Image image = iconGo.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static WingButton Directive(RectTransform parent, int column, float y, string title, string icon,
            Color rail, Action action, UiButtonStyle style, string tooltip)
        {
            float x = Pad + column * (OrderButtonWidth + OrderGap);
            WingButton button = WingUi.Button(parent, title,
                new Rect(x, y, OrderButtonWidth, CueButtonHeight),
                FontMicro, style == UiButtonStyle.Toggle ? UiButtonStyle.Toggle : UiButtonStyle.Default, action);
            Rule((RectTransform)button.transform, new Rect(0f, -3f, 3f, CueButtonHeight - 6f), rail);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(20f, 0f, 3f, 0f);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            Glyph((RectTransform)button.transform, icon, new Rect(6f, -5f, 12f, 12f), rail);
            button.WithTooltip(tooltip);
            return button;
        }

        /// <summary>Grouped command deck with explicit map-action cues and the combat bento beneath.</summary>
        private static float AddDirectivesDeck(RectTransform parent, float y)
        {
            y = AddEngagementSection(parent, y);

            attackButton = Directive(parent, 0, y, "ATTACK", "attack", WingUi.RailCyan,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.Attack),
                UiButtonStyle.Toggle, OrderHint.Attack);
            splashButton = Directive(parent, 1, y, "SPLASH", "disc", WingUi.RailCyan,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.FireForEffect),
                UiButtonStyle.Toggle, OrderHint.FireForEffect);
            Directive(parent, 2, y, "ENGAGE", "engage", WingUi.RailCyan,
                () => Order(WingAction.Engage),
                UiButtonStyle.Default, OrderHint.Engage);
            seekAndDestroyButton = Directive(parent, 3, y, "SEEK", "tasking", WingUi.RailCyan,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.SeekAndDestroy),
                UiButtonStyle.Toggle, OrderHint.SeekAndDestroy);
            y -= CueButtonHeight + TacticalGap;

            Directive(parent, 0, y, "FORM UP", "rejoin", WingUi.Warning,
                () => Order(WingAction.Rejoin),
                UiButtonStyle.Default, OrderHint.Rejoin);
            Directive(parent, 1, y, "BREAK", "fallback", WingUi.Warning,
                () => Order(WingAction.FallBack),
                UiButtonStyle.Default, OrderHint.Disengage);
            holdHereButton = Directive(parent, 2, y, "ORBIT", "orbit", WingUi.Warning,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.OrbitHere),
                UiButtonStyle.Toggle, OrderHint.HoldHere);
            jamButton = Directive(parent, 3, y, "JAM", "jam", WingUi.Warning,
                () => Order(WingAction.JamMyTarget),
                UiButtonStyle.Default, OrderHint.Jam);
            y -= CueButtonHeight + TacticalGap;

            Directive(parent, 0, y, "REFIT", "airframe", WingUi.RailEmerald,
                () => Order(WingAction.Refit),
                UiButtonStyle.Default,
                "REFIT - land at a base, refill fuel and ammunition, then resume this task and route. A new order cancels the saved task.");
            Directive(parent, 1, y, "LOITER", "posture", WingUi.RailEmerald,
                () => Order(WingAction.StandDown),
                UiButtonStyle.Default, OrderHint.StandDown);
            cargoButton = Directive(parent, 2, y, "CARGO", "cargo", WingUi.RailEmerald,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.DeliverCargo),
                UiButtonStyle.Toggle, OrderHint.DeliverCargo);
            landButton = Directive(parent, 3, y, "LAND", "land", WingUi.RailEmerald,
                () => WingCommandManager.Instance?.SelectMapOrder(WingOrder.LandHere),
                UiButtonStyle.Toggle, OrderHint.LandHere);
            y -= CueButtonHeight + TacticalGap;

            return AddTacticalBento(parent, y);
        }

        private static WingButton[] formationButtons;

        /// <summary>Formation visualizer, whole-wing shape grid, and scoped maneuvers.</summary>
        private static float AddGeometryDeck(RectTransform parent, float y)
        {
            y = AddTacticalPreview(parent, y, TacticalPreviewHeight);

            y = MicroHead(parent, y, "FORMATION", "WING");
            const int columns = 5;
            float w = (ContentWidth - TacticalGap * (columns - 1)) / columns;
            formationButtons = new WingButton[FormationShapes.All.Length];
            for (int i = 0; i < FormationShapes.All.Length; i++)
            {
                FormationShape shape = FormationShapes.All[i];
                string label = ShapeLabel(shape);
                formationButtons[i] = WingUi.Button(parent, label,
                    new Rect(Pad + (i % columns) * (w + TacticalGap),
                             y - (i / columns) * (GeometryRow + TacticalGap), w, GeometryRow),
                    FontMicro, UiButtonStyle.Toggle, () => SetFormationShape(shape))
                    .WithTooltip(FormationShapes.Pretty(shape) + " — " + FormationShapes.Role(shape));
                TMP_Text shapeText = formationButtons[i].GetComponentInChildren<TMP_Text>();
                if (shapeText != null)
                {
                    shapeText.alignment = TextAlignmentOptions.MidlineLeft;
                    shapeText.margin = new Vector4(18f, 0f, 2f, 0f);
                    shapeText.enableWordWrapping = false;
                    shapeText.overflowMode = TextOverflowModes.Ellipsis;
                }
                Glyph((RectTransform)formationButtons[i].transform, "shape_" + shape,
                    new Rect(3f, -4f, 12f, 12f), WingUi.RailEmerald);
            }
            y -= GeometryRow * 2f + TacticalGap + TacticalGap;

            y = MicroHead(parent, y, "MANEUVERS", "SELECTED");
            for (int i = 0; i < TacticalManeuvers.Length; i++)
            {
                ManeuverKind kind = TacticalManeuvers[i];
                maneuverButtons[i] = WingUi.Button(parent, ManeuverCatalog.ShortLabel(kind),
                    new Rect(Pad + (i % columns) * (w + TacticalGap),
                             y - (i / columns) * (GeometryRow + TacticalGap), w, GeometryRow),
                    FontMicro, UiButtonStyle.Default,
                    () => WingCommandManager.Instance?.ExecuteManeuver(kind, wholeWing: false))
                    .WithTooltip(ManeuverCatalog.Label(kind) + " — selected wingmen; needs " +
                        ManeuverCatalog.MinEntryAltitudeAgl(kind).ToString("0") + " m AGL and safe entry speed.");
                Glyph((RectTransform)maneuverButtons[i].transform, "maneuver",
                    new Rect(3f, -4f, 12f, 12f), WingUi.Warning);
                TMP_Text maneuverText = maneuverButtons[i].GetComponentInChildren<TMP_Text>();
                if (maneuverText != null)
                {
                    maneuverText.alignment = TextAlignmentOptions.MidlineLeft;
                    maneuverText.margin = new Vector4(18f, 0f, 2f, 0f);
                    maneuverText.enableWordWrapping = false;
                }
            }
            y -= GeometryRow * 2f + TacticalGap;
            return y - 2f;
        }

        private static float MicroHead(RectTransform parent, float y, string title, string note)
        {
            Rule(parent, new Rect(Pad, y - 2f, 3f, 8f), Green());
            Label(parent, title, new Rect(Pad + 8f, y, 160f, GeometryHead),
                  Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            if (!string.IsNullOrEmpty(note))
                Label(parent, note, new Rect(Pad, y, ContentWidth, GeometryHead),
                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            return y - GeometryHead;
        }

        private static string ShapeLabel(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "ECH R";
                case FormationShape.EchelonLeft: return "ECH L";
                case FormationShape.LineAbreast: return "ABREAST";
                case FormationShape.CombatSpread: return "SPREAD";
                case FormationShape.FingerFour: return "FINGER";
                default: return shape.ToString().ToUpperInvariant();
            }
        }

        private static void SetFormationShape(FormationShape shape)
        {
            WingFormation.Shape = shape;
            WingCommandManager manager = WingCommandManager.Instance;
            if (manager != null)
            {
                WingRegistry wing = manager.Wing;
                if (wing != null) RefreshTactical(wing);
            }
        }

        /// <summary>Shared two-line order help explaining scope and distinctions that compact button
        /// labels cannot carry.</summary>
        private static class OrderHint
        {
            public const string Rejoin =
                "FORM UP - break off and return to formation on the leader. Cancels any " +
                "attack, hold or route the selected wingmen are flying.";

            public const string Attack =
                "ATTACK TARGET - if you have contacts designated, sends the selection after " +
                "them immediately. The button stays armed: left-click a hostile on the map " +
                "to focus that target instead. Shift-left-click queues another.";

            public const string FireForEffect =
                "SPLASH - priority saturation of all designated targets. Immediately commits every " +
                "in-range weapon and empties those stores at native firing speed. Only critical " +
                "self-preservation pauses the salvo; it resumes automatically. No short-range run-in.";

            public const string Engage =
                "ENGAGE - the selected aircraft hunts on its own. The rest of the wing keeps " +
                "the doctrine on this panel. It does not come back until told to.";

            public const string SeekAndDestroy =
                "SEEK & DESTROY - then left-click the map. The selection flies to that " +
                "point, then begins hunting on its own. The wing doctrine stays as this panel shows it. " +
                "Shift-left-click queues another.";

            public const string Disengage =
                "DISENGAGE - break contact and run for the nearest friendly base or ship, " +
                "defending itself on the way. Not a landing order.";

            public const string HoldHere =
                "HOLD HERE - then left-click the map. The selection orbits that point and " +
                "defends itself, but starts nothing. Shift-left-click queues the next order.";

            public const string DeliverCargo =
                "DELIVER CARGO - then left-click a drop point, or press again to use the " +
                "stock supply route. Shift-left-click queues another drop. Only wingmen " +
                "actually carrying a load can take this.";

            public const string LandHere =
                "LAND HERE - then left-click the map. Puts a rotary wingman on the ground " +
                "at that spot rather than routing it to an airbase. Shift-left-click queues.";

            public const string StandDown =
                "STAND DOWN - cancel the current task and loiter near the nearest friendly " +
                "airbase or ship. Does not land and does not rejoin until ordered.";

            public const string HeightUp =
                "HEIGHT + - raise Move altitude. Applies to the next map Move and to " +
                "selected wingmen already moving.";

            public const string HeightDown =
                "HEIGHT - - lower Move altitude. Applies to the next map Move and to " +
                "selected wingmen already moving.";

            public const string SpeedUp =
                "SPEED + - raise Move speed. Applies to the next map Move and to selected " +
                "wingmen already moving.";

            public const string SpeedDown =
                "SPEED - - lower Move speed. Applies to the next map Move and to selected " +
                "wingmen already moving.";

            public const string SelectAll =
                "Put every wingman in the command scope, so the next order goes to the " +
                "whole flight.";

            public const string ReturnToBase =
                "RTB - dismiss this wingman from the active flight. It flies home; after " +
                "recovery, both its airframe and pilot return to their pools. Press twice " +
                "to confirm.";

            public const string Roe =
                "Rules of engagement, wing-wide: how far a wingman may go on its own " +
                "initiative before it needs telling.";

            public const string Weapon =
                "Which weapons the selected wingmen reach for first. Scoped, so a mixed " +
                "flight can split between the air and the ground.";

            public const string Requisition =
                "Buy the selected airframe. It launches from a friendly base with the fit " +
                "chosen on LOADOUT and flies out to join the wing.";

            public const string Fit =
                "What the next one of these launches with: your current player default for " +
                "this airframe, or one of the templates you have built for it on LOADOUT.";

            public const string OverLimit =
                "Permission to requisition past the mission's AI aircraft cap, at a " +
                "surcharge. Changes nothing while the squadron still has room.";

            public const string FullFuel =
                "Fuel each requisition launches with, as a share of full tanks - steps " +
                "25 / 50 / 75 / 100%. Less fuel is lighter and more agile but calls bingo " +
                "sooner; 100% is completely full even for an airframe that ships short.";

            public const string AssignSelected =
                "Conscript the friendly AI aircraft selected on the map into your wing. " +
                "Press twice to confirm the fee.";

            public const string Jam =
                "JAM - the selected wingmen hold their formation slot and run their jammer " +
                "pod against the target you have locked, until it dies or you order them " +
                "off. Only wingmen carrying a jammer pod can take it.";

            public const string Pager = "Show the rest of the list.";
        }

        private static void SetDoctrine(WingDoctrine doctrine)
        {
            WingRegistry wing = Wing();
            if (wing == null) return;
            wing.Doctrine = doctrine;
            WingCommandManager.Instance?.Toast(doctrine.PatternName);
        }

        private static void SetGuard(MissileGuard guard)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            SetDoctrine(new WingDoctrine(guard, current.Response, current.Interval,
                current.SpreadWhenThreatened, current.Targets, current.Reach));
        }

        private static void SetResponse(MissileResponse response)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            if (current.Guard == MissileGuard.Off) response = MissileResponse.Break;
            SetDoctrine(new WingDoctrine(current.Guard, response, current.Interval,
                current.SpreadWhenThreatened, current.Targets, current.Reach));
        }

        private static void SetInterval(FormationInterval interval)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            SetDoctrine(new WingDoctrine(current.Guard, current.Response, interval,
                current.SpreadWhenThreatened, current.Targets, current.Reach));
        }

        private static void SetSpread(bool spread)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            SetDoctrine(new WingDoctrine(current.Guard, current.Response, current.Interval,
                spread, current.Targets, current.Reach));
        }

        private static void SetTargets(TargetPolicy targets)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            SetDoctrine(new WingDoctrine(current.Guard, current.Response, current.Interval,
                current.SpreadWhenThreatened, targets, current.Reach));
        }

        private static void SetReach(EngagementReach reach)
        {
            WingDoctrine current = Wing()?.Doctrine ?? WingDoctrine.Reserve;
            SetDoctrine(new WingDoctrine(current.Guard, current.Response, current.Interval,
                current.SpreadWhenThreatened, current.Targets, reach));
        }

        private static void Order(WingAction action) =>
            WingCommandManager.Instance?.Execute(action, wholeWing: false);

        private static void TurnRosterPage(int direction)
        {
            rosterPage = Mathf.Max(0, rosterPage + direction);
            WingRegistry wing = Wing();
            if (wing != null) RefreshTactical(wing);
        }

        private static void RefreshTactical(WingRegistry wing)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            WingWeaponPreference? shared = manager != null ? manager.ScopeWeaponPreference() : null;
            int totalCount = wing.Count + EconomyFacade.ShopDelivery.PendingCount;
            if (summaryLabel != null)
            {
                if (totalCount == 0)
                {
                    summaryLabel.text = "COMMAND · NO WING";
                    summaryLabel.color = Dim();
                }
                else
                {
                    bool weaponMixed = shared == null && manager != null &&
                        manager.Commands.Scope(wholeWing: false).Count > 1;
                    summaryLabel.text = "COMMAND · " + (manager?.Selection.Summary(wing) ?? "ALL") +
                        (weaponMixed ? " · MIXED" : "");
                    summaryLabel.color = Friendly();
                }
            }

            pair12Button?.SetEnabled(totalCount >= 1);
            pair34Button?.SetEnabled(totalCount >= 3);
            allButton?.SetEnabled(totalCount > 0);

            WingDoctrine doctrine = wing.Doctrine;
            reserveButton?.SetLatched(doctrine.Equals(WingDoctrine.Reserve));
            escortButton?.SetLatched(doctrine.Equals(WingDoctrine.Escort));
            sweepButton?.SetLatched(doctrine.Equals(WingDoctrine.Sweep));
            LatchGuard(doctrine);
            breakButton?.SetLatched(doctrine.Response == MissileResponse.Break);
            pressButton?.SetLatched(doctrine.Response == MissileResponse.Press);
            pressButton?.SetEnabled(doctrine.Guard != MissileGuard.Off);
            LatchInterval(doctrine);
            spreadButton?.SetLatched(doctrine.SpreadWhenThreatened);
            LatchTargets(doctrine);
            slotReachButton?.SetLatched(doctrine.Reach == EngagementReach.Slot);
            longReachButton?.SetLatched(doctrine.Reach == EngagementReach.Long);

            // Leave all preference buttons unlit for mixed scope values.
            for (int i = 0; i < preferenceButtons.Length; i++)
                preferenceButtons[i]?.SetLatched(shared == WingWeaponPreferences.All[i]);

            if (manager != null)
            {
                List<WingMember> scope = manager.Commands.Scope(wholeWing: false);
                bool canCargo = false;
                bool canLand = false;
                bool canJam = false;
                bool canSeekAndDestroy = false;
                foreach (WingMember member in scope)
                {
                    canCargo |= WingOrderCatalog.CanApply(member, WingOrder.DeliverCargo);
                    canLand |= WingOrderCatalog.CanApply(member, WingOrder.LandHere);
                    canJam |= WingOrderCatalog.CanApply(member, WingOrder.JamTarget);
                    canSeekAndDestroy |= WingOrderCatalog.CanApply(member, WingOrder.SeekAndDestroy);
                }
                cargoButton?.SetEnabled(canCargo);
                // LAND follows any scoped member that can land, not the wing leader's airframe.
                landButton?.SetEnabled(canLand);
                seekAndDestroyButton?.SetEnabled(canSeekAndDestroy);
                jamButton?.SetEnabled(canJam);

                bool armed = manager.MapOrderArmed;
                WingOrder armedOrder = manager.ArmedMapOrder;
                attackButton?.SetLatched(armed && armedOrder == WingOrder.Attack);
                splashButton?.SetLatched(armed && armedOrder == WingOrder.FireForEffect);
                holdHereButton?.SetLatched(armed && armedOrder == WingOrder.OrbitHere);
                seekAndDestroyButton?.SetLatched(armed && armedOrder == WingOrder.SeekAndDestroy);
                cargoButton?.SetLatched(armed && armedOrder == WingOrder.DeliverCargo);
                landButton?.SetLatched(armed && armedOrder == WingOrder.LandHere);
            }

            RefreshOrdersCue(wing, manager);

            // Status priority is hover help, live map instruction, then standing engagement hints.
            RefreshStatusStrip(Page.Tactical,
                manager != null && manager.MapStatusIsNotice
                    ? manager.MapStatus
                    : EngagementHint(wing, shared));

            RefreshTacticalNavigation(wing);
            RefreshRoster(wing);
            RefreshFlightGeometry();
            RefreshTacticalBento(wing, manager?.Commands.Scope(wholeWing: false));
        }

        /// <summary>Map cue banner: the armed order and required input, or the idle click grammar.</summary>
        private static void RefreshOrdersCue(WingRegistry wing, WingCommandManager manager)
        {
            if (ordersCueLabel == null) return;

            if (manager != null && manager.MapOrderArmed)
            {
                WingOrder order = manager.ArmedMapOrder;
                ordersCueLabel.text = "ARMED " + WingOrderCatalog.ShortLabel(order) +
                    " · " + ArmedInputHint(order);
                ordersCueLabel.color = Warning();
                if (ordersCueRail != null) ordersCueRail.color = Warning();
                return;
            }

            string scope = manager != null ? manager.Selection.Summary(wing) : "NO WING";
            ordersCueLabel.text = "SCOPE " + scope + " · LEFT-CLICK = ORDER · SHIFT = QUEUE";
            ordersCueLabel.color = Friendly();
            if (ordersCueRail != null) ordersCueRail.color = WingUi.RailCyan;
        }

        private static string ArmedInputHint(WingOrder order)
        {
            if (order == WingOrder.DeliverCargo) return "LEFT-CLICK DROP POINT OR PRESS AGAIN";
            if (WingOrderCatalog.NeedsPoint(order)) return "LEFT-CLICK MAP POINT";
            return "LEFT-CLICK TARGET · SHIFT QUEUES";
        }

        private static void RefreshFlightGeometry()
        {
            FormationShape shape = WingFormation.Shape;

            if (formationButtons != null)
            {
                for (int i = 0; i < formationButtons.Length; i++)
                {
                    if (formationButtons[i] != null && i < FormationShapes.All.Length)
                    {
                        formationButtons[i].SetLatched(FormationShapes.All[i] == shape);
                    }
                }
            }

            RefreshTacticalPreview();
        }

        private static void LatchGuard(WingDoctrine doctrine)
        {
            for (int i = 0; i < guardButtons.Length; i++)
                guardButtons[i]?.SetLatched((int)doctrine.Guard == i);
        }

        private static void LatchInterval(WingDoctrine doctrine)
        {
            for (int i = 0; i < intervalButtons.Length; i++)
                intervalButtons[i]?.SetLatched((int)doctrine.Interval == i);
        }

        private static void LatchTargets(WingDoctrine doctrine)
        {
            for (int i = 0; i < targetButtons.Length; i++)
                targetButtons[i]?.SetLatched((int)doctrine.Targets == i);
        }

        private static string DoctrineHint(WingDoctrine doctrine)
        {
            string targets = doctrine.Targets == TargetPolicy.Hold ? "HOLD FIRE" : doctrine.Targets.ToString().ToUpperInvariant();
            string spread = doctrine.SpreadWhenThreatened ? "SPREAD" : "HOLD INTERVAL";
            return doctrine.PatternName + " · MISSILES " + doctrine.Guard.ToString().ToUpperInvariant() +
                " " + doctrine.Response.ToString().ToUpperInvariant() + " · " +
                doctrine.Interval.ToString().ToUpperInvariant() + " " + spread + " · " + targets;
        }

        /// <summary>Summarise doctrine first, adding weapon preference only when non-Auto.</summary>
        private static string EngagementHint(WingRegistry wing, WingWeaponPreference? shared)
        {
            string hint = DoctrineHint(wing.Doctrine);

            if (shared == null) return hint + "  ·  Weapon preference varies across the selection.";
            if (shared.Value == WingWeaponPreference.Auto) return hint;

            return hint + "  ·  " + WingWeaponPreferences.Hint(shared.Value);
        }

        private static void RefreshRoster(WingRegistry wing)
        {
            int pendingCount = EconomyFacade.ShopDelivery.PendingCount;
            int totalCount = wing.Count + pendingCount;
            bool empty = totalCount == 0;
            if (rosterEmptyCard != null && rosterEmptyCard.gameObject.activeSelf != empty)
                rosterEmptyCard.gameObject.SetActive(empty);

            int visibleRows = rosterExpanded ? ExpandedRosterRows : RosterRowsPerPage;
            int pages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)visibleRows));
            rosterPage = Mathf.Clamp(rosterPage, 0, pages - 1);
            int first = rosterPage * visibleRows;
            int shown = Mathf.Clamp(totalCount - first, 0, visibleRows);
            if (rosterHeaderLabel != null)
                rosterHeaderLabel.text = empty ? "FLIGHT" : "FLIGHT  " + shown + " SHOWN";
            if (rosterEmptyLabel != null)
                rosterEmptyLabel.text = "Map recruit  ·  or open SUPPLY";
            if (rosterPageLabel != null)
                rosterPageLabel.text = empty ? "—" : (rosterPage + 1) + "/" + pages;
            rosterExpandButton?.SetLatched(rosterExpanded);
            rosterExpandButton?.SetEnabled(!empty);

            rosterPrevButton?.SetEnabled(!empty && rosterPage > 0);
            rosterNextButton?.SetEnabled(!empty && rosterPage < pages - 1);

            SyncRosterRows(visibleRows);
            for (int i = 0; i < rosterRows.Count; i++)
            {
                int index = first + i;
                if (i >= visibleRows || empty) { rosterRows[i].Hide(); continue; }
                if (index < wing.Count)
                {
                    rosterRows[i].Bind(wing.Members[index]);
                }
                else if (index < totalCount)
                {
                    rosterRows[i].BindPending(EconomyFacade.ShopDelivery.GetPending(index - wing.Count), index + 1);
                }
                else
                {
                    rosterRows[i].Hide();
                }
            }

            ReflowTactical();
        }

        /// <summary>Retain inspection focus only while its pilot remains on the roster.</summary>
        private static void PruneFocus(WingRegistry wing)
        {
            _ = wing;

            if (PersonnelFacade.Roster.Contains(inspectPilot)) return;

            // After removal, prefer the next-flight pilot, then the most senior available.
            inspectPilot = PersonnelFacade.Roster.Selected;
            if (inspectPilot != null && PersonnelFacade.Roster.Contains(inspectPilot)) return;

            List<WingPilot> roster = PersonnelFacade.Roster.DisplayRoster();
            inspectPilot = roster.Count > 0 ? roster[0] : null;
        }

        private static void SyncRosterRows(int needed)
        {
            while (rosterRows.Count < needed && rosterArea != null)
            {
                int index = rosterRows.Count;
                rosterRows.Add(new RosterRow(rosterArea, index));
            }
        }

        /// <summary>Active-aircraft row with selection marker, identity, state, and scoped actions.</summary>
        private sealed class RosterRow
        {
            private const float ActionHeight = 22f;
            private const float LeadWidth = 26f;
            private const float ReleaseWidth = 44f;
            private const float RadarWidth = 44f;
            private const float EjectWidth = 26f;

            private readonly GameObject go;
            private readonly TMP_Text slot, plane, name, order;
            private readonly Image selectionRule;
            private readonly Image slotMarker;
            private readonly Image[] slotMarkerOutline;
            private readonly Image aircraftIcon;
            private readonly Image fill;
            private readonly WingButton hit;
            private readonly WingButton lead;
            private readonly WingButton release;
            private readonly WingButton radar, eject;
            private readonly Confirmation ejection = new Confirmation();
            private WingMember bound;
            private WingShopDelivery.PendingDelivery boundPending;

            /// <summary>Shared RTB confirmation across rows; arming one member disarms the previous
            /// member.</summary>
            private static readonly Confirmation memberRelease = new Confirmation();
            private static readonly Confirmation pendingRelease = new Confirmation();

            /// <summary>Clear pending member-release confirmation at mission end.</summary>
            public static void Disarm()
            {
                memberRelease.Clear();
                pendingRelease.Clear();
                foreach (RosterRow row in rosterRows) row.ejection.Clear();
            }

            public RosterRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * TacticalRosterPitch;

                go = new GameObject("Row" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, TacticalRosterHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, TacticalRosterHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, TacticalRosterHeight), WingColor());

                float btnY = -(TacticalRosterHeight - ActionHeight) * 0.5f;
                float ejectX = width - 4f - EjectWidth;
                float radarX = ejectX - TacticalGap - RadarWidth;
                float releaseX = radarX - TacticalGap - ReleaseWidth;
                float leadX = releaseX - TacticalGap - LeadWidth;

                hit = HitButton(rt, new Rect(0f, 0f, leadX - TacticalGap, TacticalRosterHeight), () =>
                {
                    if (bound != null)
                    {
                        bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                        WingCommandManager.Instance?.SelectMember(bound, toggle);
                    }
                    else if (boundPending != null)
                    {
                        WingCommandManager.Instance?.Toast(
                            boundPending.AirframeName + " is preparing for departure - cannot be ordered until airborne");
                    }
                });

                const float markerY = -(TacticalRosterHeight - 10f) * 0.5f;
                slotMarker = Panel(rt, new Rect(8f, markerY, 10f, 10f), Color.clear);
                slotMarkerOutline = Outline(rt, new Rect(8f, markerY, 10f, 10f), Dim());
                slot = Label(rt, "", new Rect(23f, 0f, 17f, TacticalRosterHeight), Dim(), FontSmall,
                             FontStyles.Bold, TextAlignmentOptions.Left);
                aircraftIcon = AddSprite(rt, "FlightAircraftIcon", IconFactory.Get("airframe"),
                    new Rect(42f, -(TacticalRosterHeight - 14f) * 0.5f, 14f, 14f), WingColor());
                aircraftIcon.preserveAspect = true;
                aircraftIcon.raycastTarget = false;
                plane = Label(rt, "", new Rect(66f, 0f, 48f, TacticalRosterHeight), WingUi.TextPrimary,
                              FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
                name = Label(rt, "", new Rect(116f, 0f, 52f, TacticalRosterHeight), WingColor(),
                             FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(172f, 0f, leadX - TacticalGap - 172f, TacticalRosterHeight), Dim(),
                              FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

                foreach (TMP_Text text in new[] { slot, plane, name, order })
                {
                    text.enableAutoSizing = true;
                    text.fontSizeMin = FontMicro;
                    text.fontSizeMax = FontSmall;
                }

                lead = WingUi.Button(rt, "LD",
                                     new Rect(leadX, btnY, LeadWidth, ActionHeight),
                                     FontMicro, UiButtonStyle.Default, ToggleLead)
                             .WithTooltip("Flight lead - the rest of the wing formates on this " +
                                          "wingman while it takes your orders. Press again to release.");
                release = WingUi.Button(rt, "RTB",
                                        new Rect(releaseX, btnY, ReleaseWidth, ActionHeight),
                                        FontMicro, UiButtonStyle.Danger, ConfirmRelease)
                                .WithTooltip(OrderHint.ReturnToBase);
                radar = WingUi.Button(rt, "RDR --", new Rect(radarX, btnY, RadarWidth, ActionHeight),
                    FontMicro, UiButtonStyle.Toggle, () => WingCommandManager.Instance?.ToggleMemberRadar(bound));
                eject = WingUi.Button(rt, "EJ", new Rect(ejectX, btnY, EjectWidth, ActionHeight),
                    FontMicro, UiButtonStyle.Danger, ConfirmEject)
                    .WithTooltip("EJECT - abandon this aircraft. Select EJ, then EJ? within 3 seconds to confirm.");
            }

            private void ConfirmEject()
            {
                var manager = WingCommandManager.Instance;
                if (manager == null || !manager.CanControlAircraft(bound) || bound.IsSurface)
                {
                    ejection.Clear();
                    return;
                }
                if (ejection.IsArmedFor(bound))
                {
                    ejection.Clear();
                    manager.EjectMember(bound);
                }
                else
                {
                    ejection.Arm(bound);
                    manager.Toast("Eject " + bound.Name + "? Select EJ? again within 3 seconds. Aircraft will be abandoned.");
                }
                eject.SetText(ejection.IsArmedFor(bound) ? "EJ?" : "EJ");
                eject.SetLatched(ejection.IsArmedFor(bound));
            }

            private void ToggleLead()
            {
                if (bound != null) WingCommandManager.Instance?.ToggleFlightLead(bound);
            }

            /// <summary>Arm release first, then confirm the same member's RTB.</summary>
            private void ConfirmRelease()
            {
                if (bound != null)
                {
                    if (memberRelease.IsArmedFor(bound))
                    {
                        WingMember going = bound;
                        memberRelease.Clear();
                        WingCommandManager.Instance?.RemoveMember(going);
                        return;
                    }

                    memberRelease.Arm(bound);
                    WingCommandManager.Instance?.Toast(
                        "Press RTB again to send " + bound.Name + " home from the wing");
                    return;
                }

                if (boundPending != null)
                {
                    if (!boundPending.CanCancel)
                    {
                        pendingRelease.Clear();
                        WingCommandManager.Instance?.Toast(
                            "Launch already accepted; wait for delivery before releasing");
                        return;
                    }
                    if (pendingRelease.IsArmedFor(boundPending))
                    {
                        WingShopDelivery.PendingDelivery going = boundPending;
                        pendingRelease.Clear();
                        EconomyFacade.ShopDelivery.CancelPending(going);
                        return;
                    }

                    pendingRelease.Arm(boundPending);
                    WingCommandManager.Instance?.Toast(
                        "Press CXL again to cancel requisition of " + boundPending.AirframeName);
                }
            }

            public void Bind(WingMember m)
            {
                bool memberChanged = bound != m || boundPending != null;
                if (memberChanged) ejection.Clear();
                bound = m;
                boundPending = null;
                if (!go.activeSelf) go.SetActive(true);
                lead.gameObject.SetActive(true);
                radar.gameObject.SetActive(true);
                eject.gameObject.SetActive(true);

                bool selected = WingCommandManager.Instance?.Selection.Contains(m) ?? true;

                bool armed = memberRelease.IsArmedFor(m);
                release?.SetEnabled(true);
                release?.WithTooltip(OrderHint.ReturnToBase);
                release?.SetLatched(armed);
                release?.SetText(armed ? "RTB?" : "RTB");

                bool canControl = WingCommandManager.Instance?.CanControlAircraft(m) ?? false;
                bool hasRadar = m.Aircraft != null && m.Aircraft.radar != null;
                bool radarOn = hasRadar && m.Aircraft.radar.activated;
                radar.SetEnabled(canControl && hasRadar);
                radar.SetText(hasRadar ? (radarOn ? "RDR ON" : "EMCON") : "RDR --");
                radar.SetLatched(radarOn);
                radar.WithTooltip(!hasRadar ? "No radar fitted" : !canControl ? "Radar control requires a locally controlled AI aircraft" :
                    "RADAR " + (radarOn ? "ACTIVE (transmitting) - click for EMCON silent mode" : "EMCON (silent) - click to transmit"));
                eject.SetEnabled(canControl && !m.IsSurface);
                eject.SetText(ejection.IsArmedFor(m) ? "EJ?" : "EJ");
                eject.SetLatched(ejection.IsArmedFor(m));

                lead?.SetLatched(m.IsFlightLead);
                lead?.SetEnabled(true);
                slot.text = m.Slot.ToString();

                // Use a numeric slot; unsupported circle glyphs render as missing-character boxes.
                if (memberChanged)
                {
                    aircraftIcon.sprite = IconFactory.Aircraft(m.Aircraft?.definition);
                    string planeStr = !string.IsNullOrEmpty(m.Aircraft?.definition?.code)
                        ? m.Aircraft.definition.code
                        : m.Name;
                    plane.text = AvTheme.Truncate(planeStr, 8);
                    string callsignStr = m.Crew != null && !string.IsNullOrEmpty(m.Crew.Callsign)
                        ? m.Crew.Callsign
                        : "AI";
                    name.text = callsignStr;
                    hit.WithTooltip(m.Name + " / " + planeStr + " / " + callsignStr +
                        ". Click to select; Shift-click to add or remove.");
                }
                slot.color = selected ? Green() : Dim();
                aircraftIcon.color = selected ? Friendly() : WingColor();
                plane.color = selected ? Green() : WingColor();
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();
                slotMarker.color = selected ? Green() : Color.clear;
                foreach (Image line in slotMarkerOutline) line.color = selected ? Green() : Dim();

                // Layer hover feedback over persistent row selection.
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
                string orderText = ShortOrder(m);
                order.text = orderText;
                order.color = OrderStatusColor(m, orderText);
            }

            public void BindPending(WingShopDelivery.PendingDelivery p, int slotNumber)
            {
                bool pendingChanged = boundPending != p || bound != null;
                ejection.Clear();
                lead.gameObject.SetActive(false);
                radar.gameObject.SetActive(false);
                eject.gameObject.SetActive(false);
                bound = null;
                boundPending = p;
                if (!go.activeSelf) go.SetActive(true);

                bool canCancel = p.CanCancel;
                if (!canCancel && pendingRelease.IsArmedFor(p)) pendingRelease.Clear();
                bool armed = canCancel && pendingRelease.IsArmedFor(p);
                release?.SetEnabled(canCancel);
                release?.WithTooltip(canCancel ? "CANCEL - cancel this pending requisition. " +
                    "Press once to arm, again to confirm." :
                    "Launch already accepted; wait for delivery before cancelling");
                release?.SetLatched(armed);
                release?.SetText(canCancel ? (armed ? "CXL?" : "CXL") : "DEPT");

                if (pendingChanged)
                {
                    aircraftIcon.sprite = IconFactory.Aircraft(p.Definition);
                    hit.WithTooltip(p.AirframeName + " is preparing for departure. Orders unlock when airborne.");
                    slot.text = slotNumber.ToString();
                    string planeStr = !string.IsNullOrEmpty(p.Definition?.code)
                        ? p.Definition.code
                        : p.AirframeName;
                    plane.text = AvTheme.Truncate(planeStr, 8);
                    name.text = "EN ROUTE";
                }
                slot.color = Dim();
                aircraftIcon.color = Dim();
                plane.color = WingColor();
                name.color = WingColor();
                selectionRule.color = MemberFrameColor();
                slotMarker.color = Color.clear;
                foreach (Image line in slotMarkerOutline) line.color = Dim();

                hit?.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
                order.text = p.StatusCode;
                order.color = Warning();
            }

            private static Color OrderStatusColor(WingMember m, string status)
            {
                if (m == null || string.IsNullOrEmpty(status)) return WingUi.TextPrimary;

                if (status == "DEFENSIVE" || status == "PULL UP" || status == "EVADE")
                    return Alert();

                if (m.RefitPending || status == "REFIT" || status == "RTB")
                    return Warning();

                if (status == "SPLASH" || status.StartsWith("ATK") || status == "JAM")
                    return WingUi.RailCyan;

                if (status == "ENGAGE" || status == "S&D")
                    return Green();

                if (status == "FORM" || status == "REJOIN" || status == "HOLD" || status == "HOLDING" || status == "PATROL")
                    return WingUi.RailEmerald;

                if (m.AssignedTarget != null && !m.AssignedTarget.disabled)
                    return WingUi.RailCyan;

                return WingUi.TextPrimary;
            }

            public void Hide()
            {
                ejection.Clear();
                bound = null;
                boundPending = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

    }
}
