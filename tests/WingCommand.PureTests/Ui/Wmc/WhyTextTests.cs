using Xunit;

namespace WingCommand.PureTests
{
    public class WhyTextTests
    {
        private static BindingReport Bank(ConstraintId by, float allowed, float requested)
        {
            var r = new BindingReport();
            r.BindBank(by, requested, allowed);
            return r;
        }

        [Fact]
        public void ALimitThatHoldsForASecondIsSaidWithItsNumbersAndHowLong()
        {
            var why = new WhyText();
            for (float t = 0f; t <= 2.5f; t += 0.1f) why.Update(Bank(ConstraintId.Terrain, 25f, 60f), t);
            Assert.Equal("BANK HELD BY THE TERRAIN FLOOR · 25° OF 60° · 2 S", why.Line(2.5f));
        }

        [Fact]
        public void AFlickerShorterThanASecondIsNotSaid()
        {
            var why = new WhyText();
            why.Update(Bank(ConstraintId.Envelope, 40f, 70f), 0f);
            why.Update(Bank(ConstraintId.Envelope, 40f, 70f), 0.5f);
            why.Update(default, 0.6f);
            Assert.Equal(WhyText.Free, why.Line(0.7f));
        }

        [Fact]
        public void GcasAndCollisionAvoidanceComeBeforeAnyLimit()
        {
            var why = new WhyText();
            var r = Bank(ConstraintId.Terrain, 25f, 60f);
            r.GcasActive = true;
            for (float t = 0f; t <= 1.5f; t += 0.1f) why.Update(r, t);
            Assert.StartsWith("GCAS PULL-UP", why.Line(1.5f));
            var c = new BindingReport { CollisionActive = true };
            var why2 = new WhyText();
            for (float t = 0f; t <= 1.5f; t += 0.1f) why2.Update(c, t);
            Assert.StartsWith("STEERING CLEAR OF A WINGMAN", why2.Line(1.5f));
        }

        [Fact]
        public void WhenTheLimitLetsGoTheLineSaysNothingHoldsIt()
        {
            var why = new WhyText();
            for (float t = 0f; t <= 2f; t += 0.1f) why.Update(Bank(ConstraintId.Terrain, 25f, 60f), t);
            why.Update(default, 2.1f);
            Assert.Equal(WhyText.Free, why.Line(2.1f));
        }

        [Fact]
        public void EveryLineFitsTheCardAndUsesOnlySafeGlyphs()
        {
            foreach (ConstraintId by in System.Enum.GetValues(typeof(ConstraintId)))
            {
                if (by == ConstraintId.None) continue;
                var why = new WhyText();
                var r = Bank(by, 25f, 60f);
                r.NzBy = by;
                r.NzAllowed = 3.5f;
                r.NzRequested = 6f;
                r.SpeedBy = by;
                r.VerticalBy = by;
                for (float t = 0f; t <= 125f; t += 0.5f) why.Update(r, t);
                string line = why.Line(125f);
                Assert.True(line.Length <= WhyText.MaxChars, line);
                foreach (char ch in line) Assert.False(ch == '…' || (ch >= '←' && ch <= '⇿') || (ch >= '■' && ch <= '◿'), line);
            }
        }

        [Fact]
        public void AReasonFromBeforeAGapInTheReportsIsForgotten()
        {
            // Review (combat): WHY kept a formation reason with a growing counter while the member settled or fought (its reports
            // stopped), and after a fight said a limit at once from its pre-combat start.
            var why = new WhyText();
            for (float t = 0f; t <= 5f; t += 0.1f) why.Update(Bank(ConstraintId.Terrain, 25f, 60f), t);
            Assert.True(why.Live(5f));
            Assert.False(why.Live(8f));
            why.Update(Bank(ConstraintId.Terrain, 25f, 60f), 70f);
            Assert.Equal(WhyText.Free, why.Line(70.5f));          // held again for less than a second
            for (float t = 70.1f; t <= 71.2f; t += 0.1f) why.Update(Bank(ConstraintId.Terrain, 25f, 60f), t);
            Assert.StartsWith("BANK HELD BY THE TERRAIN FLOOR", why.Line(71.2f));
        }
    }
}
