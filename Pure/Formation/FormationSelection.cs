using System.Collections.Generic;

namespace WingCommand
{
    internal enum SpacingPreset : byte { Close, Standard, Open, Spread }

    /// <summary>The wing's chosen shape and spacing preset (radial "Shape" and "Spacing"). Shapes cycle within
    /// their family in catalog order; "next family" jumps to the first shape of the next family. Only shapes that
    /// suit the wing's <see cref="Use"/> (behind a jet, behind a helicopter, escorting) take part. The preset's
    /// metres are clamped to the shape's allowed spacing.</summary>
    internal sealed class FormationSelection
    {
        private readonly List<FormationDefinition> all;

        public FormationDefinition Current { get; private set; }
        public SpacingPreset Spacing = SpacingPreset.Standard;
        public FormationUse Use { get; private set; } = FormationUse.Jet;

        public FormationSelection(List<FormationDefinition> catalog, string defaultId)
        {
            all = catalog;
            Current = FormationCatalog.Find(all, defaultId) ?? all[0];
        }

        public float SpacingMetres => Current.ClampSpacing(Metres(Spacing));

        public static float Metres(SpacingPreset p)
        {
            switch (p)
            {
                case SpacingPreset.Close: return FormationCatalog.Close;
                case SpacingPreset.Open: return FormationCatalog.Open;
                case SpacingPreset.Spread: return FormationCatalog.Spread;
                default: return FormationCatalog.Standard;
            }
        }

        /// <summary>Picks a shape by id; an unknown id leaves the current shape and returns false.</summary>
        public bool Select(string id)
        {
            FormationDefinition d = FormationCatalog.Find(all, id);
            if (d == null) return false;
            Current = d;
            return true;
        }

        /// <summary>The wing now flies behind another kind of anchor. A current shape that does not suit it gives way to
        /// <paramref name="defaultId"/>, or to the first shape that suits it.</summary>
        public void SetUse(FormationUse use, string defaultId)
        {
            Use = use;
            if (Suits(Current)) return;
            FormationDefinition d = FormationCatalog.Find(all, defaultId);
            Current = d != null && Suits(d) ? d : all.Find(Suits) ?? Current;
        }

        private bool Suits(FormationDefinition d) => (d.Use & Use) != 0;

        public FormationDefinition NextShape() => Step(sameFamily: true);

        public FormationDefinition NextFamily() => Step(sameFamily: false);

        private FormationDefinition Step(bool sameFamily)
        {
            int i = all.IndexOf(Current);
            for (int k = 1; k <= all.Count; k++)
            {
                FormationDefinition d = all[(i + k) % all.Count];
                if (Suits(d) && (d.Family == Current.Family) == sameFamily)
                {
                    Current = d;
                    break;
                }
            }
            return Current;
        }
    }
}
