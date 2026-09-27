using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Who the page's orders go to (spec bezel v2 §5): a key word, the scope in words, then ALL and a chip per element in
    /// use. TACTICAL, FORM and PLAN each build one; they all read and write the one selection, so the scope never differs between
    /// tabs.</summary>
    internal sealed class WmcScopeRow
    {
        private const float ChipGap = 3f, AllWidth = 38f, PresetWidth = 30f, KeyW = 60f, TextW = 126f;

        private readonly Dictionary<string, AvButton> ids;
        private readonly string prefix;
        private TMP_Text scopeText;
        private AvButton allChip;
        private readonly AvButton[] elementChips = new AvButton[ElementRoster.MaxElements];
        private readonly bool[] chipInUse = new bool[ElementRoster.MaxElements];
        private readonly string[] chipNames = new string[ElementRoster.MaxElements];
        private readonly List<uint> members = new List<uint>();
        private int scopeKey = int.MinValue;
        private string scopeLabelShown;
        private float x, y, width, chipsEnd;
        private WmcContext last;

        public WmcScopeRow(Dictionary<string, AvButton> controls, string idPrefix)
        {
            ids = controls;
            prefix = idPrefix;
        }

        /// <summary>The row at the top edge <paramref name="top"/>; <paramref name="preset"/> adds TACTICAL's [+] (element presets,
        /// disabled until P2).</summary>
        public void Build(RectTransform page, float left, float top, float w, string key, bool preset)
        {
            x = left;
            y = top;
            width = w;
            float h = BezelLayout.ScopeRow - 4f;
            AvStyled.Box(page, new Rect(x, y, width, BezelLayout.ScopeRow), "row");
            AvStyled.Label(page, new Rect(x + 8f, y - 2f, KeyW, h), key, "metric-key");
            scopeText = WmcKit.Text(page, new Rect(x + 8f + KeyW, y - 2f, TextW, h), "row-name");
            chipsEnd = x + width - 2f;
            if (preset)
            {
                AvButton plus = AvStyled.Button(page, new Rect(chipsEnd - PresetWidth, y - 2f, PresetWidth, h), "+", "btn", null);
                plus.SetEnabled(false);
                plus.WithTooltip("Element presets: saved splits of the wing, made on PLAN. Arrives in a later update.");
                ids[prefix + "preset"] = plus;
                chipsEnd -= PresetWidth + ChipGap;
            }
            float allX = x + 8f + KeyW + TextW + 4f;
            allChip = AvStyled.Button(page, new Rect(allX, y - 2f, AllWidth, h), "ALL", "btn", PickAll, AvButtonStyle.Toggle);
            allChip.WithTooltip("Orders go to the whole wing.");
            ids[prefix + "all"] = allChip;
            for (int e = 0; e < elementChips.Length; e++)
            {
                int k = e;
                elementChips[e] = AvStyled.Button(page, new Rect(allX + AllWidth + ChipGap, y - 2f, 40f, h), ElementRoster.Letter(e),
                    "btn", () => PickElement(k), AvButtonStyle.Toggle);
                elementChips[e].gameObject.SetActive(false);
                ids[prefix + "el" + e] = elementChips[e];
            }
        }

        private void PickAll()
        {
            if (last == null) return;
            last.Selection.Clear();
            last.Rescope();
        }

        /// <summary>Orders go to element <paramref name="e"/> (its chip, or its head in the flight list).</summary>
        public void PickElement(int e)
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
            int inScope = Count(c);
            int key = (int)c.Scope.Kind * 100000 + c.ScopeElement * 10000 + inScope * 100 + c.Selection.Count;
            if (key != scopeKey || !ReferenceEquals(scopeLabelShown, c.ScopeLabel))
            {
                scopeKey = key;
                scopeLabelShown = c.ScopeLabel;
                WmcKit.Set(scopeText, WmcWords.ScopeText(c.ScopeLabel, inScope));
            }
            allChip.SetLatched(c.Scope.Kind == ScopeKind.Wing);

            bool changed = false;
            int used = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                string name = present && c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
                if (present != chipInUse[e] || !ReferenceEquals(name, chipNames[e])) changed = true;
                chipInUse[e] = present;
                chipNames[e] = name;
                if (present) used++;
            }
            if (changed) PlaceChips(used);
            for (int e = 0; e < elementChips.Length; e++)
                if (chipInUse[e]) elementChips[e].SetLatched(c.Scope.Kind == ScopeKind.Element && c.Scope.Element == e);
        }

        /// <summary>Element chips share what ALL (and [+]) leave; a chip too narrow for "B · VIPER" shows its letter (the name is
        /// in the tooltip).</summary>
        private void PlaceChips(int used)
        {
            float h = BezelLayout.ScopeRow - 4f;
            float start = x + 8f + KeyW + TextW + 4f + AllWidth + ChipGap;
            float w = used > 0 ? (chipsEnd - start - ChipGap * (used - 1)) / used : 0f;
            int k = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                AvButton chip = elementChips[e];
                chip.gameObject.SetActive(chipInUse[e]);
                if (!chipInUse[e]) continue;
                AvKit.Place((RectTransform)chip.transform, new Rect(start + k * (w + ChipGap), y - 2f, w, h));
                string letter = ElementRoster.Letter(e), name = chipNames[e];
                bool named = !string.IsNullOrEmpty(name) && name != letter;
                string full = named ? letter + " · " + name : letter;
                // ~6.5 px a character at 10 px bold; the button keeps a 12 px margin.
                chip.SetText(full.Length * 6.5f <= w - 12f ? full : letter);
                chip.WithTooltip("Orders go to element " + full + ".");
                k++;
            }
        }

        /// <summary>Aircraft the scope holds now.</summary>
        public static int Count(WmcContext c)
        {
            switch (c.Scope.Kind)
            {
                case ScopeKind.Members: return c.Scope.Members?.Length ?? 0;
                case ScopeKind.Element:
                    int n = 0;
                    for (int i = 0; i < c.Count; i++)
                        if (c.Rows[i].Element == c.Scope.Element) n++;
                    return n;
                default: return c.Count;
            }
        }
    }
}
