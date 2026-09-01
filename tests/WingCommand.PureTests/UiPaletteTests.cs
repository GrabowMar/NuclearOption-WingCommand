using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand
{
    /// <summary>
    /// The panel's palette, held to the contrast it claims.
    ///
    /// These are not style opinions. Before this suite existed the panel drew its secondary
    /// text in half-grey on an 84%-opaque ground, which measures 2.8:1 over a bright map
    /// tile — hint lines and column headers that genuinely could not be read when the panel
    /// happened to sit over snow or a city. It also drew "this control is selected" as a
    /// wash measuring 1.08:1 against the resting one, which is to say it did not draw it at
    /// all. Both were invisible in code review and needed a running mission to notice.
    ///
    /// The styles are addressed by name rather than by the enum, because these tests live
    /// beside the source they cover and its types are internal.
    /// </summary>
    public sealed class UiPaletteTests
    {
        // The stock theme colours the panel inherits, with the fallbacks UiTheme uses when
        // ThemeManager is not up yet — the values the palette is actually tuned against.
        // These two come from the live game theme, so they stay stated here.
        private static Rgba Accent => new Rgba(0.30f, 1f, 0.35f);
        private static Rgba Alert => new Rgba(1f, 0.18f, 0.12f);

        // These three are the mod's own, and are read from UiPalette rather than copied.
        // They used to be restated here, which meant the contrast floors below were asserted
        // against this file's idea of the colour rather than the one the panel draws with.
        private static Rgba Dim => UiPalette.Dim;
        private static Rgba Disabled => UiPalette.Disabled;
        private static Rgba Frame => UiPalette.Frame;

        private static UiPaletteInputs Inputs => new UiPaletteInputs
        {
            Accent = Accent,
            Alert = Alert,
            Frame = Frame,
            Dim = Dim,
            Disabled = Disabled,
        };

        /// <summary>Every style the panel draws, named so a failure says which one broke.</summary>
        public static IEnumerable<object[]> Styles => new[]
        {
            new object[] { "Default" },
            new object[] { "Primary" },
            new object[] { "Quiet" },
            new object[] { "Danger" },
            new object[] { "Tab" },
        };

        /// <summary>The styles that carry a selected state. Danger latches only to confirm.</summary>
        public static IEnumerable<object[]> SelectableStyles => new[]
        {
            new object[] { "Default" },
            new object[] { "Primary" },
            new object[] { "Quiet" },
            new object[] { "Tab" },
        };

        private static UiButtonStyle Style(string name) =>
            (UiButtonStyle)Enum.Parse(typeof(UiButtonStyle), name);

        /// <summary>A dark map tile under the panel: the best case for the ground.</summary>
        private static Rgba GroundOverDarkMap =>
            UiPalette.PanelGround.Over(new Rgba(0.05f, 0.06f, 0.07f));

        /// <summary>
        /// A bright map tile under the panel — snow, a city, a selected unit's label.
        ///
        /// This is the case that matters. The panel is translucent and floats over a moving
        /// map, so its effective ground is not one colour, and the readable-in-the-worst-case
        /// question is the only useful form of the readable question.
        /// </summary>
        private static Rgba GroundOverBrightMap =>
            UiPalette.PanelGround.Over(new Rgba(0.85f, 0.85f, 0.85f));

        // --------------------------------------------------------------------- text

        [Fact]
        public void Body_text_clears_the_readable_floor_over_any_map()
        {
            var colours = new (string Name, Rgba Colour)[]
            {
                ("Dim", Dim),
                ("Accent", Accent),
                ("White", Rgba.White),
            };

            foreach ((string name, Rgba text) in colours)
            foreach (Rgba ground in new[] { GroundOverDarkMap, GroundOverBrightMap })
            {
                float ratio = Rgba.Contrast(text, ground);
                Assert.True(ratio >= 4.5f,
                    $"{name} text measures {ratio:F2}:1 against the panel ground " +
                    $"[{ground.R:F3} {ground.G:F3} {ground.B:F3}]; 4.5:1 is the floor for " +
                    "the 10-11px text the panel spends this colour on.");
            }
        }

        /// <summary>
        /// Disabled text is deliberately below the floor — that is what disabled means —
        /// but it still has to be legible enough to read the label you cannot press.
        /// </summary>
        [Fact]
        public void Disabled_text_is_dimmed_without_disappearing()
        {
            Rgba ground = GroundOverDarkMap;
            Rgba flattened = Disabled.Over(ground);

            float disabled = Rgba.Contrast(flattened, ground);
            float live = Rgba.Contrast(Dim, ground);

            Assert.True(disabled >= 2.5f,
                $"disabled text at {disabled:F2}:1 is too faint to read");
            Assert.True(disabled < live * 0.7f,
                $"disabled text at {disabled:F2}:1 is not clearly weaker than live text at " +
                $"{live:F2}:1, so a dead control does not read as dead");
        }

        // -------------------------------------------------------------------- states

        /// <summary>
        /// The rule the palette is built on: selection fills, hover only brightens.
        ///
        /// So a selected button and a hovered one must differ in their <em>fill</em>, and a
        /// hovered one and a resting one must differ in their <em>frame</em>. If selection
        /// ever stops being visible in the fill channel, hovering the button you already
        /// chose makes it indistinguishable from any other button on the row.
        /// </summary>
        [Theory]
        [MemberData(nameof(SelectableStyles))]
        public void Selection_is_visible_as_a_fill_whether_or_not_the_pointer_is_on_it(string style)
        {
            Rgba ground = GroundOverDarkMap;
            UiButtonStyle s = Style(style);

            Rgba rest = Paint(s, latched: false, hover: false).Fill.Over(ground);
            Rgba hovered = Paint(s, latched: false, hover: true).Fill.Over(ground);
            Rgba selected = Paint(s, latched: true, hover: false).Fill.Over(ground);

            float selectedVsRest = Rgba.Contrast(selected, rest);
            Assert.True(selectedVsRest >= 1.45f,
                $"{style}: a selected fill only {selectedVsRest:F2}:1 off the resting one is " +
                "not a cue anybody sees");

            float selectedVsHover = Rgba.Contrast(selected, hovered);
            Assert.True(selectedVsHover >= 1.45f,
                $"{style}: selected and hovered fills differ by {selectedVsHover:F2}:1, so " +
                "the pointer and the choice are the same signal again");
        }

        /// <summary>
        /// Both frames are composited over the ground before being compared. A frame colour
        /// is not necessarily opaque — the recessive one the quiet styles rest in is grey at
        /// 55% — and a ratio taken against the raw value measures a colour nobody ever sees.
        /// </summary>
        [Theory]
        [MemberData(nameof(Styles))]
        public void Hover_changes_the_frame_so_the_pointer_is_always_answered(string style)
        {
            Rgba ground = GroundOverDarkMap;
            UiButtonStyle s = Style(style);

            Rgba rest = Paint(s, latched: false, hover: false).Frame.Over(ground);
            Rgba hovered = Paint(s, latched: false, hover: true).Frame.Over(ground);

            float frame = Rgba.Contrast(rest, hovered);
            Assert.True(frame >= 1.35f,
                $"{style}: resting and hovered frames are only {frame:F2}:1 apart, so the " +
                "control does not acknowledge the pointer");
        }

        /// <summary>
        /// A press has to be visible while the mouse button is down, which is the only
        /// window in which the panel confirms it heard you at all.
        /// </summary>
        [Theory]
        [MemberData(nameof(Styles))]
        public void Press_is_the_loudest_state_the_button_has(string style)
        {
            Rgba ground = GroundOverDarkMap;
            UiButtonStyle s = Style(style);

            Rgba hovered = Paint(s, latched: false, hover: true).Fill.Over(ground);
            Rgba selected = Paint(s, latched: true, hover: false).Fill.Over(ground);
            Rgba pressed = UiPalette
                .Paint(s, Inputs, enabled: true, latched: false, hover: true, pressed: true)
                .Fill.Over(ground);

            float vsHover = Rgba.Contrast(pressed, hovered);
            Assert.True(vsHover >= 1.6f, $"{style}: pressed is only {vsHover:F2}:1 off hovered");
            Assert.True(pressed.RelativeLuminance > selected.RelativeLuminance,
                $"{style}: pressed is dimmer than selected, so pressing a selected control " +
                "reads as switching it off");
        }

        /// <summary>The label has to survive whatever the fill under it is doing.</summary>
        [Theory]
        [MemberData(nameof(Styles))]
        public void Labels_stay_readable_on_every_fill_they_can_land_on(string style)
        {
            Rgba ground = GroundOverBrightMap;
            UiButtonStyle s = Style(style);

            foreach (bool latched in new[] { false, true })
            foreach (bool hover in new[] { false, true })
            foreach (bool pressed in new[] { false, true })
            {
                UiButtonPaint paint = UiPalette.Paint(
                    s, Inputs, enabled: true, latched, hover, pressed);

                float ratio = Rgba.Contrast(paint.Text, paint.Fill.Over(ground));
                Assert.True(ratio >= 4.5f,
                    $"{style} (latched={latched} hover={hover} pressed={pressed}): label " +
                    $"measures {ratio:F2}:1 on its own fill");
            }
        }

        /// <summary>
        /// A disabled control must not look like any live state, or a dead button reads as
        /// an available one and the player presses it waiting for something to happen.
        /// </summary>
        [Theory]
        [MemberData(nameof(Styles))]
        public void Disabled_ignores_hover_and_selection(string style)
        {
            UiButtonStyle s = Style(style);
            UiButtonPaint baseline = UiPalette.Paint(
                s, Inputs, enabled: false, latched: false, hover: false, pressed: false);

            foreach (bool latched in new[] { false, true })
            foreach (bool hover in new[] { false, true })
            foreach (bool pressed in new[] { false, true })
            {
                UiButtonPaint paint = UiPalette.Paint(
                    s, Inputs, enabled: false, latched, hover, pressed);

                Assert.Equal(baseline.Fill.A, paint.Fill.A, 4);
                Assert.Equal(baseline.Frame.G, paint.Frame.G, 4);
                Assert.Equal(baseline.Text.B, paint.Text.B, 4);
            }
        }

        // ---------------------------------------------------------------------- rows

        /// <summary>
        /// Roster rows are the panel's main control surface and carry no frame of their own,
        /// so their whole state vocabulary is the fill: at rest, under the pointer, and
        /// selected have to be three separable things.
        /// </summary>
        [Fact]
        public void Roster_rows_separate_rest_from_hover_from_selected()
        {
            Rgba ground = GroundOverDarkMap;

            Rgba rest = Rgba.Shade(UiPalette.RowRestShade).Over(ground);
            Rgba hover = UiPalette
                .Wash(Accent, UiPalette.RowHoverScale, UiPalette.RowHoverAlpha).Over(ground);
            Rgba selected = UiPalette
                .Wash(Accent, UiPalette.RowSelectedScale, UiPalette.RowSelectedAlpha).Over(ground);

            float hoverVsRest = Rgba.Contrast(hover, rest);
            float selectedVsHover = Rgba.Contrast(selected, hover);

            Assert.True(hoverVsRest >= 1.30f,
                $"a row lifts only {hoverVsRest:F2}:1 under the pointer, which is not enough " +
                "to say the list can be clicked at all");
            Assert.True(selectedVsHover >= 1.30f,
                $"selected and hovered rows are {selectedVsHover:F2}:1 apart, so moving the " +
                "mouse down the roster looks like changing the selection");
            Assert.True(selected.RelativeLuminance > hover.RelativeLuminance,
                "a selected row should be the brighter of the two");
        }

        // ------------------------------------------------------------------- ground

        /// <summary>
        /// The panel has to stay a panel over anything the map puts under it. At the old
        /// 84% a bright tile moved the ground far enough to take the secondary text below
        /// the readable floor, which is the whole reason the opacity went up.
        /// </summary>
        [Fact]
        public void The_panel_ground_barely_moves_with_the_map_under_it()
        {
            float drift = Rgba.Contrast(GroundOverBrightMap, GroundOverDarkMap);
            Assert.True(drift <= 1.6f,
                $"the ground shifts {drift:F2}:1 between a dark and a bright map tile; the " +
                "panel is too transparent to have a predictable palette");
        }

        // ----------------------------------------------------------------- HUD surfaces

        /// <summary>
        /// The in-cockpit HUD draws in IMGUI and cannot reach the panel's widgets, so its
        /// grounds live in the palette rather than in a private block of its own. They are
        /// held to the same floor: the radial and the toast are read at a glance, over
        /// whatever the cockpit happens to be pointing at.
        /// </summary>
        [Fact]
        public void Hud_slice_text_is_readable_on_both_slice_states()
        {
            var grounds = new (string Name, Rgba Ground)[]
            {
                ("resting slice", UiPalette.HudSliceCold),
                ("hovered slice", UiPalette.HudSliceHot),
                ("toast", UiPalette.HudPanel),
            };

            foreach ((string name, Rgba ground) in grounds)
            {
                // Over black: the HUD has no panel behind it, so a translucent ground
                // composites against the darkest thing the cockpit can show.
                Rgba flattened = ground.Over(new Rgba(0f, 0f, 0f));

                float rest = Rgba.Contrast(UiPalette.HudSliceText, flattened);
                float hot = Rgba.Contrast(Rgba.White, flattened);

                Assert.True(System.Math.Max(rest, hot) >= 4.5f,
                    $"neither HUD label colour clears 4.5:1 on the {name} " +
                    $"(rest {rest:F2}:1, hot {hot:F2}:1)");
            }
        }

        /// <summary>A hovered slice has to be visibly hotter than a resting one.</summary>
        [Fact]
        public void Hovered_hud_slice_separates_from_a_resting_one()
        {
            Rgba black = new Rgba(0f, 0f, 0f);
            Rgba rest = UiPalette.HudSliceCold.Over(black);
            Rgba hot = UiPalette.HudSliceHot.Over(black);

            float separation = Rgba.Contrast(hot, rest);
            Assert.True(separation >= 1.5f,
                $"the hovered radial slice measures {separation:F2}:1 against a resting one, " +
                "which is not enough to show which order the pointer is on");
        }

        // ------------------------------------------------------------------ helpers

        private static UiButtonPaint Paint(UiButtonStyle style, bool latched, bool hover) =>
            UiPalette.Paint(style, Inputs, enabled: true, latched, hover, pressed: false);
    }
}
