using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Draws the radial menu icons procedurally at runtime.
    ///
    /// The mod ships no image files, so each glyph is rasterised into a small texture with
    /// coverage-based anti-aliasing and wrapped in a Sprite. Glyphs are drawn white; the
    /// stock menu tints them through <c>iconImage.color</c>, so they pick up the hover and
    /// caution colours automatically.
    ///
    /// The formation-shape icons draw the actual slot geometry — leader plus wingmen in
    /// their real relative positions — so the picker shows you the shape rather than
    /// naming it.
    /// </summary>
    internal static class IconFactory
    {
        private const int Size = 96;

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string key)
        {
            if (cache.TryGetValue(key, out Sprite existing)) return existing;

            var canvas = new Canvas(Size);
            Draw(key, canvas);

            Sprite sprite = canvas.ToSprite("WingCommandIcon_" + key);
            cache[key] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------- glyphs

        private static void Draw(string key, Canvas c)
        {
            switch (key)
            {
                case "root":        Chevrons(c);            break;
                case "recruit":     Recruit(c);             break;
                case "rejoin":      Rejoin(c);              break;
                case "engage":      Engage(c);              break;
                case "rtb":         ReturnToBase(c);        break;
                case "formation":   ShapeGlyph(c, FormationShape.EchelonRight); break;
                case "orders":      Orders(c);              break;
                case "attack":      AttackTarget(c);        break;
                case "posture":     Posture(c);             break;
                case "disband":     Disband(c);             break;
                case "fallback":    FallBack(c);            break;
                case "cover":       Cover(c);               break;
                case "orbit":       Orbit(c);               break;
                case "move":        Move(c);                break;
                case "tasking":     Tasking(c);             break;
                case "cargo":       Cargo(c);               break;
                case "land":        LandHere(c);            break;
                case "buy":         Buy(c);                 break;
                case "back":        Back(c);                break;
                case "wing-badge":  WingBadge(c);           break;
                case "selection":   SelectionBrackets(c);   break;
                case "airframe":    Airframe(c);            break;

                default:
                    // Shape glyphs are derived from the solver, so any formation added to
                    // the enum draws itself without needing an icon written for it.
                    if (key != null && key.StartsWith("shape_") &&
                        TryParseShape(key.Substring(6), out FormationShape shape))
                    {
                        ShapeGlyph(c, shape);
                    }
                    else
                    {
                        Chevrons(c);
                    }
                    break;
            }
        }

        /// <summary>Two stacked chevrons — the "wing command" mark.</summary>
        private static void Chevrons(Canvas c)
        {
            const float t = 7f;
            c.Segment(20, 46, 48, 70, t);
            c.Segment(48, 70, 76, 46, t);
            c.Segment(20, 24, 48, 48, t);
            c.Segment(48, 48, 76, 24, t);
        }

        /// <summary>
        /// One shallow caret above the game's aircraft silhouette. It is enough to make
        /// wing membership recognisable by shape, while staying inside the base game's
        /// sparse line-symbol language and leaving type, heading and selection readable.
        /// </summary>
        private static void WingBadge(Canvas c)
        {
            const float t = 3.5f;
            c.Segment(31, 78, 48, 86, t);
            c.Segment(48, 86, 65, 78, t);
        }

        /// <summary>
        /// A broad top-down aircraft silhouette. WMC uses this at very low opacity behind
        /// the airframe dossier, where an outline would turn into visual noise beneath the
        /// readouts.
        /// </summary>
        private static void Airframe(Canvas c)
        {
            Delta(c, 48f, 47f, 38f, 0f);
        }

        /// <summary>
        /// Four corner brackets round the icon: "this one is under command".
        ///
        /// The command selection used to be drawn with <see cref="WingBadge"/> at 134% and
        /// a brighter tint — the same caret as the membership mark, only slightly larger.
        /// Two marks that differ by a third of their size and nothing else are one mark as
        /// far as the eye is concerned, so on a busy map there was no reading which of four
        /// wingmen the next order was going to. Corner brackets are a different shape
        /// entirely, and they are the shape every military display already uses for
        /// designation, so it needs no learning.
        /// </summary>
        private static void SelectionBrackets(Canvas c)
        {
            const float t = 5f;
            const float lo = 12f;
            const float hi = 84f;
            const float arm = 22f;

            // Bottom-left, bottom-right, top-left, top-right.
            c.Segment(lo, lo, lo + arm, lo, t);
            c.Segment(lo, lo, lo, lo + arm, t);

            c.Segment(hi, lo, hi - arm, lo, t);
            c.Segment(hi, lo, hi, lo + arm, t);

            c.Segment(lo, hi, lo + arm, hi, t);
            c.Segment(lo, hi, lo, hi - arm, t);

            c.Segment(hi, hi, hi - arm, hi, t);
            c.Segment(hi, hi, hi, hi - arm, t);
        }

        /// <summary>One aircraft plus a "+" — bring another into the wing.</summary>
        private static void Recruit(Canvas c)
        {
            Delta(c, 38, 44, 20, 0f);
            c.Segment(68, 58, 68, 82, 7f);
            c.Segment(56, 70, 80, 70, 7f);
        }

        /// <summary>Two aircraft converging on a point.</summary>
        private static void Rejoin(Canvas c)
        {
            Delta(c, 48, 62, 17, 0f);
            Delta(c, 24, 26, 14, 0f);
            Delta(c, 72, 26, 14, 0f);
            c.Segment(34, 34, 44, 48, 5f);
            c.Segment(62, 34, 52, 48, 5f);
        }

        /// <summary>Crosshair.</summary>
        private static void Engage(Canvas c)
        {
            c.Ring(48, 48, 26, 6f);
            c.Segment(48, 14, 48, 32, 6f);
            c.Segment(48, 64, 48, 82, 6f);
            c.Segment(14, 48, 32, 48, 6f);
            c.Segment(64, 48, 82, 48, 6f);
            c.Disc(48, 48, 5f);
        }

        /// <summary>Runway with a descending aircraft.</summary>
        private static void ReturnToBase(Canvas c)
        {
            c.Rect(16, 16, 80, 26);
            Delta(c, 40, 68, 18, 180f);
            c.Segment(58, 78, 74, 56, 6f);
            c.Segment(74, 56, 66, 58, 6f);
            c.Segment(74, 56, 72, 64, 6f);
        }

        /// <summary>A signal flag on a mast: orders to the flight.</summary>
        private static void Orders(Canvas c)
        {
            c.Segment(30, 18, 30, 80, 7f);
            var flag = new[]
            {
                new Vector2(34f, 78f), new Vector2(76f, 66f),
                new Vector2(34f, 54f),
            };
            c.Polygon(flag);
            c.Segment(30, 44, 62, 38, 5f);
        }

        /// <summary>Two aircraft converging on a marked target.</summary>
        private static void AttackTarget(Canvas c)
        {
            c.Ring(60, 62, 16, 5f);
            c.Disc(60, 62, 5f);
            Delta(c, 24, 30, 13, 30f);
            Delta(c, 46, 22, 13, 20f);
            c.Segment(30, 42, 48, 54, 4f);
        }

        /// <summary>A shield: the wing's rules of engagement.</summary>
        private static void Posture(Canvas c)
        {
            c.Segment(26, 70, 48, 78, 7f);
            c.Segment(48, 78, 70, 70, 7f);
            c.Segment(26, 70, 30, 40, 7f);
            c.Segment(70, 70, 66, 40, 7f);
            c.Segment(30, 40, 48, 22, 7f);
            c.Segment(66, 40, 48, 22, 7f);
            c.Disc(48, 52, 8f);
        }

        private static void Disband(Canvas c)
        {
            c.Segment(24, 24, 72, 72, 9f);
            c.Segment(24, 72, 72, 24, 9f);
        }

        /// <summary>Fall back: an aircraft turning away, with countermeasures trailing.</summary>
        private static void FallBack(Canvas c)
        {
            Delta(c, 62, 62, 15, 200f);
            c.Segment(52, 52, 34, 34, 5f);
            c.Segment(34, 34, 22, 38, 5f);
            c.Disc(30, 24, 3.5f);
            c.Disc(40, 18, 3.5f);
            c.Disc(20, 32, 3.5f);
        }

        /// <summary>Cover me: a wingman held over the leader, shield-like.</summary>
        private static void Cover(Canvas c)
        {
            Delta(c, 48, 30, 14, 0f);
            c.Segment(24, 62, 48, 74, 5f);
            c.Segment(72, 62, 48, 74, 5f);
            c.Segment(24, 62, 24, 46, 5f);
            c.Segment(72, 62, 72, 46, 5f);
        }

        /// <summary>Orbit: an aircraft circling a fixed point.</summary>
        private static void Orbit(Canvas c)
        {
            c.Ring(48, 50, 26, 4f);
            c.Disc(48, 50, 5f);
            Delta(c, 48, 24, 12, 90f);
        }

        /// <summary>
        /// Move: a V pointing at the commanded point.
        ///
        /// This used to borrow the Vic formation glyph, which does not read as a V at all —
        /// the solver puts the third wingman out to one side, so the icon came out as four
        /// triangles in a lopsided cluster. A plain chevron over the point is what the marker
        /// is for: it says "go here", not "fly this shape".
        /// </summary>
        private static void Move(Canvas c)
        {
            const float t = 8f;
            c.Segment(24, 74, 48, 28, t);
            c.Segment(48, 28, 72, 74, t);
            c.Disc(48, 26, 5f);
        }

        /// <summary>Tasking: a checklist.</summary>
        private static void Tasking(Canvas c)
        {
            c.Segment(30, 30, 74, 30, 5f);
            c.Segment(30, 50, 74, 50, 5f);
            c.Segment(30, 70, 74, 70, 5f);
            c.Disc(20, 30, 4f);
            c.Disc(20, 50, 4f);
            c.Disc(20, 70, 4f);
        }

        /// <summary>Cargo: a crate under a parachute canopy.</summary>
        private static void Cargo(Canvas c)
        {
            c.Segment(26, 34, 70, 34, 5f);
            c.Segment(26, 34, 40, 20, 5f);
            c.Segment(70, 34, 56, 20, 5f);
            c.Segment(34, 52, 62, 52, 5f);
            c.Segment(34, 52, 34, 76, 5f);
            c.Segment(62, 52, 62, 76, 5f);
            c.Segment(34, 76, 62, 76, 5f);
            c.Segment(40, 20, 56, 20, 5f);
        }

        /// <summary>Land here: an aircraft descending onto a ground line.</summary>
        private static void LandHere(Canvas c)
        {
            Delta(c, 48, 34, 14, 180f);
            c.Segment(48, 46, 48, 62, 5f);
            c.Segment(48, 62, 40, 54, 5f);
            c.Segment(48, 62, 56, 54, 5f);
            c.Segment(22, 74, 74, 74, 5f);
        }

        /// <summary>Buy: an aircraft with a price tag.</summary>
        private static void Buy(Canvas c)
        {
            Delta(c, 38, 34, 14, 0f);
            c.Segment(58, 56, 78, 56, 5f);
            c.Segment(58, 56, 58, 76, 5f);
            c.Segment(58, 76, 78, 76, 5f);
            c.Segment(78, 56, 78, 76, 5f);
            c.Segment(64, 62, 72, 70, 4f);
            c.Segment(64, 70, 72, 62, 4f);
        }

        private static void Back(Canvas c)
        {
            c.Segment(26, 48, 74, 48, 8f);
            c.Segment(26, 48, 48, 70, 8f);
            c.Segment(26, 48, 48, 26, 8f);
        }

        /// <summary>
        /// Leader plus wingmen drawn in their true relative slot positions, so the icon
        /// reads as the formation it selects.
        /// </summary>
        private static bool TryParseShape(string name, out FormationShape shape)
        {
            foreach (FormationShape candidate in FormationShapes.All)
            {
                if (candidate.ToString() == name)
                {
                    shape = candidate;
                    return true;
                }
            }
            shape = FormationShape.EchelonRight;
            return false;
        }

        private static void ShapeGlyph(Canvas c, FormationShape shape)
        {
            // Reuse the real solver so the icon can never drift from the geometry. Three
            // wingmen are drawn because several shapes — finger four, diamond — only read
            // correctly once the third aircraft is in place.
            const float scale = 0.10f;
            Vector2 centre = new Vector2(48f, 56f);

            Delta(c, centre.x, centre.y, 13, 0f);

            for (int slot = 1; slot <= 3; slot++)
            {
                Vector3 offset = FormationSolver.SlotOffset(
                    Vector3.forward, slot, shape, spacing: 120f, stack: 0f);

                // World +Z is "up" on the icon; world +X is to the right.
                float x = centre.x + offset.x * scale;
                float y = centre.y + offset.z * scale + offset.y * scale;
                Delta(c, x, y, 10, 0f);
            }
        }

        /// <summary>A small aircraft mark: a triangle pointing "forward" (up by default).</summary>
        private static void Delta(Canvas c, float cx, float cy, float size, float rotationDeg)
        {
            var pts = new[]
            {
                new Vector2(0f, size),
                new Vector2(-size * 0.78f, -size * 0.72f),
                new Vector2(0f, -size * 0.30f),
                new Vector2(size * 0.78f, -size * 0.72f),
            };

            float rad = rotationDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad), cos = Mathf.Cos(rad);

            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i];
                pts[i] = new Vector2(cx + p.x * cos - p.y * sin, cy + p.x * sin + p.y * cos);
            }

            c.Polygon(pts);
        }

        // ------------------------------------------------------------------- canvas

        /// <summary>
        /// Tiny software rasteriser. Coverage is accumulated with max-blending so
        /// overlapping strokes do not darken each other.
        /// </summary>
        private sealed class Canvas
        {
            private readonly int size;
            private readonly float[] coverage;

            public Canvas(int size)
            {
                this.size = size;
                coverage = new float[size * size];
            }

            private void Plot(int x, int y, float a)
            {
                if (x < 0 || y < 0 || x >= size || y >= size || a <= 0f) return;
                int i = y * size + x;
                if (a > coverage[i]) coverage[i] = a > 1f ? 1f : a;
            }

            /// <summary>Antialiased thick line segment.</summary>
            public void Segment(float x0, float y0, float x1, float y1, float thickness)
            {
                float half = thickness * 0.5f;
                var a = new Vector2(x0, y0);
                var b = new Vector2(x1, y1);
                Vector2 ab = b - a;
                float lenSq = Mathf.Max(ab.sqrMagnitude, 1e-6f);

                Bounds2(Mathf.Min(x0, x1) - half - 2f, Mathf.Min(y0, y1) - half - 2f,
                        Mathf.Max(x0, x1) + half + 2f, Mathf.Max(y0, y1) + half + 2f,
                        (px, py) =>
                {
                    Vector2 p = new Vector2(px, py);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                    float d = Vector2.Distance(p, a + ab * t);
                    return Coverage(half - d);
                });
            }

            public void Disc(float cx, float cy, float radius)
            {
                Bounds2(cx - radius - 2f, cy - radius - 2f, cx + radius + 2f, cy + radius + 2f,
                    (px, py) => Coverage(radius - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy))));
            }

            public void Ring(float cx, float cy, float radius, float thickness)
            {
                float half = thickness * 0.5f;
                float outer = radius + half + 2f;
                Bounds2(cx - outer, cy - outer, cx + outer, cy + outer, (px, py) =>
                {
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    return Coverage(half - Mathf.Abs(d - radius));
                });
            }

            public void Rect(float x0, float y0, float x1, float y1)
            {
                Bounds2(x0 - 2f, y0 - 2f, x1 + 2f, y1 + 2f, (px, py) =>
                {
                    float dx = Mathf.Min(px - x0, x1 - px);
                    float dy = Mathf.Min(py - y0, y1 - py);
                    return Coverage(Mathf.Min(dx, dy));
                });
            }

            /// <summary>Convex/concave polygon fill, 3x3 supersampled.</summary>
            public void Polygon(Vector2[] pts)
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (Vector2 p in pts)
                {
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                }

                for (int y = Mathf.FloorToInt(minY) - 1; y <= Mathf.CeilToInt(maxY) + 1; y++)
                {
                    for (int x = Mathf.FloorToInt(minX) - 1; x <= Mathf.CeilToInt(maxX) + 1; x++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < 3; sy++)
                        {
                            for (int sx = 0; sx < 3; sx++)
                            {
                                float px = x + (sx + 0.5f) / 3f;
                                float py = y + (sy + 0.5f) / 3f;
                                if (Inside(pts, px, py)) hits++;
                            }
                        }
                        if (hits > 0) Plot(x, y, hits / 9f);
                    }
                }
            }

            private static bool Inside(Vector2[] pts, float px, float py)
            {
                bool inside = false;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    if (pts[i].y > py != pts[j].y > py &&
                        px < (pts[j].x - pts[i].x) * (py - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                        inside = !inside;
                }
                return inside;
            }

            private static float Coverage(float signedDistance)
            {
                // One-pixel linear ramp across the edge.
                return Mathf.Clamp01(signedDistance + 0.5f);
            }

            private void Bounds2(float x0, float y0, float x1, float y1, System.Func<float, float, float> f)
            {
                int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0));
                int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0));
                int ix1 = Mathf.Min(size - 1, Mathf.CeilToInt(x1));
                int iy1 = Mathf.Min(size - 1, Mathf.CeilToInt(y1));

                for (int y = iy0; y <= iy1; y++)
                {
                    for (int x = ix0; x <= ix1; x++)
                        Plot(x, y, f(x + 0.5f, y + 0.5f));
                }
            }

            public Sprite ToSprite(string name)
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte a = (byte)Mathf.RoundToInt(coverage[i] * 255f);
                    pixels[i] = new Color32(255, 255, 255, a);
                }

                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                Sprite sprite = Sprite.Create(
                    tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = name;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
        }
    }
}
