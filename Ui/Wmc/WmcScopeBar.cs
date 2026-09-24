using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>The scope bar under the tab strip (spec WMC program §4): who the next order goes to — the whole wing, an
    /// element, or the selected wingmen — with a chip per element in use and CLEAR; RECRUIT n when friendly AI aircraft are
    /// selected on the map (spec §5).</summary>
    internal sealed class WmcScopeBar
    {
        public const float Height = 26f;
        private const float ChipWidth = 26f, ClearWidth = 58f, RecruitWidth = 136f, RoomWidth = 50f;

        private readonly AvButton[] chips = new AvButton[ElementRoster.MaxElements];
        private readonly List<uint> members = new List<uint>();
        private TMP_Text label;
        private AvButton clear, recruit;
        private WmcContext last;
        private readonly List<Aircraft> recruits = new List<Aircraft>(WingOrder.MaxUnits);
        private readonly ConfirmGate recruitGate = new ConfirmGate();
        private float recruitCost, labelWidth;
        private int shownCount = -1;
        private float shownCost = -1f;

        public void Build(RectTransform parent, Rect area, Dictionary<string, AvButton> ids)
        {
            float x = area.x + AvScreen.SpineInset, w = area.width - AvScreen.SpineInset - AvTokens.Space2;
            AvStyled.Box(parent, new Rect(x, area.y, w, Height), "row");
            AvStyled.Label(parent, new Rect(x + 8f, area.y - 2f, 74f, Height - 4f), "ORDERS TO", "metric-key");
            AvButton room = AvStyled.Button(parent, new Rect(x + w - RoomWidth - 2f, area.y - 2f, RoomWidth, Height - 4f), "ROOM", "btn",
                () => WmcRoom.Instance?.Open());
            room.WithTooltip("Open the Wing Command room: the theatre map, elements and the deep member card.");
            ids["scope.room"] = room;
            float right = x + w - RoomWidth - 2f - ClearWidth - 2f;
            clear = AvStyled.Button(parent, new Rect(right, area.y - 2f, ClearWidth, Height - 4f), "CLEAR", "btn", Clear);
            clear.WithTooltip("Orders go to the whole wing again.");
            ids["scope.clear"] = clear;
            for (int e = ElementRoster.MaxElements - 1; e >= 0; e--)
            {
                int k = e;
                right -= ChipWidth + 2f;
                chips[e] = AvStyled.Button(parent, new Rect(right, area.y - 2f, ChipWidth, Height - 4f), ElementRoster.Letter(e), "btn",
                    () => Pick(k), AvButtonStyle.Toggle);
                chips[e].WithTooltip("Orders go to element " + ElementRoster.Letter(e) + ".");
                ids["scope.element" + e] = chips[e];
            }
            recruit = AvStyled.Button(parent, new Rect(right - RecruitWidth - 4f, area.y - 2f, RecruitWidth, Height - 4f), "RECRUIT", "btn", Recruit);
            recruit.WithTooltip("Take command of the friendly aircraft selected on the map (press twice; the cost is shown).");
            ids["scope.recruit"] = recruit;
            recruit.gameObject.SetActive(false);
            labelWidth = right - x - 88f;
            label = AvStyled.Label(parent, new Rect(x + 84f, area.y - 2f, labelWidth, Height - 4f), "WING", "row-name");
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>Friendly AI aircraft selected on the map that could join (host only), and what they cost.</summary>
        private void CountRecruits(WmcContext c)
        {
            recruits.Clear();
            recruitCost = 0f;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || c.Wing == null || c.Client) return;
            // Review P4 I3: only as many as the wing has room for, and only those the ledger allows.
            int room = WingService.MaxMembers - c.Wing.Members.Count - (SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0);
            foreach (MapIcon icon in map.selectedIcons)
            {
                if (recruits.Count >= WingOrder.MaxUnits || recruits.Count >= room) break;
                if (!(icon is UnitMapIcon u) || !(u.unit is Aircraft a) || !c.Wing.CanRecruit(a, out _)) continue;
                CallQuote q = WingRecruitment.Quote(a);
                if (!q.Allowed) continue;
                recruits.Add(a);
                recruitCost += q.Charge;
            }
        }

        private void Recruit()
        {
            if (last == null || recruits.Count == 0) return;
            WmcUi.Order(last, () =>
            {
                string cost = CallCost.Money(recruitCost);
                if (!recruitGate.Press(recruits.Count + "|" + cost, Time.unscaledTime))
                {
                    WingToast.Show($"Recruit {recruits.Count} for {cost}? Press RECRUIT again");
                    return;
                }
                var units = new uint[recruits.Count];
                for (int i = 0; i < units.Length; i++) units[i] = recruits[i].persistentID.Id;
                if (!WingOrders.Run(new WingOrder { Kind = OrderKind.Recruit, Units = units }).Accepted) return;
                // The recruited leave the game's selection (or the player's target list), as in 0.9.
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                foreach (Aircraft a in recruits)
                {
                    if (a == null) continue;
                    if (flying && hud.GetTargetList().Contains(a)) hud.DeSelectUnit(a);
                    else map?.DeselectIcon(a);
                }
                recruits.Clear();
            });
        }

        private void Clear()
        {
            if (last == null) return;
            last.Selection.Clear();
            last.Rescope();
        }

        private void Pick(int e)
        {
            if (last == null) return;
            members.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) members.Add(last.Rows[i].Id);
            if (members.Count > 0) last.Selection.SelectElement(e, members);
            last.Rescope();
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            label.text = c.ScopeLabel;
            CountRecruits(c);
            bool show = recruits.Count > 0;
            if (recruit.gameObject.activeSelf != show)
            {
                recruit.gameObject.SetActive(show);
                label.rectTransform.sizeDelta = new Vector2(show ? labelWidth - RecruitWidth - 8f : labelWidth, label.rectTransform.sizeDelta.y);
            }
            if (show && (recruits.Count != shownCount || recruitCost != shownCost))
            {
                shownCount = recruits.Count;
                shownCost = recruitCost;
                recruit.SetText("RECRUIT " + shownCount + " · " + CallCost.Money(recruitCost));
            }
            for (int e = 0; e < chips.Length; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                chips[e].SetEnabled(present);
                chips[e].SetLatched(c.Scope.Kind == ScopeKind.Element && c.Scope.Element == e);
            }
            clear.SetEnabled(c.Selection.Count > 0);
        }
    }
}
