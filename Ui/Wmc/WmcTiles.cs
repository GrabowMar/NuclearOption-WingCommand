using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>A page of airframe tiles (SUPPLY step 2; LOADOUT in R5): pooled cells, each rebound only when its caller's key
    /// changes (Boscali's PagingGrid idea). A tile is an icon, a code, a name and — when the caller has one — a foot under a
    /// hairline; the whole tile is the click target. An empty list shows one inert card that says why.</summary>
    internal sealed class WmcTiles
    {
        private sealed class Tile
        {
            public GameObject Root;
            public Image Fill, Rail, Icon, Hair;
            public TMP_Text Code, Name, Foot;
            public Color FootRest;
            public AvButton Hit;
            public int Key = int.MinValue;
        }

        private Tile[] tiles;
        private GameObject empty;
        private TMP_Text emptyText;

        public int PerPage => tiles.Length;
        public float Height { get; private set; }

        public static WmcTiles Build(RectTransform parent, Rect area, int cols, int rows, float tileH, string idPrefix,
            Dictionary<string, AvButton> ids, Action<int> pick)
        {
            float gap = BezelLayout.TileGap, w = (area.width - (cols - 1) * gap) / cols;
            var grid = new WmcTiles { tiles = new Tile[cols * rows], Height = rows * tileH + (rows - 1) * gap };
            for (int i = 0; i < grid.tiles.Length; i++)
            {
                var r = new Rect(area.x + i % cols * (w + gap), area.y - i / cols * (tileH + gap), w, tileH);
                grid.tiles[i] = BuildTile(parent, r, i, pick);
                ids[idPrefix + i] = grid.tiles[i].Hit;
            }

            var go = new GameObject("TilesEmpty", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, new Rect(area.x, area.y, area.width, grid.Height));
            AvStyled.Box(rt, new Rect(0f, 0f, area.width, grid.Height), "card", "inert");
            grid.emptyText = AvStyled.Label(rt, new Rect(12f, -12f, area.width - 24f, grid.Height - 24f), "", "row-sub",
                align: TextAlignmentOptions.Center);
            grid.empty = go;
            go.SetActive(false);
            return grid;
        }

        private static Tile BuildTile(RectTransform parent, Rect r, int slot, Action<int> pick)
        {
            var go = new GameObject("Tile" + slot, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            var t = new Tile { Root = go };
            t.Fill = AvStyled.Box(rt, new Rect(0f, 0f, r.width, r.height), "row");
            if (t.Fill != null) t.Fill.raycastTarget = false;
            t.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, r.height), "inert");
            t.Icon = AvKit.Panel(rt, new Rect(8f, -(r.height - 20f) * 0.5f, 20f, 20f), Color.white);
            t.Icon.preserveAspect = true;
            t.Icon.raycastTarget = false;
            float text = r.width - 36f;
            t.Code = WmcKit.Text(rt, new Rect(32f, -3f, text, 16f), "row-name");
            t.Name = WmcKit.Text(rt, new Rect(32f, -19f, text, 14f), "row-sub");
            t.Hair = AvKit.Rule(rt, new Rect(32f, -36f, text, 1f), AvTheme.Hairline);
            t.Foot = WmcKit.Text(rt, new Rect(32f, -39f, text, 14f), "row-sub");
            t.FootRest = t.Foot.color;
            t.Hit = AvKit.HitButton(rt, new Rect(0f, 0f, r.width, r.height), () => pick(slot));
            if (t.Fill != null) t.Hit.SetRowHighlight(t.Fill, WmcUi.RowColor("rest"), WmcUi.RowColor("hover"));
            go.SetActive(false);
            return t;
        }

        /// <summary>True (and remembered) when the slot's content key changed: build strings only then.</summary>
        public bool NeedsBind(int slot, int key)
        {
            Tile t = tiles[slot];
            if (t.Key == key && t.Root.activeSelf) return false;
            t.Key = key;
            return true;
        }

        /// <summary>A tile's content; a null foot hides the foot and its hairline. <paramref name="footLevel"/> is "", "ok",
        /// "warn" or "bad"; <paramref name="railClass"/> a rail state class.</summary>
        public void Bind(int slot, Sprite icon, string code, string name, string foot, string footLevel, string railClass,
            bool selected, bool enabled, string tip)
        {
            Tile t = tiles[slot];
            if (!t.Root.activeSelf) t.Root.SetActive(true);
            t.Icon.sprite = icon;
            t.Icon.enabled = icon != null;
            t.Icon.color = selected ? Color.white : enabled ? AvTheme.Friendly : AvTheme.Dim;
            WmcKit.Set(t.Code, code);
            WmcKit.Set(t.Name, name);
            bool footed = foot != null;
            t.Hair.gameObject.SetActive(footed);
            t.Foot.gameObject.SetActive(footed);
            if (footed)
            {
                WmcKit.Set(t.Foot, foot);
                t.Foot.color = string.IsNullOrEmpty(footLevel) ? t.FootRest : WmcUi.LevelColor(footLevel);
            }
            WmcUi.SetRail(t.Rail, railClass);
            if (t.Fill != null) t.Hit.SetRowHighlight(t.Fill, WmcUi.RowColor(selected ? "selected" : "rest"), WmcUi.RowColor("hover"));
            t.Hit.SetEnabled(enabled);
            t.Hit.WithTooltip(tip);
        }

        public void Hide(int slot)
        {
            Tile t = tiles[slot];
            t.Key = int.MinValue;
            if (t.Root.activeSelf) t.Root.SetActive(false);
        }

        /// <summary>One inert card over the whole grid saying why it is empty; null hides it.</summary>
        public void ShowEmpty(string text)
        {
            bool on = text != null;
            if (empty.activeSelf != on) empty.SetActive(on);
            if (on) WmcKit.Set(emptyText, text);
        }
    }
}
