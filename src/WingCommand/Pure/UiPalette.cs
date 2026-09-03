using System;

namespace WingCommand
{
    /// <summary>
    /// A colour, independent of Unity.
    ///
    /// The panel's palette decisions — is a selected control actually distinguishable from
    /// a resting one, is ten-pixel grey text readable on the panel ground — are arithmetic,
    /// and arithmetic that was previously only checkable by launching the game and looking
    /// at it. Keeping the maths on a plain struct puts it in <c>WingCommand.PureTests</c>
    /// alongside the rest of the logic that does not need a scene to be true.
    /// </summary>
    internal struct Rgba
    {
        public readonly float R, G, B, A;

        public Rgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Rgba WithAlpha(float a) => new Rgba(R, G, B, a);

        /// <summary>This colour scaled towards black, keeping its hue. The wash of a tint.</summary>
        public Rgba Scaled(float factor) => new Rgba(R * factor, G * factor, B * factor, A);

        public static Rgba Lerp(Rgba from, Rgba to, float t) =>
            new Rgba(from.R + (to.R - from.R) * t,
                     from.G + (to.G - from.G) * t,
                     from.B + (to.B - from.B) * t,
                     from.A + (to.A - from.A) * t);

        /// <summary>Composite this colour, at its own alpha, over an opaque background.</summary>
        public Rgba Over(Rgba background) =>
            new Rgba(R * A + background.R * (1f - A),
                     G * A + background.G * (1f - A),
                     B * A + background.B * (1f - A),
                     1f);

        /// <summary>WCAG relative luminance, treating the channels as sRGB.</summary>
        public float RelativeLuminance =>
            0.2126f * Linear(R) + 0.7152f * Linear(G) + 0.0722f * Linear(B);

        private static float Linear(float c) =>
            c <= 0.03928f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);

        /// <summary>
        /// WCAG contrast between two opaque colours. Composite anything translucent with
        /// <see cref="Over"/> first — a ratio taken against a colour that is only half there
        /// is a ratio against something nobody sees.
        /// </summary>
        public static float Contrast(Rgba a, Rgba b)
        {
            float la = a.RelativeLuminance;
            float lb = b.RelativeLuminance;
            if (la < lb) { float t = la; la = lb; lb = t; }
            return (la + 0.05f) / (lb + 0.05f);
        }

        public static Rgba White => new Rgba(1f, 1f, 1f);
        public static Rgba Shade(float alpha) => new Rgba(0f, 0f, 0f, alpha);
    }

    /// <summary>How a control is weighted against the others around it.</summary>
    internal enum UiButtonStyle
    {
        /// <summary>An ordinary action. Outlined in the accent colour at rest.</summary>
        Default,

        /// <summary>The one action a page exists for. Carries a fill at rest.</summary>
        Primary,

        /// <summary>Plumbing — page arrows and steppers. Recedes until pointed at.</summary>
        Quiet,

        /// <summary>Removes something. Turns alert-coloured under the pointer.</summary>
        Danger,

        /// <summary>A page selector. Lit and underlined while its page is showing.</summary>
        Tab,
    }

    /// <summary>The three colours one button state resolves to.</summary>
    internal struct UiButtonPaint
    {
        public Rgba Fill;
        public Rgba Frame;
        public Rgba Text;
    }

    /// <summary>The theme colours the palette is built out of.</summary>
    internal struct UiPaletteInputs
    {
        public Rgba Accent;
        public Rgba Alert;
        public Rgba Frame;
        public Rgba Dim;
        public Rgba Disabled;
    }

    /// <summary>
    /// Which colours a control wears, given everything currently true about it.
    ///
    /// The rule the whole set is built on: <em>selection fills, hover only brightens.</em>
    /// The panel previously drew both as a white frame, so a lit ROE button and whichever
    /// button the mouse happened to be resting on were the same control as far as the eye
    /// was concerned. Splitting the two cues onto different channels — area for state,
    /// edge for the pointer — is what makes them readable at the same time, which matters
    /// because a player hovering a button is usually hovering the one they already chose.
    ///
    /// The tint strengths below are not guesses; <c>UiPaletteTests</c> pins each one to a
    /// measured contrast band against the panel ground it is drawn on.
    /// </summary>
    internal static class UiPalette
    {
        /// <summary>
        /// The panel ground, as composited over a dark map. Everything else is measured
        /// against this, so it lives here rather than in the Unity-side drawing code.
        /// </summary>
        public static readonly Rgba PanelGround = new Rgba(0.022f, 0.040f, 0.032f, 0.950f);

        /// <summary>Secondary text: hint lines, column headers, table cells.</summary>
        public static readonly Rgba Dim = new Rgba(0.62f, 0.72f, 0.67f);

        /// <summary>Text and edges of a control that is present but currently inert.</summary>
        public static readonly Rgba Disabled = new Rgba(0.34f, 0.44f, 0.40f, 0.75f);

        /// <summary>A frame that should recede behind its contents.</summary>
        public static readonly Rgba Frame = new Rgba(0.22f, 0.44f, 0.35f, 0.55f);

        public static readonly Rgba PanelEdge = new Rgba(0.18f, 0.55f, 0.38f, 1f);
        public static readonly Rgba PanelShadow = new Rgba(0.02f, 0.05f, 0.03f, 1f);

        // 3-layer design tokens (Surfaces, Borders, Rails, Typography)
        public static readonly Rgba SurfaceCard = new Rgba(0.038f, 0.080f, 0.060f, 0.88f);
        public static readonly Rgba BorderSubtle = new Rgba(0.18f, 0.65f, 0.42f, 0.30f);
        public static readonly Rgba RailEmerald = new Rgba(0.000f, 1.000f, 0.616f);
        public static readonly Rgba RailCyan = new Rgba(0.000f, 0.898f, 1.000f);
        public static readonly Rgba TextPrimary = new Rgba(0.92f, 1.00f, 0.96f);

        // The in-cockpit HUD's own surfaces. It draws in IMGUI rather than uGUI and cannot
        // reach WingUi's widgets, but there is no reason for it to hold a second, private
        // idea of what the mod's colours are — which is how its accent came to be a
        // hand-copied duplicate of UiTheme.Friendly's fallback.

        /// <summary>The toast ground.</summary>
        public static readonly Rgba HudPanel = new Rgba(0.04f, 0.06f, 0.05f, 0.78f);

        // Fill washes, as (scale towards black, alpha). Together they set how far a filled
        // state moves off the resting ground. Selection has to clear the resting fill by
        // enough to be seen without lighting the button so brightly that the label on top of
        // it stops being readable, which is the band the tests hold these to.
        private const float RestShade = 0.30f;
        private const float QuietRestShade = 0.22f;
        private const float DangerRestShade = 0.26f;

        /// <summary>
        /// How far a resting accent frame is held back from full strength.
        ///
        /// Full-intensity green against white is only 1.33:1 — bright green is already most
        /// of the way to white in luminance — so a button whose whole hover cue was "the
        /// green frame turns white" was barely acknowledging the pointer at all, and the
        /// panel was simultaneously a wall of controls all outlined at maximum intensity.
        /// Seating the resting frame slightly deeper costs nothing at rest and roughly
        /// doubles the hover step.
        /// </summary>
        private const float RestFrameScale = 0.80f;

        private const float SelectedScale = 0.34f;
        private const float SelectedAlpha = 0.80f;

        private const float PressedScale = 0.52f;
        private const float PressedAlpha = 0.90f;

        private const float PrimaryRestScale = 0.19f;
        private const float PrimaryRestAlpha = 0.66f;

        private const float SubtleScale = 0.27f;
        private const float SubtleAlpha = 0.74f;

        private const float DangerHoverScale = 0.26f;
        private const float DangerHoverAlpha = 0.62f;

        // A roster row has no frame of its own to carry state, so its fills have to do more
        // work than a button's and are pitched correspondingly further apart.

        /// <summary>A list row the pointer is over.</summary>
        public const float RowHoverScale = 0.28f;
        public const float RowHoverAlpha = 0.66f;

        /// <summary>A list row that is currently selected.</summary>
        public const float RowSelectedScale = 0.36f;
        public const float RowSelectedAlpha = 0.82f;

        /// <summary>A list row at rest: the same near-black hole every framed box has.</summary>
        public const float RowRestShade = RestShade;

        /// <summary>The wash a tint produces, ready to be drawn over the panel ground.</summary>
        public static Rgba Wash(Rgba accent, float scale, float alpha) =>
            accent.Scaled(scale).WithAlpha(alpha);

        public static UiButtonPaint Paint(UiButtonStyle style, UiPaletteInputs colors,
                                          bool enabled, bool latched, bool hover, bool pressed)
        {
            var paint = new UiButtonPaint();

            if (!enabled)
            {
                // Reduced emphasis and no state of its own: a disabled control should look
                // like it is not participating, not like a differently-coloured live one.
                paint.Fill = Rgba.Shade(0.18f);
                paint.Frame = colors.Disabled;
                paint.Text = colors.Disabled;
                return paint;
            }

            Rgba accent = style == UiButtonStyle.Danger ? colors.Alert : colors.Accent;

            if (pressed)
            {
                // The press cue is the loudest thing the button ever does, and it is the
                // only feedback between clicking and the order actually going out.
                paint.Fill = Wash(accent, PressedScale, PressedAlpha);
                paint.Frame = Rgba.White;
                paint.Text = Rgba.White;
                return paint;
            }

            switch (style)
            {
                case UiButtonStyle.Primary:
                    paint.Fill = latched
                        ? Wash(accent, SelectedScale, SelectedAlpha)
                        : Wash(accent, PrimaryRestScale, PrimaryRestAlpha);
                    paint.Frame = hover ? Rgba.White : accent.Scaled(RestFrameScale);
                    paint.Text = hover ? Rgba.White : Rgba.Lerp(accent, Rgba.White, 0.35f);
                    break;

                case UiButtonStyle.Quiet:
                    paint.Fill = latched
                        ? Wash(accent, SubtleScale, SubtleAlpha)
                        : Rgba.Shade(QuietRestShade);
                    paint.Frame = hover ? accent : colors.Frame;
                    paint.Text = hover ? accent : colors.Dim;
                    break;

                case UiButtonStyle.Danger:
                    paint.Fill = latched ? Wash(accent, SelectedScale, SelectedAlpha)
                               : hover ? Wash(accent, DangerHoverScale, DangerHoverAlpha)
                               : Rgba.Shade(DangerRestShade);
                    paint.Frame = latched || hover ? accent : colors.Frame;
                    paint.Text = latched ? Rgba.White : hover ? accent : colors.Dim;
                    break;

                case UiButtonStyle.Tab:
                    paint.Fill = latched
                        ? Wash(accent, SubtleScale, SubtleAlpha)
                        : Rgba.Shade(QuietRestShade);
                    paint.Frame = latched || hover ? accent : colors.Frame;
                    paint.Text = latched ? Rgba.White : hover ? accent : colors.Dim;
                    break;

                default:
                    paint.Fill = latched
                        ? Wash(accent, SelectedScale, SelectedAlpha)
                        : Rgba.Shade(RestShade);
                    paint.Frame = hover ? Rgba.White : accent.Scaled(RestFrameScale);
                    paint.Text = hover || latched ? Rgba.White : accent;
                    break;
            }

            return paint;
        }
    }
}
