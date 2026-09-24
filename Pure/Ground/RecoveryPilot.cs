using System;

namespace WingCommand
{
    internal enum RecoveryIntent : byte { Rtb, Refit }

    internal enum RecoveryPhase : byte { Approach, Landing, Ground, Servicing, Departing, Reserve, Released, Done }

    /// <summary>What the engine must do for a recovering member this tick.</summary>
    internal enum RecoveryAction : byte { None, Reserve, Service }

    /// <summary>One member's way home and back (spec M3 §4).
    /// <list type="bullet">
    /// <item>Approach: the flight pipeline flies to the approach point — a jet <see cref="ApproachDistance"/> out on the
    /// landing runway's approach side (landing the way departures roll) at <see cref="ApproachHeight"/> above it, a
    /// helicopter <see cref="HeloApproachDistance"/> from the field centre on its side at
    /// <see cref="HeloApproachHeight"/>; each member of the wing one <see cref="StackStep"/> above the one before. Within
    /// <see cref="ApproachReached"/> of it the engine hands it to the game's landing state — a jet only when the field's
    /// runway is its turn (<see cref="FieldTraffic.TryClaimLanding"/>); a jet waiting for its turn orbits the point
    /// (latched while within the orbit's reach, and handed over from anywhere on it), a helicopter hovers there.</item>
    /// <item>Landing: owned by the game. A failed landing (abort, no runway, overdue after
    /// <see cref="LandingSeconds"/>) goes back to the approach and waits <see cref="RetrySeconds"/> before it tries
    /// again; the <see cref="MaxLandingTries"/>th releases it.</item>
    /// <item>Ground: after touchdown a ground pilot on the field it landed at — a jet taxis to a stand, a helicopter
    /// stands where it landed. On the stand an RTB member goes back to the reserve; a refit member is serviced for
    /// <see cref="RefitTimer"/> seconds (the engine refuels and rearms it), then departs again (Departing) and is
    /// Done once airborne.</item>
    /// </list></summary>
    internal sealed class RecoveryPilot
    {
        public static float ApproachDistance = 5000f, ApproachHeight = 450f, ApproachReached = 800f;
        public static float HeloApproachDistance = 1500f, HeloApproachHeight = 150f, LandingSeconds = 300f;
        public static float StackStep = 150f, HeloStackStep = 40f, RetrySeconds = 20f;
        public static int MaxLandingTries = 3;

        public readonly RecoveryIntent Intent;
        public readonly FieldTraffic Field;
        public RecoveryPhase Phase { get; private set; } = RecoveryPhase.Approach;
        public int FailedLandings { get; private set; }
        /// <summary>The ground pilot from touchdown on (null before).</summary>
        public GroundPilot Ground { get; private set; }

        private readonly int owner, stack;
        private readonly AirframeClass cls;
        private float landingSince = float.NaN, serviceUntil = float.NaN, retryAt = float.NegativeInfinity;
        private HoldOrbit hold;
        private bool holding, noStand;

        /// <summary>Holding at the approach point for its turn (diagnostics).</summary>
        public bool Holding => holding;

        /// <param name="stack">The member's level in the approach stack (its slot).</param>
        public RecoveryPilot(int owner, FieldTraffic field, AirframeClass cls, RecoveryIntent intent, int stack = 0)
        {
            this.owner = owner;
            Field = field;
            this.cls = cls;
            Intent = intent;
            this.stack = Math.Max(0, stack);
        }

        /// <summary>A member recalled on the ground (already turned round by its ground pilot): recovered from there.</summary>
        public static RecoveryPilot FromGround(int owner, GroundPilot ground, RecoveryIntent intent, AirframeClass cls = AirframeClass.FixedWing) =>
            new RecoveryPilot(owner, ground.Field, cls, intent) { Ground = ground, Phase = RecoveryPhase.Ground };

        private bool Vertical => cls != AirframeClass.FixedWing;

        public Vec3 ApproachPoint(in AircraftState s)
        {
            if (Vertical)
            {
                Vec3 centre = Field.Field.Center, away = (s.Pos - centre).Horizontal;
                away = away.SqrLength > 1e-4f ? away.Normalized : Vec3.Forward;
                Vec3 p = centre + away * HeloApproachDistance;
                return new Vec3(p.X, centre.Y + HeloApproachHeight + HeloStackStep * stack, p.Z);
            }
            RunwaySample r = Field.Runway;
            Vec3 threshold = Field.Reverse ? r.End : r.Start, dir = r.Direction(Field.Reverse);
            Vec3 point = threshold - dir * ApproachDistance;
            return new Vec3(point.X, threshold.Y + ApproachHeight + StackStep * stack, point.Z);
        }

        /// <summary>The approach for the flight pipeline; <paramref name="handOver"/> once close enough to land, the retry
        /// wait is over and (a jet) the runway is its turn. Until then it holds at the point.</summary>
        public FlightIntent ApproachIntent(in AircraftState s, AirframeProfile p, float time, float dt, out bool handOver)
        {
            Vec3 point = ApproachPoint(s);
            Vec3 to = (point - s.Pos).Horizontal;
            // Holding is latched: the orbit is wider than the reach, so it holds while it stays within the orbit's reach
            // (review M3c I1), and it may be handed over from anywhere on it.
            float reach = holding ? 2f * HoldOrbit.RadiusFor(FormationPilot.OrbitSpeed(p)) + ApproachReached : ApproachReached;
            bool reached = to.Length < reach;
            handOver = reached && time >= retryAt && (Vertical || Field.TryClaimLanding(owner, time));
            Vec3 dir = to.SqrLength > 1e-4f ? to.Normalized : s.Fwd.Horizontal.Normalized;
            Vec3 heading = Vertical ? dir : Field.Runway.Direction(Field.Reverse);
            RefState reference = new RefState(point, dir * p.CruiseSpeed, Vec3.Zero);
            if (reached && !handOver)
            {
                if (Vertical) reference = new RefState(point, Vec3.Zero, Vec3.Zero);
                else
                {
                    if (!holding) hold.Begin(point, FormationPilot.OrbitSpeed(p), s.Pos);
                    reference = hold.Step(point, Vec3.Zero, dt);
                }
            }
            holding = reached && !handOver && !Vertical;
            return new FlightIntent
            {
                Ref = reference,
                Limits = new SpeedLimits(Vertical ? 0f : p.MinimumSpeed(1f), p.CruiseSpeed, false, true),
                Precision = 1f,
                Aggression = 0.3f,
                HasHeading = !holding,
                HeadingDeg = Vec3.HeadingDeg(heading),
            };
        }

        public void LandingBegun(float time)
        {
            Phase = RecoveryPhase.Landing;
            landingSince = time;
        }

        public bool LandingOverdue(float time) => Phase == RecoveryPhase.Landing && time - landingSince > LandingSeconds;

        public void LandingFailed(float time, WingEventRing events, int slot)
        {
            Field.ReleaseLanding(owner);
            retryAt = time + RetrySeconds;
            FailedLandings++;
            Phase = FailedLandings >= MaxLandingTries ? RecoveryPhase.Released : RecoveryPhase.Approach;
            Log(events, time, slot, WingEventKind.LandingFailed);
        }

        /// <summary>Down on <paramref name="landedAt"/> (the field the game landed it at): a jet taxis to a stand (none
        /// free: back to the reserve), a helicopter stands where it is.</summary>
        public void Landed(FieldTraffic landedAt, in AircraftState s, float time, WingEventRing events, int slot)
        {
            Field.ReleaseLanding(owner);
            Ground = new GroundPilot(owner, landedAt, cls, new Pose(s.Pos, s.Fwd), -1);
            // A jet with nowhere to stand goes back to the reserve even on a refit: it is not serviced on the runway.
            noStand = !Vertical && !Ground.TaxiIn(s, time, events, slot);
            if (Vertical || noStand) Ground.StandHere(new Pose(s.Pos, s.Fwd), time);
            Phase = RecoveryPhase.Ground;
            Log(events, time, slot, WingEventKind.Landed);
        }

        /// <summary>After the ground pilot's step: on the stand, back to the reserve (RTB) or into servicing (Refit); the
        /// service is due after the refit time; departing, done once airborne.</summary>
        public RecoveryAction Update(float time, float fuel, float ammo, WingEventRing events, int slot)
        {
            switch (Phase)
            {
                case RecoveryPhase.Ground when Ground.Phase == GroundPhase.Stand:
                    if (Intent == RecoveryIntent.Rtb || noStand)
                    {
                        Phase = RecoveryPhase.Reserve;
                        Log(events, time, slot, WingEventKind.Reserved);
                        return RecoveryAction.Reserve;
                    }
                    Phase = RecoveryPhase.Servicing;
                    serviceUntil = time + RefitTimer.Seconds(fuel, ammo);
                    return RecoveryAction.None;
                case RecoveryPhase.Servicing when time >= serviceUntil:
                    return RecoveryAction.Service;
                case RecoveryPhase.Departing when Ground.Done:
                    Phase = RecoveryPhase.Done;
                    return RecoveryAction.None;
                default:
                    return RecoveryAction.None;
            }
        }

        /// <summary>Refuelled and rearmed: out again from the stand (<paramref name="abreast"/>: how many of its type fit
        /// the runway side by side).</summary>
        public void Serviced(float time, int abreast, WingEventRing events, int slot)
        {
            if (Phase != RecoveryPhase.Servicing) return;
            if (!Vertical) Ground.Field.Departures.Expect(owner, abreast);
            Ground.Depart(time);
            Phase = RecoveryPhase.Departing;
            Log(events, time, slot, WingEventKind.Serviced);
        }

        /// <summary>Leaves the wing: the runway is no longer its to land on.</summary>
        public void Leave() => Field.ReleaseLanding(owner);

        private static void Log(WingEventRing events, float time, int slot, WingEventKind kind) =>
            events?.Push(new WingEvent { Time = time, Member = slot, Kind = kind });
    }
}
