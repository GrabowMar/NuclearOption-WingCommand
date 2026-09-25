using System.Text;

namespace WingCommand
{
    internal enum ConstraintId : byte { None, Collision, Terrain, Envelope, Gcas, Authority }

    /// <summary>Which constraint bound the last command, per axis, with requested and allowed values.
    /// Filled on the hot path without allocation; <see cref="Describe"/> is for diagnostics only.</summary>
    internal struct BindingReport
    {
        public ConstraintId BankBy, NzBy, SpeedBy, VerticalBy;
        public float BankRequested, BankAllowed, NzRequested, NzAllowed;
        public bool GcasActive, CollisionActive;

        public void BindBank(ConstraintId by, float requested, float allowed)
        {
            if (BankBy != ConstraintId.None) return;
            BankBy = by;
            BankRequested = requested;
            BankAllowed = allowed;
        }

        public void BindNz(ConstraintId by, float requested, float allowed)
        {
            if (NzBy != ConstraintId.None) return;
            NzBy = by;
            NzRequested = requested;
            NzAllowed = allowed;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            if (BankBy != ConstraintId.None) sb.Append($"BANK {BankBy} {BankAllowed:0}/{BankRequested:0} ");
            if (NzBy != ConstraintId.None) sb.Append($"NZ {NzBy} {NzAllowed:0.0}/{NzRequested:0.0} ");
            if (VerticalBy != ConstraintId.None) sb.Append($"VERT {VerticalBy} ");
            if (SpeedBy != ConstraintId.None) sb.Append($"SPD {SpeedBy} ");
            if (CollisionActive) sb.Append("COLL ");
            if (GcasActive) sb.Append("GCAS");
            return sb.ToString().TrimEnd();
        }
    }

// Filled by the engine (M1c) and the FlightSim from terrain probes and pilot profiles.
#pragma warning disable CS0649
    /// <summary>World facts the constraints need this tick, gathered by the caller.</summary>
    internal struct LimitContext
    {
        /// <summary>Altitude of the smoothed terrain floor under and ahead of the aircraft; NaN = unknown.</summary>
        public float FloorY;
        /// <summary>Terrain under and just ahead of this aircraft (0–2 s), for GCAS; used when
        /// <see cref="HasNearFloor"/>. The look-ahead <see cref="FloorY"/> would trip GCAS on a ridge that is
        /// still 10 s away.</summary>
        public float NearFloorY;
        public bool HasNearFloor;
        public float Clearance;
        public float Aggression;
        /// <summary>Wing collision bias for this aircraft (m/s², world), added before any other constraint.</summary>
        public Vec3 CollisionBias;
    }
#pragma warning restore CS0649
}
