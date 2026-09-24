using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>What every WMC tab reads at a refresh (spec M7b §6): the wing's rows — the host's service, or a client's
    /// mirror of the same snapshot entries.</summary>
    internal sealed class WmcContext
    {
        public WingService Wing;
        public readonly SnapshotMember[] Rows = new SnapshotMember[WcSnapshot.MaxMembers];
        public int Count;
        public bool Client, Stale;
        public float MissionTime;
        /// <summary>The selected aircraft's persistent id on the WING tab, 0 for none (slots renumber; review M7b-1 I2).</summary>
        public uint SelectedId;

        /// <summary>Orders run on the host (client orders are M6c-2).</summary>
        public bool CanOrder => !Client && Wing != null && Wing.Selection != null;

        /// <summary>The live member flying the aircraft with this persistent id (host only), or null.</summary>
        public WingMember MemberOf(uint id)
        {
            if (Wing == null || id == 0u) return null;
            foreach (WingMember m in Wing.Members)
                if (!m.Released && m.Aircraft != null && m.Aircraft.persistentID.Id == id) return m;
            return null;
        }

        /// <summary>The aircraft with this persistent id, or null.</summary>
        public static Unit UnitOf(uint id) => id != 0u && new PersistentID { Id = id }.TryGetUnit(out Unit u) ? u : null;

        /// <summary>The aircraft of the row flying a slot now (host or client), or null.</summary>
        public Unit UnitAtSlot(int slot)
        {
            for (int i = 0; i < Count; i++)
                if (Rows[i].Slot == slot) return UnitOf(Rows[i].Id);
            return null;
        }
    }

    internal interface IWmcTab
    {
        /// <summary>Once per panel build; <paramref name="body"/> is the page area (top-left origin, y grows negative).</summary>
        void Build(RectTransform page, Rect body);

        void Refresh(WmcContext c);

        /// <summary>The status strip's ambient line while the tab shows.</summary>
        string Hint { get; }

        /// <summary>The page's content height; the panel wraps a taller page in a scroll viewport.</summary>
        float ContentHeight { get; }
    }

    /// <summary>Layout helpers the WMC tabs share.</summary>
    internal static class WmcUi
    {
        public const float Row = AvTokens.RowHeight, Gap = AvTokens.Space1, TitleHeight = 18f;

        /// <summary>A section title at <paramref name="y"/>; returns the y below it.</summary>
        public static float Title(RectTransform parent, Rect body, float y, string text)
        {
            AvStyled.Label(parent, new Rect(body.x, y, body.width, TitleHeight), text, "section-title");
            return y - TitleHeight - Gap;
        }

        /// <summary>Buttons <paramref name="columns"/> to a row, registered under their ids; returns the y below.</summary>
        public static float Buttons(RectTransform parent, Rect body, float y, int columns,
            Dictionary<string, AvButton> ids, params (string Id, string Text, Action Click, string Tip)[] items)
        {
            float w = (body.width - Gap * (columns - 1)) / columns;
            for (int i = 0; i < items.Length; i++)
            {
                int col = i % columns;
                if (i > 0 && col == 0) y -= Row + Gap;
                AvButton b = AvStyled.Button(parent, new Rect(body.x + col * (w + Gap), y, w, Row), items[i].Text, "btn", items[i].Click);
                if (!string.IsNullOrEmpty(items[i].Tip)) b.WithTooltip(items[i].Tip);
                if (ids != null) ids[items[i].Id] = b;
            }
            return y - Row - Gap;
        }

        /// <summary>A track and its fill; <see cref="SetBar"/> sizes the fill.</summary>
        public static Image Bar(RectTransform parent, Rect area)
        {
            Image track = AvKit.Panel(parent, area, AvTheme.SurfaceInert);
            track.raycastTarget = false;
            Image fill = AvKit.Panel(parent, new Rect(area.x, area.y, 0f, area.height), AvTheme.Friendly);
            fill.raycastTarget = false;
            return fill;
        }

        public static void SetBar(Image fill, float width, float fraction, Color color)
        {
            RectTransform rt = fill.rectTransform;
            rt.sizeDelta = new Vector2(float.IsNaN(fraction) ? 0f : width * Mathf.Clamp01(fraction), rt.sizeDelta.y);
            fill.color = color;
        }

        /// <summary>A fuel/ammo colour: alert below 15%, caution below 35%.</summary>
        public static Color Level(float fraction) =>
            fraction < 0.15f ? AvTheme.Alert : fraction < 0.35f ? AvTheme.Warning : AvTheme.Friendly;

        /// <summary>A section head (spine tick + section-title, optional note); returns the y below it.</summary>
        public static float Head(RectTransform parent, Rect body, float y, string title, string note = null)
        {
            AvStyled.SpineTick(parent, body.x - AvScreen.SpineInset, y - 8f);
            AvStyled.Label(parent, new Rect(body.x, y, body.width, 16f), title, "section-title");
            if (!string.IsNullOrEmpty(note))
                AvStyled.Label(parent, new Rect(body.x, y, body.width, 16f), note, "section-title-note",
                    align: TextAlignmentOptions.MidlineRight);
            return y - 16f - Gap;
        }

        /// <summary>A `.row` card with a state rail; the whole card is the click target and highlights on hover.</summary>
        public static AvButton Card(RectTransform parent, Rect r, Action click, out Image fill, out Image rail)
        {
            fill = AvStyled.Box(parent, r, "row");
            if (fill != null) fill.raycastTarget = false;
            rail = AvStyled.Rail(parent, new Rect(r.x, r.y, 3f, r.height), "info");
            AvButton hit = AvKit.HitButton(parent, r, click);
            if (fill != null) hit.SetRowHighlight(fill, AvTheme.SurfaceInert, AvTheme.SurfaceRaised);
            return hit;
        }

        public static void SetRail(Image rail, string railClass)
        {
            if (rail == null) return;
            AvStyle style = AvStyleHost.Style("rail " + railClass);
            rail.color = AvStyleHost.Resolve(style.Background, AvTheme.RailInert);
        }

        /// <summary>The row-value colour of a level class ("ok", "warn", "bad", "").</summary>
        public static Color LevelColor(string level) =>
            level == "bad" ? AvTheme.RailDanger : level == "warn" ? AvTheme.RailCaution : level == "ok" ? AvTheme.RailReady
            : AvTheme.TextPrimary;

        /// <summary>Run an order only where orders run; otherwise say why (review focus 1).</summary>
        public static void Order(WmcContext c, Action act)
        {
            if (c == null || !c.CanOrder)
            {
                WingToast.Show(c != null && c.Client ? "WMC: orders are host only for now" : "Wing Command is not ready");
                return;
            }
            act();
        }
    }
}
