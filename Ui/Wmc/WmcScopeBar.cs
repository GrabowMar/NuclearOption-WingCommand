using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>The scope bar under the tab strip (spec WMC program §4): who the next order goes to — the whole wing, an
    /// element, or the selected wingmen — with a chip per element in use and CLEAR.</summary>
    internal sealed class WmcScopeBar
    {
        public const float Height = 26f;
        private const float ChipWidth = 26f, ClearWidth = 58f;

        private readonly AvButton[] chips = new AvButton[ElementRoster.MaxElements];
        private readonly List<uint> members = new List<uint>();
        private TMP_Text label;
        private AvButton clear;
        private WmcContext last;

        public void Build(RectTransform parent, Rect area, Dictionary<string, AvButton> ids)
        {
            float x = area.x + AvScreen.SpineInset, w = area.width - AvScreen.SpineInset - AvTokens.Space2;
            AvStyled.Box(parent, new Rect(x, area.y, w, Height), "row");
            AvStyled.Label(parent, new Rect(x + 8f, area.y - 2f, 74f, Height - 4f), "ORDERS TO", "metric-key");
            float right = x + w - ClearWidth - 2f;
            clear = AvStyled.Button(parent, new Rect(right, area.y - 2f, ClearWidth, Height - 4f), "CLEAR", "btn", () => last?.Selection.Clear());
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
            label = AvStyled.Label(parent, new Rect(x + 84f, area.y - 2f, right - x - 88f, Height - 4f), "WING", "row-name");
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void Pick(int e)
        {
            if (last == null) return;
            members.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) members.Add(last.Rows[i].Id);
            if (members.Count > 0) last.Selection.SelectElement(e, members);
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            label.text = c.Selection.Label(c.Rows, c.Count);
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
