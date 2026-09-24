using System;

namespace WingCommand
{
    internal enum SettlePhase : byte { Approach, Descend, Down, LiftOff, Done }

    /// <summary>Spec M4 §5.2: a helicopter lands at a ground point and waits there. Approach — hover-fly to
    /// <see cref="ApproachHeight"/> over the point; Descend — straight down, the sink rate easing from
    /// <see cref="DescentRate"/> to <see cref="TouchdownRate"/> near the ground (<see cref="FlareGain"/> per metre of
    /// height), flown as the reference's own vertical speed at the aircraft's height (no height error to catch up: the
    /// FlightSim touched down at 3 m/s when the reference ran ahead); Down — below <see cref="TouchdownHeight"/> and slower than
    /// <see cref="TouchdownSpeed"/> vertically: collective 0 and brakes, until <see cref="TakeOff"/> (lifted over
    /// <see cref="BounceHeight"/> it descends again); LiftOff — up to <see cref="LiftOffHeight"/>, then Done. The member's
    /// own rotary pipeline flies it with the terrain floor off, as the lift-off from a pad does. An approach longer than
    /// <see cref="ApproachSeconds"/> (an order given high up takes a while) or a descent longer than
    /// <see cref="SettleSeconds"/> gives up.</summary>
    internal sealed class SettlePilot
    {
        public static float ApproachHeight = 15f, ApproachReached = 3f, DescentRate = 1.5f, TouchdownRate = 0.4f, FlareGain = 0.25f,
            TouchdownHeight = 0.4f, TouchdownSpeed = 1f, BounceHeight = 2f, LiftOffHeight = 20f, SettleSeconds = 60f, ApproachSeconds = 180f;

        public readonly Vec3 Point;
        private readonly float headingDeg;
        private float since;
        private ControlOutput last;
        private bool trackOnLift;

        public SettlePilot(Vec3 groundPoint, float headingDeg, float time)
        {
            Point = groundPoint;
            this.headingDeg = headingDeg;
            since = time;
        }

        public SettlePhase Phase { get; private set; }

        /// <summary>Lift off now (from down, or straight from the approach or descent).</summary>
        public void TakeOff()
        {
            if (Phase == SettlePhase.Done || Phase == SettlePhase.LiftOff) return;
            trackOnLift = Phase == SettlePhase.Down;
            Phase = SettlePhase.LiftOff;
        }

        public ControlOutput Step(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            switch (Phase)
            {
                case SettlePhase.Approach:
                    if (time - since > ApproachSeconds) return GiveUp(time, events, slot);
                    Vec3 over = Point + Vec3.Up * ApproachHeight;
                    if ((over - s.Pos).Horizontal.Length < ApproachReached && Math.Abs(s.Pos.Y - over.Y) < ApproachReached)
                    {
                        Phase = SettlePhase.Descend;
                        since = time;
                    }
                    return last = Fly(over, Vec3.Zero, s, p, pipeline, dt);
                case SettlePhase.Descend:
                    if (time - since > SettleSeconds) return GiveUp(time, events, slot);
                    if (s.RadarAlt < TouchdownHeight && Math.Abs(s.Vel.Y) < TouchdownSpeed)
                    {
                        Phase = SettlePhase.Down;
                        Log(events, time, slot, WingEventKind.Landed);
                        return last = Held();
                    }
                    float rate = Scalar.Clamp(s.RadarAlt * FlareGain, TouchdownRate, DescentRate);
                    return last = Fly(new Vec3(Point.X, s.Pos.Y, Point.Z), new Vec3(0f, -rate, 0f), s, p, pipeline, dt);
                case SettlePhase.Down:
                    if (s.RadarAlt > BounceHeight)
                    {
                        Phase = SettlePhase.Descend;
                        pipeline.Track(s, last, p);
                    }
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
                    return last = Fly(Point + Vec3.Up * LiftOffHeight, Vec3.Zero, s, p, pipeline, dt);
                default:
                    return last;
            }
        }

        private static ControlOutput Held() => new ControlOutput { Throttle = 0f, Brake = 1f };

        private ControlOutput Fly(Vec3 target, Vec3 vel, in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float dt)
        {
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
            return pipeline.Step(g, s, new LimitContext { FloorY = float.NaN, Aggression = 0.3f }, p, dt);
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
