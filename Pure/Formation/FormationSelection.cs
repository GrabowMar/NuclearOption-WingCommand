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
        /// <summary>The shape last flown under each use (index by bit: jet, rotary, escort), restored on return.</summary>
        private readonly FormationDefinition[] lastByUse = new FormationDefinition[3];

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

        /// <summary>The escort shape for a wing escorting a unit: helicopters hold close-escort slots (jets among them
        /// fly high cover by role); an all-jet wing flies high cover.</summary>
        public static string EscortDefaultId(bool anyRotary) => anyRotary ? "close-escort" : "high-cover";

        /// <summary>The wing now flies behind another kind of anchor. The shape last flown under that use comes back;
        /// otherwise a current shape that does not suit it gives way to <paramref name="defaultId"/>, or to the first
        /// shape that suits it.</summary>
        public void SetUse(FormationUse use, string defaultId)
        {
            if (use == Use) return;
            lastByUse[Slot(Use)] = Current;
            Use = use;
            FormationDefinition remembered = lastByUse[Slot(use)];
            if (remembered != null && Suits(remembered))
            {
                Current = remembered;
                return;
            }
            if (Suits(Current)) return;
            FormationDefinition d = FormationCatalog.Find(all, defaultId);
            Current = d != null && Suits(d) ? d : all.Find(Suits) ?? Current;
        }

        private static int Slot(FormationUse use) => use == FormationUse.Rotary ? 1 : use == FormationUse.Escort ? 2 : 0;

        private bool Suits(FormationDefinition d) => (d.Use & Use) != 0;

        public FormationDefinition NextShape() => Step(sameFamily: true);

        public FormationDefinition NextFamily() => Step(sameFamily: false);

        /// <summary>The shapes that suit the wing's use, in catalog order (spec M7b §3 FORM).</summary>
        public void Suitable(List<FormationDefinition> into)
        {
            into.Clear();
            foreach (FormationDefinition d in all)
                if (Suits(d)) into.Add(d);
        }

        /// <summary>The families of <paramref name="shapes"/>, each once, in first-seen order.</summary>
        public static void Families(List<FormationDefinition> shapes, List<string> into)
        {
            into.Clear();
            foreach (FormationDefinition d in shapes)
                if (!into.Contains(d.Family)) into.Add(d.Family);
        }

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
