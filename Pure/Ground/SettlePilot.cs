using System;

namespace WingCommand
{
    internal enum SettlePhase : byte { Approach, Descend, Down, LiftOff, Done }

    /// <summary>Spec M4 §5.2: a helicopter lands at a ground point and waits there.
    /// <list type="bullet">
    /// <item>Approach — while farther than <see cref="ApproachNear"/> it keeps its height (at least the approach height)
    /// under the caller's limits (terrain floor, the wing's collision bias: review M4c I4); near the point it comes down
    /// to <see cref="ApproachHeight"/> over it with the floor off.</item>
    /// <item>Descend — straight down, the sink rate easing from <see cref="DescentRate"/> to <see cref="TouchdownRate"/>
    /// (<see cref="FlareGain"/> per metre), flown as the reference's own vertical speed at the aircraft's height (the
    /// FlightSim touched down at 3 m/s when a sinking reference ran ahead).</item>
    /// <item>Down — only on real contact (review M4c I1): below <see cref="ContactHeight"/>, sinking slower than
    /// <see cref="TouchdownSpeed"/>, sliding slower than <see cref="TouchdownSlide"/>, the disc within
    /// <see cref="MaxSlopeDeg"/> of level, for <see cref="ContactDwell"/>; then the collective ramps to 0 over
    /// <see cref="CollectiveRampSeconds"/> and the brakes hold; lifted over <see cref="BounceHeight"/> it descends
    /// again (a fresh descent: review M4c I2).</item>
    /// <item>LiftOff — on <see cref="TakeOff"/>, up to <see cref="LiftOffHeight"/>, then Done.</item>
    /// </list>
    /// An approach longer than <see cref="ApproachSeconds"/> or a descent longer than <see cref="SettleSeconds"/> gives
    /// up.</summary>
    internal sealed class SettlePilot
    {
        public static float ApproachHeight = 15f, ApproachReached = 3f, ApproachNear = 50f, DescentRate = 1.5f,
            TouchdownRate = 0.4f, FlareGain = 0.25f, ContactHeight = 0.2f, TouchdownSpeed = 1f, TouchdownSlide = 1f,
            MaxSlopeDeg = 15f, ContactDwell = 0.5f, CollectiveRampSeconds = 1f, BounceHeight = 2f, LiftOffHeight = 20f,
            SettleSeconds = 60f, ApproachSeconds = 180f;

        public readonly Vec3 Point;
        private readonly float headingDeg;
        private float since, contact, downFor, rampFrom;
        private ControlOutput last;
        private bool trackOnLift;

        public SettlePilot(Vec3 groundPoint, float headingDeg, float time)
        {
            Point = groundPoint;
            this.headingDeg = headingDeg;
            since = time;
        }

        public SettlePhase Phase { get; private set; }
        /// <summary>How long the approach may take before giving up (<see cref="ApproachSeconds"/>, or longer for a far
        /// point: review M4c-2 I1).</summary>
        public float ApproachLimit = ApproachSeconds;
        /// <summary>The reference last flown (diagnostics, tests).</summary>
        public Vec3 Target { get; private set; }

        /// <summary>Ground under a candidate point: dry (not water, something hit) and within the slope limit.</summary>
        public static bool Landable(bool dryLand, float normalY) => dryLand && normalY >= (float)Math.Cos(MaxSlopeDeg * Math.PI / 180.0);

        /// <summary>Lift off now (from down, or straight from the approach or descent).</summary>
        public void TakeOff()
        {
            if (Phase == SettlePhase.Done || Phase == SettlePhase.LiftOff) return;
            trackOnLift = Phase == SettlePhase.Down;
            Phase = SettlePhase.LiftOff;
        }

        /// <param name="approach">The limits the approach flies under while far from the point (terrain floor, collision
        /// bias).</param>
        public ControlOutput Step(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot, in LimitContext approach)
        {
            var groundOff = new LimitContext { FloorY = float.NaN, Aggression = 0.3f };
            switch (Phase)
            {
                case SettlePhase.Approach:
                {
                    if (time - since > ApproachLimit) return GiveUp(time, events, slot);
                    Vec3 over = Point + Vec3.Up * ApproachHeight;
                    bool near = (over - s.Pos).Horizontal.Length < ApproachNear;
                    if (!near) over = new Vec3(over.X, Math.Max(over.Y, s.Pos.Y), over.Z);
                    else if ((over - s.Pos).Horizontal.Length < ApproachReached && Math.Abs(s.Pos.Y - over.Y) < ApproachReached)
                    {
                        Phase = SettlePhase.Descend;
                        since = time;
                        contact = 0f;
                    }
                    return last = Fly(over, Vec3.Zero, s, p, pipeline, dt, near ? groundOff : approach);
                }
                case SettlePhase.Descend:
                {
                    if (time - since > SettleSeconds) return GiveUp(time, events, slot);
                    bool touching = s.RadarAlt < ContactHeight && Math.Abs(s.Vel.Y) < TouchdownSpeed &&
                                    s.Vel.Horizontal.Length < TouchdownSlide && s.Up.Y >= (float)Math.Cos(MaxSlopeDeg * Math.PI / 180.0);
                    contact = touching ? contact + dt : 0f;
                    if (contact >= ContactDwell - 1e-4f)
                    {
                        Phase = SettlePhase.Down;
                        downFor = 0f;
                        rampFrom = last.Throttle;
                        Log(events, time, slot, WingEventKind.Landed);
                        return last = Held();
                    }
                    float rate = Scalar.Clamp(s.RadarAlt * FlareGain, TouchdownRate, DescentRate);
                    return last = Fly(new Vec3(Point.X, s.Pos.Y, Point.Z), new Vec3(0f, -rate, 0f), s, p, pipeline, dt, groundOff);
                }
                case SettlePhase.Down:
                    if (s.RadarAlt > BounceHeight)
                    {
                        Phase = SettlePhase.Descend;
                        since = time;
                        contact = 0f;
                        pipeline.Track(s, last, p);
                        return last;
                    }
                    downFor += dt;
                    return last = Held();
                case SettlePhase.LiftOff:
                    if (trackOnLift)
                    {
                        pipeline.Track(s, last, p);
                        trackOnLift = false;
                    }
                    if (s.RadarAlt >= LiftOffHeight - 3f)
                    {
                        Phase = SettlePhase.Done;
                        Log(events, time, slot, WingEventKind.Airborne);
                    }
                    return last = Fly(Point + Vec3.Up * LiftOffHeight, Vec3.Zero, s, p, pipeline, dt, groundOff);
                default:
                    return last;
            }
        }

        /// <summary>The collective ramping to 0 (no cut: review M4c I1) and the brakes on.</summary>
        private ControlOutput Held() => new ControlOutput
        {
            Throttle = rampFrom * Math.Max(0f, 1f - downFor / CollectiveRampSeconds), Brake = 1f,
        };

        private ControlOutput Fly(Vec3 target, Vec3 vel, in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float dt,
            in LimitContext limits)
        {
            Target = target;
            var intent = new FlightIntent
            {
                Ref = new RefState(target, vel, Vec3.Zero),
                Limits = new SpeedLimits(0f, p.CruiseSpeed, false, true),
                Precision = 1f,
                Aggression = 0.3f,
                HasHeading = true,
                HeadingDeg = headingDeg,
            };
            GuidanceCommand g = pipeline.Guide(intent, s, p);
            return pipeline.Step(g, s, limits, p, dt);
        }

        private ControlOutput GiveUp(float time, WingEventRing events, int slot)
        {
            Phase = SettlePhase.Done;
            Log(events, time, slot, WingEventKind.LandingFailed);
            return last;
        }

        private static void Log(WingEventRing events, float time, int slot, WingEventKind kind) =>
            events?.Push(new WingEvent { Time = time, Member = slot, Kind = kind });
    }
}
