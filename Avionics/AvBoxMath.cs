using System;
using System.Collections.Generic;

namespace NOAvionics
{
    /// <summary>
    /// A rectangle in avionics panel space: top-left origin, <c>Y</c> negative going down.
    ///
    /// This is the convention <c>AvKit.Place</c> already writes into a <c>RectTransform</c>
    /// (anchor and pivot pinned to the parent's top-left), so a computed box drops straight
    /// into the existing widget calls. It is a separate type from <c>UnityEngine.Rect</c>
    /// only because this assembly is compiled into the net8.0 test projects, which have no
    /// game install to reference.
    /// </summary>
    public readonly struct AvRect
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public AvRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>The bottom edge, which is the most negative Y the box covers.</summary>
        public float Bottom => Y - Height;

        public float Right => X + Width;

        public AvRect Inset(float left, float top, float right, float bottom) =>
            new AvRect(X + left, Y - top,
                       Math.Max(0f, Width - left - right),
                       Math.Max(0f, Height - top - bottom));

        public override string ToString() =>
            "(" + X.ToString("0.##") + ", " + Y.ToString("0.##") + ", " +
            Width.ToString("0.##") + " x " + Height.ToString("0.##") + ")";
    }

    /// <summary>Which way a container stacks its children.</summary>
    public enum AvAxis
    {
        /// <summary>Children stack downward; the main axis is height.</summary>
        Column,

        /// <summary>Children stack rightward; the main axis is width.</summary>
        Row,

        /// <summary>Children fill a fixed column count, wrapping into rows.</summary>
        Grid,
    }

    /// <summary>How a node claims space along its parent's main axis.</summary>
    public enum AvSize
    {
        /// <summary>An explicit pixel extent.</summary>
        Fixed,

        /// <summary>Measured from content — children, or text through <see cref="AvNode.Measure"/>.</summary>
        Auto,

        /// <summary>A weighted share of whatever the fixed and auto siblings left over.</summary>
        Grow,
    }

    /// <summary>
    /// Measures a leaf's extent along the main axis, given the cross-axis space it will get.
    ///
    /// The Unity side supplies one that asks TextMeshPro for a wrapped string's preferred
    /// height; the tests supply a stub. Returning zero is legitimate — an empty leaf.
    /// </summary>
    public delegate float AvMeasure(AvNode node, float available);

    /// <summary>
    /// One box in a panel layout tree.
    ///
    /// The panels used to position everything by hand — roughly three hundred literal
    /// <c>new Rect(Pad + inner - 176f, y - 2f, 168f, 18f)</c> calls across the two mods —
    /// which meant nothing could size to its own content and nothing could claim leftover
    /// space. Long descriptions clipped no matter how wide the panel grew, and short pages
    /// left voids. Both are size problems, so both are fixed here rather than at each call
    /// site: <see cref="AvSize.Auto"/> makes a row as tall as its copy needs, and
    /// <see cref="AvSize.Grow"/> hands the remainder to whoever should absorb it.
    ///
    /// The whole tree is measured and arranged once, at panel build time, and then thrown
    /// away — the panels keep only the resulting rectangles. Nothing here runs per frame,
    /// which matters because the refresh loops already rewrite every label at 0.15s.
    /// </summary>
    public sealed class AvNode
    {
        private static readonly AvNode[] NoChildren = new AvNode[0];

        private List<AvNode> children;

        public AvNode(string name, AvAxis axis)
        {
            Name = name ?? "";
            Axis = axis;
        }

        public string Name { get; private set; }

        /// <summary>Space-separated style classes, resolved by the stylesheet layer.</summary>
        public string Classes { get; private set; }

        public AvAxis Axis { get; private set; }

        public AvSize Mode { get; private set; } = AvSize.Auto;

        /// <summary>Main-axis extent when <see cref="Mode"/> is <see cref="AvSize.Fixed"/>.</summary>
        public float Extent { get; private set; }

        /// <summary>Share of the remainder when <see cref="Mode"/> is <see cref="AvSize.Grow"/>.</summary>
        public float Weight { get; private set; } = 1f;

        /// <summary>
        /// A cross-axis override. Zero means "fill the cross axis", which is what almost
        /// every row in a column wants.
        /// </summary>
        public float CrossExtent { get; private set; }

        public float PadLeft { get; private set; }
        public float PadTop { get; private set; }
        public float PadRight { get; private set; }
        public float PadBottom { get; private set; }

        public float Gap { get; private set; }

        /// <summary>Column count when <see cref="Axis"/> is <see cref="AvAxis.Grid"/>.</summary>
        public int Columns { get; private set; } = 1;

        internal void SetColumns(int columns) => Columns = columns < 1 ? 1 : columns;

        /// <summary>
        /// A leaf's intrinsic main-axis extent, in the absence of a measure callback.
        /// Set this for a leaf whose size is known without asking the text engine.
        /// </summary>
        public float Content { get; private set; }

        /// <summary>Arbitrary payload, so a caller can hang its own model off a node.</summary>
        public object Tag { get; set; }

        /// <summary>The arranged rectangle, valid only after <see cref="Arrange"/>.</summary>
        public AvRect Rect { get; private set; }

        public IList<AvNode> Children => (IList<AvNode>)children ?? NoChildren;

        public int ChildCount => children == null ? 0 : children.Count;

        // ------------------------------------------------------------------- building

        public AvNode Class(string classes)
        {
            Classes = classes;
            return this;
        }

        public AvNode Height(float px)
        {
            Mode = AvSize.Fixed;
            Extent = px;
            return this;
        }

        /// <summary>Alias of <see cref="Height"/>; reads better on a node inside a row.</summary>
        public AvNode Width(float px) => Height(px);

        public AvNode Auto()
        {
            Mode = AvSize.Auto;
            return this;
        }

        public AvNode Grow(float weight = 1f)
        {
            Mode = AvSize.Grow;
            Weight = weight <= 0f ? 1f : weight;
            return this;
        }

        public AvNode Cross(float px)
        {
            CrossExtent = px;
            return this;
        }

        public AvNode Pad(float all) => Pad(all, all, all, all);

        public AvNode Pad(float vertical, float horizontal) =>
            Pad(horizontal, vertical, horizontal, vertical);

        public AvNode Pad(float left, float top, float right, float bottom)
        {
            PadLeft = left;
            PadTop = top;
            PadRight = right;
            PadBottom = bottom;
            return this;
        }

        public AvNode Gaps(float px)
        {
            Gap = px;
            return this;
        }

        public AvNode Intrinsic(float px)
        {
            Content = px;
            return this;
        }

        public AvNode Add(AvNode child)
        {
            if (child == null) return this;
            if (children == null) children = new List<AvNode>();
            children.Add(child);
            return this;
        }

        public AvNode Add(params AvNode[] nodes)
        {
            if (nodes == null) return this;
            for (int i = 0; i < nodes.Length; i++) Add(nodes[i]);
            return this;
        }

        // -------------------------------------------------------------------- lookup

        /// <summary>
        /// Find a descendant by dotted path (<c>"strikes.row3.name"</c>) or by bare name.
        ///
        /// This is what replaces a panel holding forty parallel fields just to remember
        /// where it put things: build the tree, then ask it.
        /// </summary>
        public AvNode Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            int dot = path.IndexOf('.');
            if (dot < 0) return FindChild(path);

            AvNode next = FindChild(path.Substring(0, dot));
            return next == null ? null : next.Find(path.Substring(dot + 1));
        }

        /// <summary>The arranged rectangle of a descendant, or an empty rect if absent.</summary>
        public AvRect this[string path]
        {
            get
            {
                AvNode found = Find(path);
                return found == null ? default(AvRect) : found.Rect;
            }
        }

        private AvNode FindChild(string name)
        {
            if (children == null) return null;

            // Direct children first, so a shallow name never resolves to a deep namesake.
            for (int i = 0; i < children.Count; i++)
            {
                if (string.Equals(children[i].Name, name, StringComparison.Ordinal))
                    return children[i];
            }

            for (int i = 0; i < children.Count; i++)
            {
                AvNode deep = children[i].FindChild(name);
                if (deep != null) return deep;
            }

            return null;
        }

        // ------------------------------------------------------------------ measuring

        /// <summary>
        /// This node's main-axis extent given the cross-axis space it will occupy.
        ///
        /// Cross-axis-first is what makes wrapped text measurable: a column hands its child
        /// the inner width, and only then can the child say how tall its paragraph is.
        /// </summary>
        public float MeasureMain(float crossAvailable, AvMeasure measure)
        {
            switch (Mode)
            {
                case AvSize.Fixed:
                    return Extent;

                // A Grow node contributes nothing to its parent's intrinsic size; it consumes
                // what is left after everyone else. Measuring it as its content would let a
                // long child push the parent past the panel it has to fit inside.
                case AvSize.Grow:
                    return 0f;

                default:
                    return MeasureContent(crossAvailable, measure);
            }
        }

        private float MeasureContent(float crossAvailable, AvMeasure measure)
        {
            float inner = InnerCross(crossAvailable);

            if (children == null || children.Count == 0)
            {
                float leaf = measure != null ? measure(this, inner) : Content;
                return Math.Max(0f, leaf) + PadTop + PadBottom;
            }

            if (Axis == AvAxis.Row)
            {
                // A row is as tall as its tallest child, and a child of a row measures
                // against the width it will actually get.
                float tallest = 0f;
                float[] widths = ResolveRowWidths(inner, measure);
                for (int i = 0; i < children.Count; i++)
                {
                    float h = children[i].MeasureContentCross(widths[i], measure);
                    if (h > tallest) tallest = h;
                }
                return tallest + PadTop + PadBottom;
            }

            if (Axis == AvAxis.Grid)
            {
                int cols = Columns < 1 ? 1 : Columns;
                float cell = cols == 1 ? inner : (inner - Gap * (cols - 1)) / cols;

                float total = 0f;
                int rows = 0;
                for (int i = 0; i < children.Count; i += cols)
                {
                    float tallest = 0f;
                    for (int c = 0; c < cols && i + c < children.Count; c++)
                    {
                        float h = children[i + c].MeasureMain(cell, measure);
                        if (h > tallest) tallest = h;
                    }
                    total += tallest;
                    rows++;
                }
                if (rows > 1) total += Gap * (rows - 1);
                return total + PadTop + PadBottom;
            }

            // Column.
            float sum = 0f;
            int counted = 0;
            for (int i = 0; i < children.Count; i++)
            {
                sum += children[i].MeasureMain(inner, measure);
                counted++;
            }
            if (counted > 1) sum += Gap * (counted - 1);
            return sum + PadTop + PadBottom;
        }

        /// <summary>
        /// Height of a node that lives inside a row, where the main axis of the *parent*
        /// is width but this node's own extent along the cross axis is what is wanted.
        /// </summary>
        private float MeasureContentCross(float widthAvailable, AvMeasure measure)
        {
            if (Axis == AvAxis.Row || Axis == AvAxis.Grid || children != null)
                return MeasureContent(widthAvailable, measure);

            float leaf = measure != null ? measure(this, Math.Max(0f, widthAvailable - PadLeft - PadRight)) : Content;
            return Math.Max(0f, leaf) + PadTop + PadBottom;
        }

        /// <summary>
        /// The width this node wants when nothing is constraining it — a chip sized to its
        /// own label, a cost column sized to its longest number. The measure callback is
        /// handed a non-positive available extent to distinguish this from a height query.
        /// </summary>
        private float MeasureIntrinsicWidth(AvMeasure measure)
        {
            if (children != null && children.Count > 0)
            {
                float total = 0f;
                if (Axis == AvAxis.Row)
                {
                    for (int i = 0; i < children.Count; i++)
                        total += children[i].MeasureIntrinsicWidth(measure);
                    if (children.Count > 1) total += Gap * (children.Count - 1);
                }
                else
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        float w = children[i].MeasureIntrinsicWidth(measure);
                        if (w > total) total = w;
                    }
                }
                return total + PadLeft + PadRight;
            }

            float leaf = measure != null ? measure(this, 0f) : Content;
            return Math.Max(0f, leaf) + PadLeft + PadRight;
        }

        private float InnerCross(float crossAvailable)
        {
            float inner = Axis == AvAxis.Row
                ? crossAvailable                       // cross axis of a row is height
                : crossAvailable - PadLeft - PadRight; // cross axis of a column is width
            return Math.Max(0f, inner);
        }

        // ------------------------------------------------------------------ arranging

        /// <summary>
        /// Resolve every rectangle in the tree inside <paramref name="area"/>.
        ///
        /// Call once per panel build. Every node's <see cref="Rect"/> is valid afterwards
        /// and stays valid until something calls this again.
        /// </summary>
        public AvNode Arrange(AvRect area, AvMeasure measure = null)
        {
            Rect = area;
            ArrangeChildren(measure);
            return this;
        }

        private void ArrangeChildren(AvMeasure measure)
        {
            if (children == null || children.Count == 0) return;

            AvRect inner = Rect.Inset(PadLeft, PadTop, PadRight, PadBottom);

            switch (Axis)
            {
                case AvAxis.Row: ArrangeRow(inner, measure); break;
                case AvAxis.Grid: ArrangeGrid(inner, measure); break;
                default: ArrangeColumn(inner, measure); break;
            }

            for (int i = 0; i < children.Count; i++) children[i].ArrangeChildren(measure);
        }

        private void ArrangeColumn(AvRect inner, AvMeasure measure)
        {
            int n = children.Count;
            var extents = new float[n];

            float used = 0f;
            float weight = 0f;
            for (int i = 0; i < n; i++)
            {
                AvNode c = children[i];
                if (c.Mode == AvSize.Grow) { weight += c.Weight; continue; }
                extents[i] = c.MeasureMain(inner.Width, measure);
                used += extents[i];
            }
            if (n > 1) used += Gap * (n - 1);

            float spare = Math.Max(0f, inner.Height - used);
            if (weight > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    if (children[i].Mode == AvSize.Grow)
                        extents[i] = spare * (children[i].Weight / weight);
                }
            }

            float y = inner.Y;
            for (int i = 0; i < n; i++)
            {
                AvNode c = children[i];
                float w = c.CrossExtent > 0f ? c.CrossExtent : inner.Width;
                c.Rect = new AvRect(inner.X, y, w, extents[i]);
                y -= extents[i] + Gap;
            }
        }

        private void ArrangeRow(AvRect inner, AvMeasure measure)
        {
            int n = children.Count;
            float[] widths = ResolveRowWidths(inner.Width, measure);

            float x = inner.X;
            for (int i = 0; i < n; i++)
            {
                AvNode c = children[i];
                float h = c.CrossExtent > 0f ? c.CrossExtent : inner.Height;
                c.Rect = new AvRect(x, inner.Y, widths[i], h);
                x += widths[i] + Gap;
            }
        }

        private float[] ResolveRowWidths(float innerWidth, AvMeasure measure)
        {
            int n = children.Count;
            var widths = new float[n];

            float used = 0f;
            float weight = 0f;
            for (int i = 0; i < n; i++)
            {
                AvNode c = children[i];
                if (c.Mode == AvSize.Grow) { weight += c.Weight; continue; }

                // Along a row the main axis is width. A Fixed child states it outright; an
                // Auto child is asked for its unconstrained preferred width, which the
                // measure callback signals by receiving a non-positive available extent.
                widths[i] = c.Mode == AvSize.Fixed
                    ? c.Extent
                    : c.MeasureIntrinsicWidth(measure);
                used += widths[i];
            }
            if (n > 1) used += Gap * (n - 1);

            float spare = Math.Max(0f, innerWidth - used);
            if (weight > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    if (children[i].Mode == AvSize.Grow)
                        widths[i] = spare * (children[i].Weight / weight);
                }
            }

            return widths;
        }

        private void ArrangeGrid(AvRect inner, AvMeasure measure)
        {
            int cols = Columns < 1 ? 1 : Columns;
            float cell = cols == 1 ? inner.Width : (inner.Width - Gap * (cols - 1)) / cols;

            float y = inner.Y;
            for (int i = 0; i < children.Count; i += cols)
            {
                float tallest = 0f;
                for (int c = 0; c < cols && i + c < children.Count; c++)
                {
                    float h = children[i + c].MeasureMain(cell, measure);
                    if (h > tallest) tallest = h;
                }

                for (int c = 0; c < cols && i + c < children.Count; c++)
                {
                    children[i + c].Rect =
                        new AvRect(inner.X + c * (cell + Gap), y, cell, tallest);
                }

                y -= tallest + Gap;
            }
        }

        /// <summary>Depth-first walk, this node first. Handy for a debug overlay.</summary>
        public void Walk(Action<AvNode, int> visit, int depth = 0)
        {
            if (visit == null) return;
            visit(this, depth);
            if (children == null) return;
            for (int i = 0; i < children.Count; i++) children[i].Walk(visit, depth + 1);
        }

        public override string ToString() => Name + " " + Rect;
    }

    /// <summary>Fluent entry points, so a layout reads as a shape rather than as arithmetic.</summary>
    public static class AvLayout
    {
        public static AvNode Column(string name) => new AvNode(name, AvAxis.Column);

        public static AvNode Row(string name) => new AvNode(name, AvAxis.Row);

        public static AvNode Grid(string name, int columns)
        {
            var node = new AvNode(name, AvAxis.Grid);
            node.SetColumns(columns);
            return node;
        }

        /// <summary>A column whose children each size to their own content.</summary>
        public static AvNode Stack(string name) => new AvNode(name, AvAxis.Column);

        /// <summary>A leaf: something a widget gets drawn into.</summary>
        public static AvNode Cell(string name) => new AvNode(name, AvAxis.Column);

        /// <summary>Blank space that pushes its siblings apart.</summary>
        public static AvNode Spacer(float px) =>
            new AvNode("spacer", AvAxis.Column).Height(px);

        /// <summary>Blank space that absorbs whatever is left.</summary>
        public static AvNode Filler() => new AvNode("filler", AvAxis.Column).Grow();
    }
}
