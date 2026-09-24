using System;

namespace WingCommand
{
    /// <summary>How the game's countermeasures see a missile: flares for infrared, chaff and a notch for the rest.</summary>
    internal enum MissileSeeker : byte { Radar, Infrared }

    /// <summary>The nearest missile guiding on a member (spec M5 §7.1).</summary>
    internal struct MissileThreat
    {
        public bool Present;
        /// <summary>Which missile (the caller's serial): a new one chooses its own notch side.</summary>
        public int Id;
        public Vec3 Pos, Vel;
        public MissileSeeker Seeker;
    }

    /// <summary>What the defence asks of the flight: the reference to fly, the throttle (idle or full) and the
    /// countermeasure trigger.</summary>
    internal struct DefenceCommand
    {
        public bool Active, Idle, Full, Countermeasures;
        public RefState Ref;
        public float ImpactSeconds;
    }

    /// <summary>Spec M5 §7.2: the game's own missile evasion for a member flying with the wing. After a reaction of 1 s
    /// (precision 1) to 4 s (precision 0): infrared → idle and flares on the home reference; radar → a notch
    /// perpendicular to the line of sight on the side nearer the heading (kept for the threat), <see cref="NotchDescent"/>
    /// low, full throttle, blended <see cref="EarlyBlend"/> from home while impact is beyond
    /// <see cref="EarlyImpactSeconds"/>, chaff inside <see cref="ChaffImpactSeconds"/> once within
    /// <see cref="ChaffAlignDeg"/> of the notch, a <see cref="PullUpHeight"/> pull inside
    /// <see cref="PullUpImpactSeconds"/>. With the threat gone it stays active on the home reference, trigger off, for
    /// <see cref="ClearSeconds"/> (a threat back within it needs no new reaction), then ends.</summary>
    internal sealed class MissileDefence
    {
        public static float MinReaction = 1f, MaxReaction = 4f, ClearSeconds = 1f;
        public static float NotchDistance = 1000f, NotchDescent = 300f, EarlyImpactSeconds = 7f, EarlyBlend = 0.3f;
        public static float ChaffImpactSeconds = 8f, ChaffAlignDeg = 20f, PullUpImpactSeconds = 2f, PullUpHeight = 1000f;
        /// <summary>The aggression the member flies Defend with (the envelope's bank and load ceilings).</summary>
        public static float DefendAggression = 1f;
        /// <summary>Idle only above this × the loaded minimum speed and never under GCAS (review M5c I5).</summary>
        public static float IdleMinSpeedFactor = 1.3f;
        /// <summary>Another missile replaces the one defended against only when this much closer (review M5c I2).</summary>
        public static float SwitchCloserFraction = 0.8f;

        private float seen, clear;
        private int lastId;

        public bool Active { get; private set; }
        /// <summary>The notch side for the current threat (0: none chosen).</summary>
        public int Side { get; private set; }

        /// <summary>Forget the defence (the member leaves formation flight: recovery, combat, release).</summary>
        public void End()
        {
            Active = false;
            Side = 0;
            seen = clear = 0f;
            lastId = 0;
        }

        /// <summary>Keep defending against the current missile unless another is clearly closer (squared distances).</summary>
        public static bool KeepCurrent(float currentSqr, float nearestSqr) =>
            nearestSqr >= SwitchCloserFraction * SwitchCloserFraction * currentSqr;

        public static float ReactionFor(float precision) => MaxReaction - (MaxReaction - MinReaction) * Scalar.Clamp01(precision);

        public DefenceCommand Step(in MissileThreat t, in AircraftState s, in RefState home, float precision, float dt)
        {
            if (!t.Present)
            {
                seen = 0f;
                if (!Active) return default;
                clear += dt;
                if (clear < ClearSeconds - 1e-4f) return new DefenceCommand { Active = true, Ref = home, ImpactSeconds = float.PositiveInfinity };
                Active = false;
                Side = 0;
                return default;
            }
            clear = 0f;
            // A new missile chooses its own notch side (review M5c I1); the reaction is not restarted.
            if (t.Id != lastId) Side = 0;
            lastId = t.Id;
            if (!Active)
            {
                seen += dt;
                if (seen < ReactionFor(precision) - 1e-4f) return default;
                Active = true;
            }
            Vec3 los = s.Pos - t.Pos;
            float range = Math.Max(los.Length, 1f);
            float closing = Math.Max(Vec3.Dot(los / range, t.Vel - s.Vel), 1f);
            float impact = range / closing;
            if (t.Seeker == MissileSeeker.Infrared)
                return new DefenceCommand { Active = true, Idle = true, Countermeasures = true, Ref = home, ImpactSeconds = impact };

            Vec3 heading = s.Vel.Horizontal.SqrLength > 1f ? s.Vel.Horizontal.Normalized : s.Fwd.Horizontal.Normalized;
            Vec3 toHome = home.Pos - s.Pos;
            (float nx, float nz, int side) = RadarDefenceGeometry.Notch(los.X, los.Z, heading.X, heading.Z, toHome.X, toHome.Z, true, Side);
            Side = side;
            var notch = new Vec3(nx, 0f, nz);
            float speed = Math.Max(home.Vel.Length, s.Vel.Length);
            Vec3 point, vel = notch * speed;
            if (impact < PullUpImpactSeconds) point = s.Pos + notch * NotchDistance + Vec3.Up * PullUpHeight;
            else
            {
                point = s.Pos + notch * NotchDistance - Vec3.Up * NotchDescent;
                if (impact > EarlyImpactSeconds)
                {
                    point = Vec3.Lerp(home.Pos, point, EarlyBlend);
                    vel = Vec3.Lerp(home.Vel, vel, EarlyBlend);
                }
            }
            float aligned = s.Vel.Horizontal.SqrLength > 1f ? Vec3.Dot(s.Vel.Horizontal.Normalized, notch) : -1f;
            bool chaff = impact < ChaffImpactSeconds && aligned >= (float)Math.Cos(ChaffAlignDeg * Scalar.Deg2Rad);
            return new DefenceCommand { Active = true, Full = true, Countermeasures = chaff, Ref = new RefState(point, vel, Vec3.Zero), ImpactSeconds = impact };
        }
    }
}
