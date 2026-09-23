using System.Collections.Generic;

namespace WingCommand
{
    internal enum SpacingPreset : byte { Close, Standard, Open, Spread }

    /// <summary>The wing's chosen shape and spacing preset (radial "Shape" and "Spacing"). Shapes cycle within
    /// their family in catalog order; "next family" jumps to the first shape of the next family. The preset's
    /// metres are clamped to the shape's allowed spacing.</summary>
    internal sealed class FormationSelection
    {
        private readonly List<FormationDefinition> all;

        public FormationDefinition Current { get; private set; }
        public SpacingPreset Spacing = SpacingPreset.Standard;

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

        public FormationDefinition NextShape() => Step(sameFamily: true);

        public FormationDefinition NextFamily() => Step(sameFamily: false);

        private FormationDefinition Step(bool sameFamily)
        {
            int i = all.IndexOf(Current);
            for (int k = 1; k <= all.Count; k++)
            {
                FormationDefinition d = all[(i + k) % all.Count];
                if ((d.Family == Current.Family) == sameFamily)
                {
                    Current = d;
                    break;
                }
            }
            return Current;
        }
    }
}
