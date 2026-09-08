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
        public void HairAndUniformAlwaysMatchTheFacePool()
        {
            var combinations = new HashSet<(int, int, int)>();
            for (int i = 0; i < 2000; i++)
            {
                string identity = "pilot " + i;
                var parts = PilotPortraitGenerator.Select(identity);
                Assert.Equal(parts, PilotPortraitGenerator.Select(identity));
                Assert.InRange(parts.Face, 0, 5);
                if (parts.Hair != -1)
                    Assert.InRange(parts.Hair, parts.Face < 3 ? 6 : 10, parts.Face < 3 ? 9 : 13);
                Assert.InRange(parts.Uniform, parts.Face < 3 ? 14 : 16, parts.Face < 3 ? 15 : 17);
                combinations.Add((parts.Face, parts.Hair, parts.Uniform));
            }
            Assert.Equal(60, combinations.Count);
        }

        [Fact]
        public void PortraitsAreStableOpaqueAndBlendRegisteredLayersInOrder()
        {
            int width = PilotPortraitGenerator.Width, height = PilotPortraitGenerator.Height;
            var atlas = new byte[PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4];
            var blank = PilotPortraitGenerator.Compose("A. Brennan|TALLY", atlas);
            Assert.Equal(blank, PilotPortraitGenerator.Compose("A. Brennan|TALLY", atlas));
            Assert.Throws<ArgumentException>(() => PilotPortraitGenerator.Compose("test", new byte[8]));

            // Half-transparent uniform covers the face; transparent hair leaves it intact.
            for (int tile = 0; tile < 20; tile++)
            {
                int p = ((4 - tile / 4) * height * width * 4 + tile % 4 * width) * 4;
                atlas[p + ((tile >= 14 && tile < 18) ? 0 : 2)] = 240;
                atlas[p + 3] = (byte)((tile >= 14 && tile < 18) ? 128 : tile < 6 ? 255 : 0);
            }
            var output = PilotPortraitGenerator.Compose("A. Brennan|TALLY", atlas);
            Assert.Equal(output, PilotPortraitGenerator.Compose("A. Brennan|TALLY", atlas));
            Assert.Equal(width * height * 4, output.Length);
            Assert.InRange(output[0], (byte)75, (byte)100);
            Assert.InRange(output[2], (byte)85, (byte)115);
            Assert.Equal(blank[4], output[4]);
            for (int p = 3; p < output.Length; p += 4) Assert.Equal(255, output[p]);

            for (int tile = 6; tile < 14; tile++)
            {
                int p = ((4 - tile / 4) * height * width * 4 + tile % 4 * width) * 4;
                atlas[p] = 0; atlas[p + 1] = 240; atlas[p + 2] = 0; atlas[p + 3] = 255;
            }
            bool sawHair = false;
            for (int i = 0; i < 20; i++)
            {
                var topped = PilotPortraitGenerator.Compose("pilot " + i, atlas);
                if (topped[1] > 100)
                {
                    sawHair = true;
                    Assert.True(topped[1] > topped[0] * 3 && topped[1] > topped[2] * 3);
                }
            }
            Assert.True(sawHair);

            for (int face = 0; face < 6; face++)
            {
                int p = (((4 - face / 4) * height + 50) * width * 4 + face % 4 * width + 50) * 4;
                atlas[p] = (byte)(face * 40);
                atlas[p + 3] = 255;
            }
            var variants = new HashSet<byte>();
            for (int i = 0; i < 100; i++) variants.Add(PilotPortraitGenerator.Compose("pilot " + i, atlas)[(50 * width + 50) * 4]);
            Assert.Equal(6, variants.Count);
        }
    }
}
