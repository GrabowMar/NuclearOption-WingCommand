using System;

namespace WingCommand
{
    /// <summary>A portrait layer the studio steps.</summary>
    internal enum LookLayer : byte { Body, Face, Hair, Suit, Scene }

    /// <summary>The studio's operations on a draft (research squadron-studio §2.4): look layers that wrap both ways (hair 0 is bald), a
    /// random look, the face an un-customised pilot already shows (frozen when saved, so a rename never changes it), a draft compared
    /// with its record, a generated pilot, and a dialogue tag that follows a rename when it followed the callsign.</summary>
    internal static class PilotStudio
    {
        public static int Count(LookLayer layer)
        {
            switch (layer)
            {
                case LookLayer.Body: return 2;
                case LookLayer.Face: return PilotPortraitGenerator.FacesPerBody;
                case LookLayer.Hair: return PilotPortraitGenerator.HairCount;
                case LookLayer.Suit: return PilotPortraitGenerator.UniformCount;
                default: return PilotPortraitGenerator.BackdropCount;
            }
        }

        public static int Value(PortraitSelection s, LookLayer layer)
        {
            switch (layer)
            {
                case LookLayer.Body: return (int)s.Body;
                case LookLayer.Face: return s.Face;
                case LookLayer.Hair: return s.Hair;
                case LookLayer.Suit: return s.Uniform;
                default: return s.Backdrop;
            }
        }

        public static PortraitSelection Step(PortraitSelection s, LookLayer layer, int dir)
        {
            int n = Count(layer), v = ((Value(s, layer) + dir) % n + n) % n;
            return PilotPortraitGenerator.Normalize(new PortraitSelection(
                layer == LookLayer.Body ? (PortraitBody)v : s.Body,
                layer == LookLayer.Face ? v : s.Face,
                layer == LookLayer.Hair ? v : s.Hair,
                layer == LookLayer.Suit ? v : s.Uniform,
                0,
                layer == LookLayer.Scene ? v : s.Backdrop));
        }

        public static PortraitSelection RandomLook(Func<int, int> next) =>
            new PortraitSelection((PortraitBody)next(2), next(Count(LookLayer.Face)), next(Count(LookLayer.Hair)), next(Count(LookLayer.Suit)), 0,
                next(Count(LookLayer.Scene)));

        /// <summary>The look to save: the chosen one, else the face the game already showed for this name and callsign.</summary>
        public static PortraitSelection Frozen(string name, string callsign, PortraitSelection? chosen) =>
            chosen.HasValue ? PilotPortraitGenerator.Normalize(chosen.Value) : PilotPortraitGenerator.Select(name + "|" + callsign);

        /// <summary>A copy to edit (identity, look and service record).</summary>
        public static CustomPilotRecord DraftOf(CustomPilotRecord r) => r?.Clone();

        /// <summary>Whether a draft still says what its record says (identity and look; the service record is not the studio's).</summary>
        public static bool Same(CustomPilotRecord a, CustomPilotRecord b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            return a.Callsign == b.Callsign && a.Name == b.Name && a.ResolvedDialogueTag == b.ResolvedDialogueTag && a.Persona == b.Persona
                && (a.Background ?? "") == (b.Background ?? "") && a.HasCustomPortrait == b.HasCustomPortrait
                && (!a.HasCustomPortrait || a.Selection == b.Selection);
        }

        /// <summary>A tag that followed the old callsign (or was empty) follows the new one; a chosen tag stays.</summary>
        public static string TagAfterRename(string oldCallsign, string newCallsign, string tag) =>
            string.IsNullOrWhiteSpace(tag) || string.Equals(tag.Trim(), oldCallsign, StringComparison.OrdinalIgnoreCase) ? newCallsign : tag;

        /// <summary>A new pilot: a callsign not <paramref name="taken"/>, a name, a persona, a bio and a look, all inside the limits.</summary>
        public static CustomPilotRecord Generate(Func<int, int> next, Func<string, bool> taken)
        {
            var persona = (ChatterPersona)next(4);
            string callsign = PilotText.Callsign(PilotIdentity.Callsign(next, c => taken != null && taken(PilotText.Callsign(c))));
            var r = new CustomPilotRecord
            {
                Callsign = callsign, Name = PilotText.Name(PilotIdentity.Name(next)), DialogueTag = callsign, Persona = persona,
                Background = PilotText.Bio(PilotIdentity.Background(next, persona)),
            };
            r.ApplySelection(RandomLook(next));
            return r;
        }
    }
}
