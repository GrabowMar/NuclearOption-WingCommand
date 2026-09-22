using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free WCAG AA contrast and state separation tests for AvionicsTokens.
    /// Executed by both Wing Command and Boscali Summer test runners without Unity.
    /// </summary>
    public static class AvionicsTokenTests
    {
        private static Rgba Accent => new Rgba(0.30f, 1f, 0.35f);
        private static Rgba Alert => new Rgba(1f, 0.18f, 0.12f);
        private static Rgba Dim => AvTokens.TextDim;
        private static Rgba Disabled => AvTokens.TextMuted;
        private static Rgba Frame => AvTokens.Frame;

        private static AvPaletteInputs Inputs => new AvPaletteInputs
        {
            Accent = Accent,
            Alert = Alert,
            Frame = Frame,
            Dim = Dim,
            Disabled = Disabled,
        };

        private static readonly AvButtonStyle[] AllStyles =
        {
            AvButtonStyle.Default,
            AvButtonStyle.Primary,
            AvButtonStyle.Quiet,
            AvButtonStyle.Danger,
            AvButtonStyle.Tab,
            AvButtonStyle.Toggle,
        };

        private static readonly AvButtonStyle[] SelectableStyles =
        {
            AvButtonStyle.Default,
            AvButtonStyle.Primary,
            AvButtonStyle.Quiet,
            AvButtonStyle.Tab,
            AvButtonStyle.Toggle,
        };

        private static Rgba GroundOverDarkMap =>
            AvTokens.Ground.Over(new Rgba(0.05f, 0.06f, 0.07f));

        private static Rgba GroundOverBrightMap =>
            AvTokens.Ground.Over(new Rgba(0.85f, 0.85f, 0.85f));

        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestBodyText(assert);
            TestDisabledText(assert);
            TestSelectionFills(assert);
            TestHoverFrames(assert);
            TestPressLoudest(assert);
            TestLabelContrastOnAllFills(assert);
            TestDisabledIgnoresPointer(assert);
            TestRosterRows(assert);
            TestGroundDrift(assert);
            TestHudToast(assert);
            TestActionHierarchy(assert);
            TestSecondaryTextOnSurfaces(assert);
        }

        private static void TestActionHierarchy(Action<bool, string> assert)
        {
            AvButtonPaint action = AvTokens.Paint(AvButtonStyle.Default, Inputs, true, false, false, false);
            AvButtonPaint selected = AvTokens.Paint(AvButtonStyle.Toggle, Inputs, true, true, false, false);
            assert(Math.Abs(action.Frame.G - Frame.G) < 0.001f,
                "ordinary actions use a neutral frame; accent is reserved for selection and primary actions");
            assert(Math.Abs(action.Text.G - AvTokens.TextPrimary.G) < 0.001f,
                "ordinary actions have readable neutral labels");
            assert(Math.Abs(selected.Frame.G - Accent.G) < 0.001f,
                "selected controls retain the live theme accent");
        }

        private static void TestSecondaryTextOnSurfaces(Action<bool, string> assert)
        {
            foreach (Rgba surface in new[] { AvTokens.Ground, AvTokens.Surface, AvTokens.SurfaceRaised })
            {
                float ratio = Rgba.Contrast(AvTokens.TextMuted, surface.Over(GroundOverBrightMap));
                assert(ratio >= 4.5f, $"secondary text is {ratio:F2}:1 on a shared surface; must be at least 4.5:1");
            }
        }

        private static void TestBodyText(Action<bool, string> assert)
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
                assert(ratio >= 4.5f,
                    $"{name} text measures {ratio:F2}:1 against panel ground; 4.5:1 is the floor.");
            }
        }

        private static void TestDisabledText(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;
            Rgba flattened = Disabled.Over(ground);

            float disabled = Rgba.Contrast(flattened, ground);
            float live = Rgba.Contrast(Dim, ground);

            assert(disabled >= 4.5f,
                $"disabled text at {disabled:F2}:1 misses the 4.5:1 readability floor");
            assert(disabled < live * 0.7f,
                $"disabled text at {disabled:F2}:1 is not clearly weaker than live text at {live:F2}:1");
        }

        private static void TestSelectionFills(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;

            foreach (AvButtonStyle s in SelectableStyles)
            {
                Rgba rest = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: false, pressed: false).Fill.Over(ground);
                Rgba hovered = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: true, pressed: false).Fill.Over(ground);
                Rgba selected = AvTokens.Paint(s, Inputs, enabled: true, latched: true, hover: false, pressed: false).Fill.Over(ground);

                float selectedVsRest = Rgba.Contrast(selected, rest);
                assert(selectedVsRest >= 1.45f,
                    $"{s}: selected fill only {selectedVsRest:F2}:1 off resting fill (must be ≥1.45:1)");

                float selectedVsHover = Rgba.Contrast(selected, hovered);
                assert(selectedVsHover >= 1.45f,
                    $"{s}: selected and hovered fills differ by only {selectedVsHover:F2}:1 (must be ≥1.45:1)");
            }
        }

        private static void TestHoverFrames(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;

            foreach (AvButtonStyle s in AllStyles)
            {
                Rgba rest = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: false, pressed: false).Frame.Over(ground);
                Rgba hovered = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: true, pressed: false).Frame.Over(ground);

                float frame = Rgba.Contrast(rest, hovered);
                assert(frame >= 1.35f,
                    $"{s}: resting and hovered frames are only {frame:F2}:1 apart (must be ≥1.35:1)");
            }
        }

        private static void TestPressLoudest(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;

            foreach (AvButtonStyle s in AllStyles)
            {
                Rgba hovered = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: true, pressed: false).Fill.Over(ground);
                Rgba selected = AvTokens.Paint(s, Inputs, enabled: true, latched: true, hover: false, pressed: false).Fill.Over(ground);
                Rgba pressed = AvTokens.Paint(s, Inputs, enabled: true, latched: false, hover: true, pressed: true).Fill.Over(ground);

                float vsHover = Rgba.Contrast(pressed, hovered);
                assert(vsHover >= 1.6f, $"{s}: pressed is only {vsHover:F2}:1 off hovered (must be ≥1.6:1)");
                assert(pressed.RelativeLuminance > selected.RelativeLuminance,
                    $"{s}: pressed must be brighter than selected");
            }
        }

        private static void TestLabelContrastOnAllFills(Action<bool, string> assert)
        {
            Rgba ground = GroundOverBrightMap;

            foreach (AvButtonStyle s in AllStyles)
            foreach (bool latched in new[] { false, true })
            foreach (bool hover in new[] { false, true })
            foreach (bool pressed in new[] { false, true })
            {
                AvButtonPaint paint = AvTokens.Paint(s, Inputs, enabled: true, latched, hover, pressed);
                float ratio = Rgba.Contrast(paint.Text, paint.Fill.Over(ground));
                assert(ratio >= 4.5f,
                    $"{s} (latched={latched} hover={hover} pressed={pressed}): label measures {ratio:F2}:1 on fill");
            }
        }

        private static void TestDisabledIgnoresPointer(Action<bool, string> assert)
        {
            foreach (AvButtonStyle s in AllStyles)
            {
                AvButtonPaint baseline = AvTokens.Paint(s, Inputs, enabled: false, latched: false, hover: false, pressed: false);

                foreach (bool latched in new[] { false, true })
                foreach (bool hover in new[] { false, true })
                foreach (bool pressed in new[] { false, true })
                {
                    AvButtonPaint paint = AvTokens.Paint(s, Inputs, enabled: false, latched, hover, pressed);
                    assert(Math.Abs(baseline.Fill.A - paint.Fill.A) < 0.001f, $"{s}: disabled fill alpha varied");
                    assert(Math.Abs(baseline.Frame.G - paint.Frame.G) < 0.001f, $"{s}: disabled frame green varied");
                    assert(Math.Abs(baseline.Text.B - paint.Text.B) < 0.001f, $"{s}: disabled text blue varied");
                }
            }
        }

        private static void TestRosterRows(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;

            Rgba rest = AvTokens.RowFill(Accent, false).Over(ground);
            Rgba hover = AvTokens.RowFill(Accent, false, true).Over(ground);
            Rgba selected = AvTokens.RowFill(Accent, true).Over(ground);

            float hoverVsRest = Rgba.Contrast(hover, rest);
            float selectedVsHover = Rgba.Contrast(selected, hover);

            assert(hoverVsRest >= 1.30f, $"row hover lift is only {hoverVsRest:F2}:1");
            assert(selectedVsHover >= 1.30f, $"selected and hovered rows are only {selectedVsHover:F2}:1 apart");
            assert(selected.RelativeLuminance > hover.RelativeLuminance, "selected row must be brighter than hovered");
            assert(AvTokens.RowFill(Accent, true, true).RelativeLuminance > selected.RelativeLuminance,
                "a selected row must still respond to pointer hover");
        }

        private static void TestGroundDrift(Action<bool, string> assert)
        {
            float drift = Rgba.Contrast(GroundOverBrightMap, GroundOverDarkMap);
            assert(drift <= 1.6f, $"ground drift is {drift:F2}:1 (must be ≤1.6:1)");
        }

        private static void TestHudToast(Action<bool, string> assert)
        {
            Rgba flattened = AvTokens.HudPanel.Over(new Rgba(0f, 0f, 0f));
            float contrast = Rgba.Contrast(Rgba.White, flattened);
            assert(contrast >= 4.5f, $"toast text measures {contrast:F2}:1 on HUD panel");
        }
    }
}
