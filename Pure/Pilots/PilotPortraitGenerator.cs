using System;

namespace WingCommand
{
    /// <summary>Small paper doll: background, face, uniform, hair. RGBA rows run bottom-up, like Unity.</summary>
    internal static class PilotPortraitGenerator
    {
        public const int Width = 128;
        public const int Height = 192;
        public const int AtlasWidth = Width * 4;
        public const int AtlasHeight = Height * 5;

        public static (int Face, int Hair, int Uniform, int Backdrop) Select(string identity)
        {
            // String.GetHashCode is process-dependent; an identity must keep its face across launches.
            uint hash = 2166136261;
            foreach (char c in identity ?? "WingCommand") hash = unchecked((hash ^ c) * 16777619);
            var random = new Random(unchecked((int)hash));
            int face = random.Next(6), hair = random.Next(5), uniform = random.Next(2);
            return (face, hair == 4 ? -1 : (face < 3 ? 6 : 10) + hair,
                    (face < 3 ? 14 : 16) + uniform, random.Next(3));
        }

        public static byte[] Compose(string identity, byte[] atlas)
        {
            if (atlas == null || atlas.Length != AtlasWidth * AtlasHeight * 4)
                throw new ArgumentException("Expected a 512 x 960 RGBA portrait atlas.", nameof(atlas));
            var parts = Select(identity);
            int backdrop = parts.Backdrop;
            var pixels = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = (y * Width + x) * 4;
                    int light = 130 + y / 7 - Math.Abs(x - Width / 2) / 8;
                    pixels[p] = (byte)(light - 17 + backdrop * 3);
                    pixels[p + 1] = (byte)(light - 3 + backdrop * 2);
                    pixels[p + 2] = (byte)(light + 6);
                    pixels[p + 3] = 255;
                }
            }
            Layer(pixels, atlas, parts.Face);
            Layer(pixels, atlas, parts.Uniform);
            if (parts.Hair >= 0) Layer(pixels, atlas, parts.Hair);

            // Preserve facial contrast at the small UI size. Baked scanlines alias when
            // the panel scales and obscure eyes and mouths in the supply thumbnail.
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = (y * Width + x) * 4;
                    int r = pixels[p], g = pixels[p + 1], b = pixels[p + 2];
                    int gray = (r * 30 + g * 59 + b * 11) / 100;
                    pixels[p] = (byte)((r * 2 + gray) / 3 * 91 / 100);
                    pixels[p + 1] = (byte)((g * 2 + gray) / 3);
                    pixels[p + 2] = (byte)Math.Min(255, (b * 2 + gray) / 3 + 9);
                }
            }
            return pixels;
        }

        private static void Layer(byte[] pixels, byte[] atlas, int tile)
        {
            int left = tile % 4 * Width;
            int bottom = AtlasHeight - (tile / 4 + 1) * Height;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int source = ((bottom + y) * AtlasWidth + left + x) * 4;
                    int target = (y * Width + x) * 4;
                    int alpha = atlas[source + 3];
                    // Most of a wig tile is empty; only edge pixels need alpha blending.
                    if (alpha == 0) continue;
                    if (alpha == 255)
                    {
                        pixels[target] = atlas[source];
                        pixels[target + 1] = atlas[source + 1];
                        pixels[target + 2] = atlas[source + 2];
                        continue;
                    }
                    for (int c = 0; c < 3; c++)
                        pixels[target + c] = (byte)((atlas[source + c] * alpha + pixels[target + c] * (255 - alpha) + 127) / 255);
                }
            }
        }
    }
}
