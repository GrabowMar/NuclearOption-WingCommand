using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>R7: the studio's look steppers and draft rules (research squadron-studio §2.4, §3).</summary>
    public class PilotStudioTests
    {
        private static readonly PortraitSelection Start = new PortraitSelection(PortraitBody.Male, 2, 3, 1, 0, 2);

        [Theory]
        [InlineData((int)LookLayer.Face, 5, 1, 0)]
        [InlineData((int)LookLayer.Face, 0, -1, 5)]
        [InlineData((int)LookLayer.Hair, 6, 1, 0)]
        [InlineData((int)LookLayer.Hair, 0, -1, 6)]
        [InlineData((int)LookLayer.Suit, 3, 1, 0)]
        [InlineData((int)LookLayer.Scene, 0, -1, 3)]
        public void EachPortraitLayerWrapsBothWays(int layerIndex, int from, int dir, int to)
        {
            var layer = (LookLayer)layerIndex;
            var s = new PortraitSelection(PortraitBody.Male, layer == LookLayer.Face ? from : 0, layer == LookLayer.Hair ? from : 0,
                layer == LookLayer.Suit ? from : 0, 0, layer == LookLayer.Scene ? from : 0);
            Assert.Equal(to, PilotStudio.Value(PilotStudio.Step(s, layer, dir), layer));
        }

        [Fact]
        public void ChangingTheBodyKeepsFaceHairSuitAndScene()
        {
            PortraitSelection s = PilotStudio.Step(Start, LookLayer.Body, 1);
            Assert.Equal(PortraitBody.Female, s.Body);
            Assert.Equal((2, 3, 1, 2), (s.Face, s.Hair, s.Uniform, s.Backdrop));
            Assert.Equal(PortraitBody.Male, PilotStudio.Step(s, LookLayer.Body, 1).Body);
        }

        [Fact]
        public void ARandomLookStaysInsideEveryLayersRange()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var rng = new System.Random(seed);
                PortraitSelection s = PilotStudio.RandomLook(rng.Next);
                Assert.Equal(s, PilotPortraitGenerator.Normalize(s));
            }
        }

        [Fact]
        public void AnUncustomisedPilotKeepsTheFaceTheGameAlreadyShowed()
        {
            Assert.Equal(PilotPortraitGenerator.Select("O. Bae|HATCH"), PilotStudio.Frozen("O. Bae", "HATCH", null));
            Assert.Equal(Start, PilotStudio.Frozen("O. Bae", "HATCH", Start));
        }

        [Fact]
        public void ADraftDiffersFromItsRecordAfterAnyEditAndMatchesAgainWhenReverted()
        {
            var record = new CustomPilotRecord { Callsign = "HATCH", Name = "O. Bae", Persona = ChatterPersona.Calm, Background = "b" };
            record.ApplySelection(Start);
            CustomPilotRecord draft = PilotStudio.DraftOf(record);
            Assert.True(PilotStudio.Same(draft, record));
            draft.Persona = ChatterPersona.Dry;
            Assert.False(PilotStudio.Same(draft, record));
            draft = PilotStudio.DraftOf(record);
            draft.ApplySelection(PilotStudio.Step(Start, LookLayer.Hair, 1));
            Assert.False(PilotStudio.Same(draft, record));
            Assert.True(PilotStudio.Same(PilotStudio.DraftOf(record), record));
        }

        [Fact]
        public void ATagThatFollowedTheCallsignFollowsARename()
        {
            Assert.Equal("ZULU", PilotStudio.TagAfterRename("HATCH", "ZULU", "HATCH"));
            Assert.Equal("ZULU", PilotStudio.TagAfterRename("HATCH", "ZULU", null));
            Assert.Equal("ACE", PilotStudio.TagAfterRename("HATCH", "ZULU", "ACE"));
        }

        [Fact]
        public void EveryGeneratedDraftFitsTheLimits()
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                var rng = new System.Random(seed);
                CustomPilotRecord d = PilotStudio.Generate(rng.Next, _ => false);
                Assert.Equal(d.Callsign, PilotText.Callsign(d.Callsign));
                Assert.Equal(d.Name, PilotText.Name(d.Name));
                Assert.Equal(d.Background, PilotText.Bio(d.Background));
                Assert.True(d.HasCustomPortrait);
            }
        }
    }
}
