using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>INSPECT's WHY line (spec bezel v2 §5 INSPECT, I1): what holds the aircraft back and for how long, from its flight
    /// pipeline's <see cref="BindingReport"/> — GCAS and collision avoidance first, then the bank, the pull, the climb and the speed
    /// limits with the constraint that set them. A limit is said once it has held for <see cref="HoldSeconds"/> (the report changes
    /// every tick; a flicker is not a reason).</summary>
    internal sealed class WhyText
    {
        public const float HoldSeconds = 1f;
        public const int MaxChars = 60;
        public const string Free = "NOTHING LIMITS IT";
        /// <summary>Reports further apart than this (s) mean the member left formation flight (a fight, a settle, a recovery): what
        /// held before is forgotten.</summary>
        public const float GapSeconds = 0.5f;

        private const int Gcas = 0, Collision = 1, Bank = 2, Nz = 3, Vertical = 4, Speed = 5, Kinds = 6;
        private readonly float[] since = { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN };
        private readonly ConstraintId[] by = new ConstraintId[Kinds];
        private BindingReport last;
        private float lastUpdate = float.NaN;

        /// <summary>Reports are coming (the member flies in formation now).</summary>
        public bool Live(float time) => !float.IsNaN(lastUpdate) && time - lastUpdate <= GapSeconds;

        public void Update(in BindingReport r, float time)
        {
            if (!float.IsNaN(lastUpdate) && time - lastUpdate > GapSeconds)
                for (int k = 0; k < Kinds; k++)
                {
                    since[k] = float.NaN;
                    by[k] = ConstraintId.None;
                }
            lastUpdate = time;
            Track(Gcas, r.GcasActive ? ConstraintId.Gcas : ConstraintId.None, time);
            Track(Collision, r.CollisionActive ? ConstraintId.Collision : ConstraintId.None, time);
            Track(Bank, r.BankBy, time);
            Track(Nz, r.NzBy, time);
            Track(Vertical, r.VerticalBy, time);
            Track(Speed, r.SpeedBy, time);
            last = r;
        }

        private void Track(int kind, ConstraintId now, float time)
        {
            if (now == ConstraintId.None)
            {
                since[kind] = float.NaN;
                by[kind] = ConstraintId.None;
                return;
            }
            if (now != by[kind] || float.IsNaN(since[kind])) since[kind] = time;
            by[kind] = now;
        }

        /// <summary>The kind said now (−1: none) and for how many whole seconds: a page's change key.</summary>
        public int Key(float time)
        {
            int k = Said(time);
            return k < 0 ? -1 : k * 100000 + (int)(time - since[k]) * 8 + (int)by[k];
        }

        private int Said(float time)
        {
            for (int k = 0; k < Kinds; k++)
                if (!float.IsNaN(since[k]) && time - since[k] >= HoldSeconds) return k;
            return -1;
        }

        public string Line(float time)
        {
            int k = Said(time);
            if (k < 0) return Free;
            string held = " · " + ((int)(time - since[k])).ToString(CultureInfo.InvariantCulture) + " S";
            switch (k)
            {
                case Gcas: return "GCAS PULL-UP" + held;
                case Collision: return "STEERING CLEAR OF A WINGMAN" + held;
                case Bank: return "BANK HELD BY " + Who(by[k]) + " · " + Deg(last.BankAllowed) + " OF " + Deg(last.BankRequested) + held;
                case Nz: return "PULL HELD BY " + Who(by[k]) + " · " + G(last.NzAllowed) + " OF " + G(last.NzRequested) + held;
                case Vertical: return "CLIMB HELD BY " + Who(by[k]) + held;
                default: return "SPEED HELD BY " + Who(by[k]) + held;
            }
        }

        private static string Who(ConstraintId c)
        {
            switch (c)
            {
                case ConstraintId.Terrain: return "THE TERRAIN FLOOR";
                case ConstraintId.Envelope: return "ITS ENVELOPE";
                case ConstraintId.Gcas: return "GCAS";
                case ConstraintId.Authority: return "ROLL AUTHORITY";
                case ConstraintId.Collision: return "COLLISION AVOIDANCE";
                default: return "A LIMIT";
            }
        }

        private static string Deg(float v) => ((int)Math.Round(Math.Abs(v))).ToString(CultureInfo.InvariantCulture) + "°";

        private static string G(float v) => v.ToString("0.0", CultureInfo.InvariantCulture) + " G";
    }
}
