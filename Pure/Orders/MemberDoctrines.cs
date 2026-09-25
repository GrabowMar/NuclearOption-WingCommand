namespace WingCommand
{
    /// <summary>Doctrine one aircraft flies when it differs from its element's (spec WMC rebuild R3; R12's member level): a
    /// base profile of its own, or sparse overrides of the per-aircraft axes (targets, reach, weapons, radar) on top of the
    /// element's. Keyed by persistent aircraft id, at most <see cref="Max"/> aircraft.</summary>
    internal sealed class MemberDoctrines
    {
        public const int Max = 8;

        private struct Slot
        {
            public uint Id;
            public bool HasBase;
            public WingDoctrine Base;
            public byte Mask;
            public WingDoctrine Over;
        }

        private readonly Slot[] slots = new Slot[Max];

        public static bool PerAircraft(DoctrineAxis axis) =>
            axis == DoctrineAxis.Targets || axis == DoctrineAxis.Reach || axis == DoctrineAxis.Weapons || axis == DoctrineAxis.Radar;

        /// <summary>What aircraft <paramref name="id"/> flies inside an element flying <paramref name="element"/>.</summary>
        public WingDoctrine Resolve(uint id, WingDoctrine element)
        {
            int i = Find(id);
            if (i < 0) return element;
            WingDoctrine d = slots[i].HasBase ? slots[i].Base : element;
            byte mask = slots[i].Mask;
            for (int a = (int)DoctrineAxis.Targets; a <= (int)DoctrineAxis.Radar; a++)
                if ((mask & (1 << a)) != 0) d = d.With((DoctrineAxis)a, slots[i].Over.Get((DoctrineAxis)a));
            return d;
        }

        /// <summary>Whether aircraft <paramref name="id"/> keeps its own value of <paramref name="axis"/> when its element's
        /// changes (an override, or a profile of its own).</summary>
        public bool Has(uint id, DoctrineAxis axis)
        {
            int i = Find(id);
            return i >= 0 && (slots[i].HasBase || (slots[i].Mask & (1 << (int)axis)) != 0);
        }

        /// <summary>False for an element-wide axis, id 0, or a ninth aircraft.</summary>
        public bool SetOverride(uint id, DoctrineAxis axis, byte value)
        {
            if (!PerAircraft(axis)) return false;
            int i = Claim(id);
            if (i < 0) return false;
            slots[i].Over = slots[i].Over.With(axis, value);
            slots[i].Mask |= (byte)(1 << (int)axis);
            return true;
        }

        /// <summary>A profile of its own; its overrides go.</summary>
        public bool SetBase(uint id, WingDoctrine d)
        {
            int i = Claim(id);
            if (i < 0) return false;
            slots[i].HasBase = true;
            slots[i].Base = d;
            slots[i].Mask = 0;
            return true;
        }

        public void Forget(uint id)
        {
            int i = Find(id);
            if (i >= 0) slots[i] = default(Slot);
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++) slots[i] = default(Slot);
        }

        private int Find(uint id)
        {
            if (id == 0u) return -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Id == id) return i;
            return -1;
        }

        private int Claim(uint id)
        {
            if (id == 0u) return -1;
            int i = Find(id);
            if (i >= 0) return i;
            for (i = 0; i < slots.Length; i++)
                if (slots[i].Id == 0u)
                {
                    slots[i].Id = id;
                    return i;
                }
            return -1;
        }
    }
}
