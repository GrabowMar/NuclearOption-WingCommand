using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class PilotGenerationTests
    {
        [Fact]
        public void RecruitmentStaysVariedAndUniqueEvenAfterCallsignPoolIsExhausted()
        {
            var random = new Random(73);
            var callsigns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TALLY", "TALLY-2" };
            var names = new HashSet<string>();
            var bios = new HashSet<string>();
            for (int i = 0; i < 1200; i++)
            {
                Assert.True(callsigns.Add(PilotIdentity.Callsign(random.Next, callsigns.Contains)));
                names.Add(PilotIdentity.Name(random.Next));
                string bio = PilotIdentity.Background(random.Next, (ChatterPersona)(i % 4));
                Assert.InRange(bio.Length, 80, 310);
                bios.Add(bio);
            }
            Assert.True(names.Count > 800);
            Assert.True(bios.Count > 1000);
        }

        [Fact]
        public void SelectionsAreStableAndResolveOnlyToTheirBodySpecificAssetPools()
        {
            var faces = new HashSet<int>();
            var facesByFaction = new[] { new HashSet<int>(), new HashSet<int>() };
            for (int i = 0; i < 2000; i++)
            {
                PortraitSelection selection = PilotPortraitGenerator.Select("pilot " + i);
                Assert.Equal(selection, PilotPortraitGenerator.Select("pilot " + i));
                Assert.InRange(selection.Face, 0, PilotPortraitGenerator.FacesPerBody - 1);
                Assert.InRange(selection.Hair, 0, PilotPortraitGenerator.HairCount - 1);
                Assert.InRange(selection.Uniform, 0, PilotPortraitGenerator.UniformCount - 1);
                Assert.Equal(0, selection.Accessory);

                ResolvedPortraitParts parts = PilotPortraitGenerator.Resolve(selection);
                bool female = selection.Body == PortraitBody.Female;
                Assert.InRange(parts.FaceTile, female ? 6 : 0, female ? 11 : 5);
                Assert.InRange(parts.UniformTile, female ? 28 : 24, female ? 31 : 27);
                if (parts.HairTile >= 0) Assert.InRange(parts.HairTile, female ? 18 : 12, female ? 23 : 17);

                faces.Add(parts.FaceTile);
                facesByFaction[selection.Uniform / 2].Add(parts.FaceTile);
            }
            Assert.Equal(12, faces.Count);
            Assert.All(facesByFaction, pool => Assert.Equal(12, pool.Count));
        }

        [Fact]
        public void LegacySelectionsMigrateToSemanticGenderedChoices()
        {
            PortraitSelection rawFemale = PilotPortraitGenerator.FromLegacySelection(4, 2, 1, 2);
            Assert.Equal(PortraitBody.Female, rawFemale.Body);
            Assert.Equal(1, rawFemale.Face);
            Assert.Equal(3, rawFemale.Hair);
            Assert.Equal(1, rawFemale.Uniform);
            Assert.Equal(2, rawFemale.Backdrop);

            PortraitSelection badExport = PilotPortraitGenerator.FromLegacySelection(1, 8, 15, 1);
            Assert.Equal(PortraitBody.Male, badExport.Body);
            Assert.Equal(3, badExport.Hair);
            Assert.Equal(1, badExport.Uniform);
            Assert.Equal(0, badExport.Accessory);
        }

        [Fact]
        public void PortraitsAreStableOpaqueAndHairIsRenderedLast()
        {
            int width = PilotPortraitGenerator.Width;
            int height = PilotPortraitGenerator.Height;
            var atlas = new byte[PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4];
            var selection = new PortraitSelection(PortraitBody.Male, 0, 1, 0, 1, 0);
            var blank = PilotPortraitGenerator.Compose(selection, atlas);
            Assert.Equal(blank, PilotPortraitGenerator.Compose(selection, atlas));
            Assert.Throws<ArgumentException>(() => PilotPortraitGenerator.Compose(selection, new byte[8]));

            ResolvedPortraitParts parts = PilotPortraitGenerator.Resolve(selection);
            SetPixel(atlas, parts.FaceTile, 50, 50, 240, 0, 0, 255);
            SetPixel(atlas, parts.UniformTile, 50, 50, 0, 240, 0, 255);
            SetPixel(atlas, parts.HairTile, 50, 50, 0, 0, 240, 255);
            var withHair = PilotPortraitGenerator.Compose(
                new PortraitSelection(PortraitBody.Male, 0, 1, 0, 0, 0), atlas);
            var withoutHair = PilotPortraitGenerator.Compose(
                new PortraitSelection(PortraitBody.Male, 0, 0, 0, 0, 0), atlas);

            int sample = (50 * width + 50) * 4;
            Assert.True(withHair[sample + 2] > withoutHair[sample + 2]);
            Assert.True(withHair[sample + 2] > withHair[sample + 1]);
            Assert.Equal(width * height * 4, withHair.Length);
            for (int p = 3; p < withHair.Length; p += 4) Assert.Equal(255, withHair[p]);
        }

        [Fact]
        public void RetiredEquipmentCannotChangeThePortraitOrHideHair()
        {
            var atlas = new byte[PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4];
            foreach (PortraitBody body in new[] { PortraitBody.Male, PortraitBody.Female })
            {
                for (int hair = 1; hair < PilotPortraitGenerator.HairCount; hair++)
                {
                    var loose = new PortraitSelection(body, 0, hair, 0, 0, 0);
                    SetPixel(atlas, PilotPortraitGenerator.Resolve(loose).HairTile, 15, 150, 255, 0, 0, 255);
                    Assert.NotEqual(PilotPortraitGenerator.Compose(new PortraitSelection(body, 0, 0, 0, 0, 0), atlas),
                        PilotPortraitGenerator.Compose(loose, atlas));
                    for (int equipment = 1; equipment <= 8; equipment++)
                    {
                        var selection = new PortraitSelection(body, 0, hair, 0, equipment, 0);
                        Assert.Equal(PilotPortraitGenerator.Compose(loose, atlas),
                            PilotPortraitGenerator.Compose(selection, atlas));
                        Assert.Equal(0, selection.Accessory);
                        Assert.Equal(hair, selection.Hair);
                    }
                }
            }
        }

        private static void SetPixel(byte[] atlas, int tile, int x, int y, byte r, byte g, byte b, byte a)
        {
            int left = tile % PilotPortraitGenerator.AtlasColumns * PilotPortraitGenerator.Width;
            int bottom = PilotPortraitGenerator.AtlasHeight -
                (tile / PilotPortraitGenerator.AtlasColumns + 1) * PilotPortraitGenerator.Height;
            int p = ((bottom + y) * PilotPortraitGenerator.AtlasWidth + left + x) * 4;
            atlas[p] = r;
            atlas[p + 1] = g;
            atlas[p + 2] = b;
            atlas[p + 3] = a;
        }
    }
}
