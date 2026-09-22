using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free tests for the <c>.avss</c> parser and cascade.
    ///
    /// The sheet is player-editable and re-readable at runtime, so its failure modes are
    /// not hypothetical: a typo has to degrade to "keep the last good sheet and say which
    /// line", never to an exception inside a paint path or a panel that renders nothing.
    /// </summary>
    public static class AvStyleTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestVariablesSubstitute(assert);
            TestColourForms(assert);
            TestThemeReferences(assert);
            TestCompoundSelectors(assert);
            TestStateBeatsBaseRegardlessOfOrder(assert);
            TestSourceOrderWithinAPass(assert);
            TestShorthandPad(assert);
            TestNamedFontSteps(assert);
            TestCommentsAndCommaSelectors(assert);
            TestMalformedInputIsSurvivable(assert);
            TestUnknownLookupsAreEmpty(assert);
        }

        private static void TestVariablesSubstitute(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ":root { ink: #EBFFF5; pad-std: 14; }" +
                ".t { color: ink; pad: pad-std; }");

            AvStyle t = s.Resolve("t");
            assert(!s.HasErrors, "a sheet using variables parses without errors");
            Near(assert, t.Color.Value.R, 0.9216f, "a variable resolves to its colour");
            Near(assert, t.PadLeft, 14f, "a variable resolves to its number");
        }

        private static void TestColourForms(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ":root { glass: #060A08 f2; }" +
                ".a { background: #0A140F; }" +
                ".b { background: #0A140F e0; }" +
                ".c { background: glass; }" +
                ".d { background: #0A140Fe0; }");

            assert(!s.HasErrors, "every colour form parses: " + string.Join("; ", s.Errors.ToArray()));
            Near(assert, s.Resolve("a").Background.Value.A, 1f, "a bare hex colour is opaque");
            Near(assert, s.Resolve("b").Background.Value.A, 0.8784f, "a trailing hex pair sets alpha");
            Near(assert, s.Resolve("c").Background.Value.A, 0.949f, "alpha survives variable expansion");
            Near(assert, s.Resolve("d").Background.Value.A, 0.8784f, "an 8-digit hex sets alpha too");
        }

        private static void TestThemeReferences(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".x { color: accent; }" +
                ".y { background: accent 40; }" +
                ".z { rail: #00FF9D; }");

            assert(s.Resolve("x").Color.Kind == AvColorRef.Accent,
                   "'accent' stays a live reference rather than being frozen to a literal");
            Near(assert, s.Resolve("y").Background.Alpha, 0.251f, "a theme reference carries an alpha");
            assert(s.Resolve("z").Rail.Kind == AvColorRef.Fixed,
                   "a rail declared as a literal stays literal, so status survives a theme change");
            Near(assert, s.Resolve("z").RailWidth, 3f, "a rail without an explicit width defaults to 3px");

            AvStyleSheet themed = AvStyleSheet.Parse(
                ":root { state-wash: accent 14; state-edge: warning 80; }" +
                ".a { background: state-wash; border: 1 state-edge; }");
            assert(!themed.HasErrors, "theme colours with alpha may be shared through variables");
            assert(themed.Resolve("a").Background.Kind == AvColorRef.Accent,
                "a theme colour alias stays live");
            Near(assert, themed.Resolve("a").Background.Alpha, 20f / 255f, "theme alias retains its alpha");
        }

        private static void TestCompoundSelectors(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".card { background: #101010; gap: 4; }" +
                ".card.locked { background: #050505; }");

            Near(assert, s.Resolve("card").Background.Value.R, 0.0627f, "the base rule applies alone");
            Near(assert, s.Resolve("card locked").Background.Value.R, 0.0196f,
                 "a compound rule applies when every one of its classes is present");
            Near(assert, s.Resolve("card locked").Gap, 4f, "the base rule still contributes to a compound match");
            Near(assert, s.Resolve("locked").Background.Value.R, 0f,
                 "a compound rule does not apply on a partial class match");
        }

        private static void TestStateBeatsBaseRegardlessOfOrder(Action<bool, string> assert)
        {
            // The hover rule is written *before* the base rule; it must still win.
            AvStyleSheet s = AvStyleSheet.Parse(
                ".row:hover { background: #FFFFFF; }" +
                ".row { background: #000000; }");

            Near(assert, s.Resolve("row").Background.Value.R, 0f, "no state means the base rule");
            Near(assert, s.Resolve("row", "hover").Background.Value.R, 1f,
                 "a state rule wins over the base rule even when it appears earlier in the file");
            Near(assert, s.Resolve("row", "armed").Background.Value.R, 0f,
                 "an unrelated state falls through to the base rule");
        }

        private static void TestSourceOrderWithinAPass(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".a { color: #111111; }" +
                ".a { color: #222222; }");

            Near(assert, s.Resolve("a").Color.Value.R, 0.1333f, "a later rule overrides an earlier one");
        }

        private static void TestShorthandPad(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".one { pad: 8; }" +
                ".two { pad: 6 12; }" +
                ".four { pad: 1 2 3 4; }");

            AvStyle one = s.Resolve("one");
            assert(one.PadTop == 8f && one.PadBottom == 8f && one.PadLeft == 8f && one.PadRight == 8f,
                   "one pad value applies to all four sides");

            AvStyle two = s.Resolve("two");
            assert(two.PadTop == 6f && two.PadBottom == 6f && two.PadLeft == 12f && two.PadRight == 12f,
                   "two pad values are vertical then horizontal");

            AvStyle four = s.Resolve("four");
            assert(four.PadTop == 1f && four.PadRight == 2f && four.PadBottom == 3f && four.PadLeft == 4f,
                   "four pad values run clockwise from the top");
        }

        private static void TestNamedFontSteps(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".t { font: title bold; }" +
                ".m { font: micro; }" +
                ".n { font: 25 bold; }");

            Near(assert, s.Resolve("t").FontSize, AvTokens.FontTitle, "a named step resolves off the token scale");
            assert(s.Resolve("t").Bold, "'bold' after a size sets the weight");
            Near(assert, s.Resolve("m").FontSize, AvTokens.FontMicro, "the micro step is the 10px floor");
            assert(!s.Resolve("m").Bold, "a size without 'bold' stays normal weight");
            Near(assert, s.Resolve("n").FontSize, 25f, "a display size can be stated outright");
        }

        private static void TestCommentsAndCommaSelectors(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                "/* the panel ground */\n" +
                ".a, .b { color: #FFFFFF; }\n" +
                "/* trailing */ .c { color: #000000; }");

            assert(!s.HasErrors, "comments and comma selectors parse cleanly");
            Near(assert, s.Resolve("a").Color.Value.R, 1f, "the first of a comma selector gets the rule");
            Near(assert, s.Resolve("b").Color.Value.R, 1f, "so does the second");
            Near(assert, s.Resolve("c").Color.Value.R, 0f, "a rule after a comment still parses");
        }

        private static void TestMalformedInputIsSurvivable(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".good { color: #FFFFFF; }\n" +
                ".bad { color: notacolour; }\n" +
                ".alsobad { nonsense: 4; }\n" +
                ".good2 { gap: 6; }");

            assert(s.HasErrors, "a malformed declaration is reported");
            assert(s.Errors.Count >= 2, "each distinct problem is reported");
            Near(assert, s.Resolve("good").Color.Value.R, 1f, "rules before the bad one still apply");
            Near(assert, s.Resolve("good2").Gap, 6f, "parsing continues past a bad declaration");

            foreach (string e in s.Errors)
                assert(e.StartsWith("line ", StringComparison.Ordinal), "every error names a line: '" + e + "'");

            AvStyleSheet unclosed = AvStyleSheet.Parse(".x { color: #FFFFFF;");
            assert(unclosed.HasErrors, "an unclosed block is an error, not an exception");

            AvStyleSheet empty = AvStyleSheet.Parse("");
            assert(!empty.HasErrors && empty.RuleCount == 0, "an empty sheet is simply empty");

            AvStyleSheet junk = AvStyleSheet.Parse("}}}{{{");
            assert(junk != null, "total junk still returns a sheet");
        }

        private static void TestUnknownLookupsAreEmpty(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(".a { color: #FFFFFF; }");

            AvStyle none = s.Resolve("does-not-exist");
            assert(!none.Color.HasValue && !none.Background.HasValue,
                   "an unknown class declares nothing, so the widget keeps its own colours");
            assert(!s.Resolve(null).Color.HasValue, "a null class set is harmless");
            Near(assert, s.Number("missing", 470f), 470f, "a missing variable falls back");
            Near(assert, s.Paint("missing", AvTokens.RailReady).Value.G, AvTokens.RailReady.G,
                "a missing colour variable preserves the supplied fallback");
        }

        private static void Near(Action<bool, string> assert, float actual, float expected, string what)
        {
            assert(Math.Abs(actual - expected) < 0.002f,
                   what + " (expected " + expected.ToString("0.####") + ", got " + actual.ToString("0.####") + ")");
        }
    }
}
