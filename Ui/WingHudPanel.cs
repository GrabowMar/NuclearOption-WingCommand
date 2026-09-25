using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Compact wing strip on the game's HUD canvas (spec §8):
    /// <list type="bullet">
    /// <item>the shape and spacing;</item>
    /// <item>the autopilot annunciator;</item>
    /// <item>one row per wingman: phase (JOIN/SLOT/HOLD/BEHIND), slot error, and the limit that binds it.</item>
    /// </list>
    /// Refreshes at 5 Hz and hides when there is neither a wing nor an engaged autopilot.</summary>
    internal sealed class WingHudPanel : IWingService
    {
        private const float Width = 250f, LineHeight = 16f;

        public string Name => "HUD";

        private RectTransform root;
        private Canvas canvas;
        private Image background;
        private TMP_Text title, autopilot;
        private readonly TMP_Text[] rows = new TMP_Text[FormationCatalog.MaxSlots];
        private float nextRefresh;

        public void Activate() => Reset();

        public void Deactivate() => Reset();

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            WingService wing = WingService.Instance;
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            bool any = (wing != null && wing.Members.Count > 0) || (ap != null && ap.Session.Engaged);
            if (!Plugin.Settings.ShowHud.Value || hud == null || !hud.isActiveAndEnabled || !any)
            {
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            Canvas c = hud.GetComponentInParent<Canvas>();
            if (c == null) return;
            if (root == null || canvas != c)
            {
                Reset();
                Build(hud, c);
            }
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.anchoredPosition = new Vector2(24f + Plugin.Settings.HudX.Value, 180f + Plugin.Settings.HudY.Value);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh(wing, ap);
        }

        private void Build(CombatHUD hud, Canvas c)
        {
            TMP_Text template = hud.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (template != null) WingUi.Font = template.font;
            canvas = c;
            var go = new GameObject("WingCommand_HudPanel", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(c.transform, worldPositionStays: false);
            root.SetAsLastSibling();
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            background = WingUi.Panel(root, new Rect(0f, 0f, Width, LineHeight * 2f), AvTheme.Unity(AvTokens.HudPanel));
            background.raycastTarget = false;
            title = WingUi.Label(root, "", new Rect(6f, -2f, Width - 12f, LineHeight), AvTheme.Accent, AvTokens.FontSmall,
                FontStyles.Bold, TextAlignmentOptions.Left);
            autopilot = WingUi.Label(root, "", new Rect(6f, -2f - LineHeight, Width - 12f, LineHeight), WingUi.TextPrimary,
                AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            for (int i = 0; i < rows.Length; i++)
                rows[i] = WingUi.Label(root, "", new Rect(6f, -2f - LineHeight * (i + 2), Width - 12f, LineHeight),
                    WingUi.TextPrimary, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
        }

        private void Refresh(WingService wing, PlayerAutopilot ap)
        {
            int n = wing != null ? wing.Members.Count : 0;
            FormationSelection sel = wing?.Selection;
            title.text = n > 0 && sel != null
                ? $"WING  {sel.Current.Name.ToUpperInvariant()}  {sel.Spacing.ToString().ToUpperInvariant()}  {wing.Doctrine.PatternName}"
                : "AUTOPILOT";
            autopilot.text = ap != null
                ? WingHudText.Autopilot(ap.Session.Spec, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count)
                : "";
            for (int i = 0; i < rows.Length; i++)
            {
                if (i >= n)
                {
                    rows[i].text = "";
                    continue;
                }
                WingMember m = wing.Members[i];
                bool behind = m.Brain.LastRejoin.FallingBehind;
                string phase = WingHudText.Duty(m.Engaged, m.Recovery != null, m.Recovery != null ? m.Recovery.Intent : RecoveryIntent.Rtb)
                               ?? (m.Settle != null ? WingHudText.Settle(m.Settle.Phase) : null)
                               ?? WingHudText.Phase(m.Brain.Mind.Current, behind);
                string binding = WingHudText.Binding(m.Brain.Pipeline.Report);
                WingFrame f = wing.FrameOf(m);
                float error = f != null && m.Brain.Slot < f.Count ? (f.Slots[m.Brain.Slot].Ref.Pos - m.Last.Pos).Length : 0f;
                rows[i].text = WingHudText.Member(m.Seat, phase, error, binding, WingHudText.BingoTime(m.Bingo.SecondsToBingo));
                rows[i].color = behind || binding == "GCAS" || binding == "COLL" ? AvTheme.Warning : WingUi.TextPrimary;
            }
            float lines = 2 + n;
            background.rectTransform.sizeDelta = new Vector2(Width, LineHeight * lines + 4f);
            root.sizeDelta = new Vector2(Width, LineHeight * lines + 4f);
        }

        private void Reset()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvas = null;
            nextRefresh = 0f;
        }
    }
}
