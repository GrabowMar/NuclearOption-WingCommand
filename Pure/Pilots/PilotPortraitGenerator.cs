using System;

namespace WingCommand
{
    /// <summary>Body-specific portrait asset pool. The source art and outfit proportions differ by body.</summary>
    internal enum PortraitBody
    {
        Male = 0,
        Female = 1,
    }

    /// <summary>
    /// Persisted, editor-facing portrait choices. Values are semantic selectors, never atlas tile IDs.
    /// Hair zero means bald. The accessory argument is accepted only for old saves.
    /// </summary>
    internal readonly struct PortraitSelection : IEquatable<PortraitSelection>
    {
        public PortraitBody Body { get; }
        public int Face { get; }
        public int Hair { get; }
        public int Uniform { get; }
        public int Accessory { get; }
        public int Backdrop { get; }

        public PortraitSelection(PortraitBody body, int face, int hair, int uniform, int accessory, int backdrop)
        {
            Body = body;
            Face = face;
            Hair = hair;
            Uniform = uniform;
            Accessory = 0; // Legacy equipment is retired; old saves remain readable.
            Backdrop = backdrop;
        }

        public bool Equals(PortraitSelection other) =>
            Body == other.Body && Face == other.Face && Hair == other.Hair && Uniform == other.Uniform &&
            Accessory == other.Accessory && Backdrop == other.Backdrop;

        public override bool Equals(object obj) => obj is PortraitSelection other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Body;
                hash = hash * 31 + Face;
                hash = hash * 31 + Hair;
                hash = hash * 31 + Uniform;
                hash = hash * 31 + Accessory;
                return hash * 31 + Backdrop;
            }
        }

        public static bool operator ==(PortraitSelection left, PortraitSelection right) => left.Equals(right);
        public static bool operator !=(PortraitSelection left, PortraitSelection right) => !left.Equals(right);
    }

    /// <summary>Renderer-only atlas layer addresses resolved from a <see cref="PortraitSelection"/>.</summary>
    internal readonly struct ResolvedPortraitParts
    {
        public int FaceTile { get; }
        public int HairTile { get; }
        public int UniformTile { get; }
        public int Backdrop { get; }

        public ResolvedPortraitParts(int faceTile, int hairTile, int uniformTile, int backdrop)
        {
            FaceTile = faceTile;
            HairTile = hairTile;
            UniformTile = uniformTile;
            Backdrop = backdrop;
        }
    }

    /// <summary>Paper doll: background, gendered face, faction uniform, hair. RGBA rows run bottom-up, like Unity.</summary>
    internal static class PilotPortraitGenerator
    {
        public const int Width = 128;
        public const int Height = 192;
        public const int AtlasColumns = 4;
        public const int AtlasRows = 8;
        public const int AtlasWidth = Width * AtlasColumns;
        public const int AtlasHeight = Height * AtlasRows;
        public const int AtlasTileCount = AtlasColumns * AtlasRows;

        public const int FacesPerBody = 6;
        public const int HairCount = 7;       // 0 is bald, 1-6 are hair layers.
        public const int UniformCount = 4;
        public const int BackdropCount = 4;

        private const int MaleFaceStart = 0;
        private const int FemaleFaceStart = 6;
        private const int MaleHairStart = 12;
        private const int FemaleHairStart = 18;
        private const int MaleUniformStart = 24;
        private const int FemaleUniformStart = 28;

        public static PortraitSelection DefaultSelection =>
            new PortraitSelection(PortraitBody.Male, 0, 0, 0, 0, 0);

        public static PortraitSelection Normalize(PortraitSelection selection)
        {
            PortraitBody body = selection.Body == PortraitBody.Female ? PortraitBody.Female : PortraitBody.Male;
            return new PortraitSelection(
                body,
                Clamp(selection.Face, 0, FacesPerBody - 1),
                Clamp(selection.Hair, 0, HairCount - 1),
                Clamp(selection.Uniform, 0, UniformCount - 1),
                0,
                Clamp(selection.Backdrop, 0, BackdropCount - 1));
        }

        /// <summary>Converts the v1 global-face / raw-selector representation to v2 semantic choices.</summary>
        public static PortraitSelection FromLegacySelection(int face, int hair, int uniform, int backdrop)
        {
            PortraitBody body = face >= 3 ? PortraitBody.Female : PortraitBody.Male;
            int localFace = face >= 3 ? face - 3 : face;

            // v1 export accidentally wrote resolved tile IDs; recognize both that form and raw selectors.
            int localHair;
            if (hair >= 6 && hair <= 9)
            {
                body = PortraitBody.Male;
                localHair = hair - 6 + 1;
            }
            else if (hair >= 10 && hair <= 13)
            {
                body = PortraitBody.Female;
                localHair = hair - 10 + 1;
            }
            else
            {
                localHair = hair >= 0 && hair <= 3 ? hair + 1 : 0;
            }

            int localUniform;
            if (uniform >= 14 && uniform <= 15)
            {
                body = PortraitBody.Male;
                localUniform = uniform - 14;
            }
            else if (uniform >= 16 && uniform <= 17)
            {
                body = PortraitBody.Female;
                localUniform = uniform - 16;
            }
            else
            {
                localUniform = uniform;
            }

            return Normalize(new PortraitSelection(body, localFace, localHair, localUniform, 0, backdrop));
        }

        public static ResolvedPortraitParts Resolve(PortraitSelection selection)
        {
            selection = Normalize(selection);
            bool female = selection.Body == PortraitBody.Female;
            int face = (female ? FemaleFaceStart : MaleFaceStart) + selection.Face;
            int hair = selection.Hair == 0 ? -1 : (female ? FemaleHairStart : MaleHairStart) + selection.Hair - 1;
            int uniform = (female ? FemaleUniformStart : MaleUniformStart) + selection.Uniform;
            return new ResolvedPortraitParts(face, hair, uniform, selection.Backdrop);
        }

        public static string BodyLabel(PortraitBody body) => body == PortraitBody.Female ? "FEMALE" : "MALE";

        public static string UniformLabel(int uniform)
        {
            switch (Clamp(uniform, 0, UniformCount - 1))
            {
                case 1: return "BDF DRESS";
                case 2: return "PALA FLIGHT";
                case 3: return "PALA DRESS";
                default: return "BDF FLIGHT";
            }
        }

        public static PortraitSelection Select(string identity)
        {
            // String.GetHashCode is process-dependent; an identity must keep its look across launches.
            uint hash = 2166136261;
            foreach (char c in identity ?? "WingCommand") hash = unchecked((hash ^ c) * 16777619);
            var random = new Random(unchecked((int)hash));
            int uniform = random.Next(UniformCount);
            int face = random.Next(FacesPerBody);
            // Regional art direction is a tendency, never an exclusive faction pool.
            if (random.Next(3) != 0) face = uniform < 2 ? random.Next(3) : random.Next(3, 6);
            return new PortraitSelection(
                random.Next(2) == 0 ? PortraitBody.Male : PortraitBody.Female,
                face,
                random.Next(HairCount),
                uniform,
                0,
                random.Next(BackdropCount));
        }

        public static byte[] Compose(string identity, byte[] atlas) => Compose(Select(identity), atlas);

        public static byte[] Compose(PortraitSelection selection, byte[] atlas)
        {
            if (atlas == null || atlas.Length != AtlasWidth * AtlasHeight * 4)
                throw new ArgumentException("Expected a 512 x 1536 RGBA portrait atlas.", nameof(atlas));

            ResolvedPortraitParts parts = Resolve(selection);
            var pixels = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = (y * Width + x) * 4;
                    int light = 126 + y / 8 - Math.Abs(x - Width / 2) / 8;
                    int blueShift = parts.Backdrop == 1 ? 8 : parts.Backdrop == 2 ? 14 : 5;
                    int redShift = parts.Backdrop == 3 ? 4 : 0;
                    pixels[p] = (byte)Math.Max(0, light - 19 + redShift);
                    pixels[p + 1] = (byte)Math.Max(0, light - 5 + parts.Backdrop * 2);
                    pixels[p + 2] = (byte)Math.Min(255, light + blueShift);
                    pixels[p + 3] = 255;
                }
            }

            Layer(pixels, atlas, parts.FaceTile);
            Layer(pixels, atlas, parts.UniformTile);
            if (parts.HairTile >= 0) Layer(pixels, atlas, parts.HairTile);

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

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static void Layer(byte[] pixels, byte[] atlas, int tile)
        {
            if (tile < 0 || tile >= AtlasTileCount) return;
            int left = tile % AtlasColumns * Width;
            int bottom = AtlasHeight - (tile / AtlasColumns + 1) * Height;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int source = ((bottom + y) * AtlasWidth + left + x) * 4;
                    int target = (y * Width + x) * 4;
                    int alpha = atlas[source + 3];
                    // Empty pixels are skipped; only edges need alpha blending.
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
