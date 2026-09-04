using NOAvionics;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Where the three columns of the maximised map screen begin and end.
    /// Thin adapter over <see cref="AvGrid"/>.
    ///
    /// Panels on the left, the map in the centre, and one thin rail on the right holding
    /// every button. One authority means they cannot disagree.
    ///
    /// All rectangles are in the maximised map canvas's own space: origin at the canvas
    /// centre, +Y up, which is what <c>RectTransform.anchoredPosition</c> wants when anchor
    /// and pivot are both centred.
    /// </summary>
    internal static class MfdLayout
    {
        public const float RailWidth = 96f;
        public const float Gutter = 8f;
        public const float MapInset = 0f;
        public const float Margin = 8f;

        /// <summary>
        /// Height kept clear at the bottom of every column for the game's own spawn strip
        /// — "Select Home Airbase", the Select Aircraft button, the spectator bar. With no
        /// reserve the map viewport was drawn over the top of that strip and swallowed the
        /// clicks meant for it, so the button could not be pressed while the map was up.
        /// </summary>
        public const float BottomReserve = 120f;

        /// <summary>
        /// Height kept clear at the top for the mission clock and the kill / chat feed,
        /// which the game paints in the top-left. With no reserve the stock panels docked
        /// in the left column had their first row sitting under that feed and clipped by
        /// the canvas edge.
        /// </summary>
        public const float TopReserve = 26f;

        /// <summary>The three columns, resolved against a canvas of this size.</summary>
        internal struct Columns
        {
            /// <summary>Where a panel goes.</summary>
            public Rect Panel;

            /// <summary>What is left for the map, between the panel column and the rail.</summary>
            public Rect Map;

            /// <summary>The button rail, against the right edge.</summary>
            public Rect Rail;

            /// <summary>Canvas size the columns were resolved against.</summary>
            public Vector2 Canvas;
        }

        /// <summary>Resolve the columns for a canvas using AvGrid geometry authority.</summary>
        public static Columns Resolve(Vector2 canvasSize, float panelWidth = AvTokens.PanelWidth)
        {
            var spec = AvGridSpec.Default;
            spec.Gutter = Gutter;
            spec.Margin = Margin;
            spec.TopReserve = TopReserve;
            spec.BottomReserve = BottomReserve;
            spec.MapInset = MapInset;

            AvRegions regions = AvGrid.Resolve(canvasSize.x, canvasSize.y, panelWidth, RailWidth, spec);

            return new Columns
            {
                Panel = ToUnityRect(regions.Panel),
                Map = ToUnityRect(regions.Map),
                Rail = ToUnityRect(regions.Rail),
                Canvas = canvasSize,
            };
        }

        /// <summary>Resolve against a live canvas, or report failure if there is not one yet.</summary>
        public static bool TryResolve(Canvas canvas, out Columns columns, float panelWidth = AvTokens.PanelWidth)
        {
            columns = default;
            if (canvas == null) return false;

            var rt = canvas.transform as RectTransform;
            if (rt == null) return false;

            Vector2 size = rt.rect.size;
            if (size.x <= 1f || size.y <= 1f) return false;

            columns = Resolve(size, panelWidth);
            return true;
        }

        private static Rect ToUnityRect(AvRect r) => new Rect(r.X, r.Y, r.Width, r.Height);

        /// <summary>
        /// The centre of a column, as an <c>anchoredPosition</c> for a child of the canvas
        /// whose anchor and pivot are centred.
        /// </summary>
        public static Vector2 CentreOf(Rect column) =>
            new Vector2(column.x + column.width * 0.5f, column.y - column.height * 0.5f);

        /// <summary>
        /// The top-left corner of a column, as an <c>anchoredPosition</c> for a child of the
        /// canvas whose anchors are centred and whose <b>pivot is its own top-left</b>.
        /// </summary>
        public static Vector2 TopLeftOf(Rect column) => new Vector2(column.x, column.y);

        /// <summary>
        /// Place a panel of a known height at the top of the panel column.
        /// </summary>
        public static Vector2 PanelPosition(Columns columns, float panelHeight)
        {
            Rect column = columns.Panel;
            float height = Mathf.Min(panelHeight, column.height);
            return new Vector2(column.x + column.width * 0.5f, column.y - height * 0.5f);
        }
    }
}

