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
        /// <summary>Who the next order goes to (spec WMC program §4): the selection by aircraft id, and the scope it makes this
        /// refresh.</summary>
        public readonly WmcSelection Selection = new WmcSelection();
        public WingScope Scope;
        /// <summary>The element the scope points at without detaching (A for the wing, the first selected member's).</summary>
        public int ScopeElement;
        /// <summary>The scope in words ("WING", "ELEMENT B", "#3 #4").</summary>
        public string ScopeLabel = "WING";

        /// <summary>Scope, label and element from the selection over this refresh's rows. The panel calls it on refresh, and
        /// every selection handler calls it at once (review P3 I3), so an order pressed right after a click goes to what
        /// the click chose.</summary>
        public void Rescope()
        {
            Scope = Selection.Scope(Rows, Count);
            ScopeLabel = Selection.Label(Rows, Count);
            ScopeElement = 0;
            if (Scope.Kind == ScopeKind.Element) ScopeElement = Scope.Element;
            else if (Scope.Kind == ScopeKind.Members)
            {
                int i = WingRows.IndexOf(Rows, Count, Scope.Members[0]);
                if (i >= 0) ScopeElement = Rows[i].Element;
            }
        }

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

        /// <summary>A page's spine (its full content height) and the inner rect its content hangs in, right of the spine
        /// (the synced shell has no outer padding; Boscali's panels lay out the same way).</summary>
        public static Rect Page(RectTransform page, Rect body, float contentHeight)
        {
            AvStyled.Spine(page, new Rect(body.x, body.y, 1f, Mathf.Max(body.height, contentHeight)));
            return new Rect(body.x + AvScreen.SpineInset, body.y,
                body.width - AvScreen.SpineInset - AvTokens.Space2, body.height);
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
            if (fill != null) hit.SetRowHighlight(fill, RowColor("rest"), RowColor("hover"));
            return hit;
        }

        /// <summary>The `.row` fill for a <see cref="WmcStyle"/> row key: rest, hover (`.row:hover`) or selected
        /// (`.row:armed`).</summary>
        public static Color RowColor(string key)
        {
            AvStyle style = key == "hover" ? AvStyleHost.Style("row", "hover")
                : key == "selected" ? AvStyleHost.Style("row", "armed") : AvStyleHost.Style("row");
            return AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert);
        }

        private static readonly Dictionary<string, Color> railColors = new Dictionary<string, Color>();

        /// <summary>Colours a rail by its stylesheet class, each class resolved once (no string per refresh: review P1 m6).
        /// ponytail: a theme change mid-mission keeps the first colours; clear the cache on theme change if that matters.</summary>
        public static void SetRail(Image rail, string railClass)
        {
            if (rail == null) return;
            if (!railColors.TryGetValue(railClass, out Color c))
            {
                AvStyle style = AvStyleHost.Style("rail " + railClass);
                c = AvStyleHost.Resolve(style.Background, AvTheme.RailInert);
                railColors[railClass] = c;
            }
            if (rail.color != c) rail.color = c;
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
