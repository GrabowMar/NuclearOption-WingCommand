using System;

namespace WingCommand
{
    [Flags]
    internal enum FormationModifiers : byte { None = 0, TurnCompress = 1, Crossover = 2, TerrainFlatten = 4 }

    /// <summary>Which wings a shape suits: behind a jet, behind a helicopter, or escorting a unit.</summary>
    [Flags]
    internal enum FormationUse : byte { None = 0, Jet = 1, Rotary = 2, Escort = 4 }

    /// <summary>One wingman position relative to the leader: right and aft in spacing units, up in stack
    /// units (10 m). RollFollow ≥ 0 overrides the automatic turn-frame weight.</summary>
    internal readonly struct SlotDef
    {
        public readonly float Right, Aft, Up, RollFollow;

        public SlotDef(float right, float aft, float up, float rollFollow = -1f)
        {
            Right = right;
            Aft = aft;
            Up = up;
            RollFollow = rollFollow;
        }
    }

    /// <summary>A formation shape from data. Slots are wingmen only; the leader is at the origin.</summary>
    internal sealed class FormationDefinition
    {
        public string Id = "", Family = "", Name = "";
        public SlotDef[] Slots = Array.Empty<SlotDef>();
        /// <summary>Element index of each slot; the leader is in element 0.</summary>
        public int[] Element = Array.Empty<int>();
        public FormationModifiers Modifiers;
        /// <summary>From the shape's <c>"for"</c> list, else by family: classic suits jets and helicopters, tactical
        /// jets, rotary helicopters, escort escorting.</summary>
        public FormationUse Use = FormationUse.Jet;
        public float SpacingMin = 40f, SpacingMax = 160f, SpacingDefault = 80f;

        public float ClampSpacing(float spacing) => Scalar.Clamp(spacing, SpacingMin, SpacingMax);
    }
}
