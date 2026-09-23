using System;
using System.Numerics;

namespace WingCommand
{
    internal struct RejoinOutput
    {
        public RefState Ref;
        /// <summary>0 = rejoin reference, 1 = slot.</summary>
        public float Sigma;
        public bool FallingBehind, FallingBehindStarted, FallingBehindCleared;
        public float InterceptSeconds;
    }

    /// <summary>Procedural rejoin for one member. References, in order:
    /// <list type="number">
    /// <item>A cutoff rendezvous with the pre-slot, predicted around the leader's turn, on the member's own
    /// lane (one lane step below the leader per slot number, so rejoin paths cannot cross).</item>
    /// <item>The pre-slot: 1 spacing aft, 20 m low.</item>
    /// <item>The slot.</item>
    /// </list>
    /// σ blends them continuously. σ_pre = 1 − smoothstep(1, 2, d/spacing), falling at 0.7 s and rising at
    /// 2.5 s. σ_slot follows it only once the stagger gate is clear, the member is already in its slot, or it
    /// has waited 30 s.
    /// Falling behind is declared when there is no intercept, or one beyond 120 s, for 10 s. The member then
    /// pursues the slot itself (extended trail) until the intercept is under 60 s. It never gives up the
    /// rejoin.</summary>
    internal sealed class RejoinPlanner
    {
        public const float LaneStep = 30f, LaneClearance = 5f, LaneCapture = 10f, PreSlotLow = 20f;
        public const float SigmaNear = 1f, SigmaFar = 2f, SigmaTauDown = 0.7f, SigmaTauUp = 2.5f;
        public const float InSlotFraction = 0.3f, AdvancedSigma = 0.95f, StaggerDeadlockSeconds = 30f;
        public const float BehindEnterSeconds = 120f, BehindExitSeconds = 60f, BehindPersistSeconds = 10f;
        public const float InterceptHorizon = 1000f;

        private float sigmaPre, sigmaSlot, waited;
        private bool primed, behind;
        private Persistence behindTimer;

        /// <summary>Vertical distance between rejoin lanes. The pilot keeps it clear of the collision-bias radius
        /// (at least <see cref="LaneStep"/>), so members on adjacent lanes cross without tripping the bias.</summary>
        public float LaneStepM = LaneStep;

        public bool FallingBehind => behind;

        public RejoinOutput Step(in AircraftState s, in SlotTarget slot, in LeaderEstimate leader, int lane,
            float spacing, float availableSpeed, bool staggerClear, float dt)
        {
            RefState target = slot.Ref;
            var pre = new RefState(target.Pos - leader.Track * spacing - Vec3.Up * PreSlotLow, target.Vel, target.Acc);

            float intercept = FormationIntercept.InterceptSeconds(Flat(target.Pos - s.Pos), Flat(target.Vel),
                availableSpeed, InterceptHorizon);
            bool wasBehind = behind;
            if (!behind) behind = behindTimer.Update(intercept > BehindEnterSeconds, BehindPersistSeconds, dt);
            else if (intercept < BehindExitSeconds)
            {
                behind = false;
                behindTimer = default;
            }

            RefState rendezvous = behind ? target : Rendezvous(s, pre, leader, LaneStepM * lane, availableSpeed);

            float dSlot = (target.Pos - s.Pos).Length;
            float d = Math.Min((pre.Pos - s.Pos).Length, dSlot);
            float rawPre = behind ? 0f : 1f - Scalar.SmoothStep(SigmaNear, SigmaFar, d / Math.Max(1f, spacing));
            bool inSlot = dSlot < InSlotFraction * spacing;
            if (staggerClear) waited = 0f;
            else if (!inSlot && sigmaPre >= AdvancedSigma) waited += dt;
            bool advancing = staggerClear || inSlot || sigmaSlot > AdvancedSigma || waited >= StaggerDeadlockSeconds;
            float rawSlot = advancing ? rawPre : 0f;

            if (!primed)
            {
                sigmaPre = rawPre;
                sigmaSlot = rawSlot;
                primed = true;
            }
            sigmaPre = Filter(sigmaPre, rawPre, dt);
            sigmaSlot = Filter(sigmaSlot, rawSlot, dt);

            return new RejoinOutput
            {
                Ref = Blend(Blend(rendezvous, pre, sigmaPre), target, sigmaSlot),
                Sigma = sigmaPre * sigmaSlot,
                FallingBehind = behind,
                FallingBehindStarted = behind && !wasBehind,
                FallingBehindCleared = wasBehind && !behind,
                InterceptSeconds = intercept,
            };
        }

        private static RefState Rendezvous(in AircraftState s, in RefState pre, in LeaderEstimate leader, float laneDepth,
            float availableSpeed)
        {
            FormationIntercept.Plan plan = FormationIntercept.Solve(Flat(pre.Pos - s.Pos), Flat(leader.Vel),
                Flat(pre.Pos - leader.Pos), Flat(pre.Vel), s.Speed, availableSpeed, leader.TurnRate);
            var cutoff = new RefState(
                new Vec3(s.Pos.X + plan.Gap.X, leader.Pos.Y - laneDepth, s.Pos.Z + plan.Gap.Y),
                new Vec3(plan.ArrivalVelocity.X, 0f, plan.ArrivalVelocity.Y), Vec3.Zero);
            // The lead point is used only across the track (the cutoff inside a turn): along the track the
            // reference stays at the pre-slot, so the stopping-distance law brakes for the real gap. It only
            // helps a member that has to catch the pre-slot up; one abeam or ahead of it waits for it instead,
            // so the lead fades by how far the pre-slot is ahead (cosine, clipped at 0). The lane altitude stays
            // either way: it is what keeps rejoins from different sides apart.
            Vec3 across = Vec3.Cross(Vec3.Up, leader.Track);
            Vec3 gap = (pre.Pos - s.Pos).Horizontal;
            float length = gap.Length;
            float ahead = length > 1f ? Scalar.Clamp01(Vec3.Dot(gap, leader.Track) / length) : 1f;
            RefState faded = Blend(pre, cutoff, ahead);
            Vec3 pos = pre.Pos + across * Vec3.Dot(faded.Pos - pre.Pos, across);
            pos = new Vec3(pos.X, cutoff.Pos.Y, pos.Z);

            // Lanes first: a member on the far side of the leader's track from its slot keeps to its own side
            // until it is on its lane, so the crossing happens with lane separation (blended over 10-20 m).
            float memberSide = Vec3.Dot(s.Pos - leader.Pos, across), refSide = Vec3.Dot(pos - leader.Pos, across);
            if (memberSide * Vec3.Dot(pre.Pos - leader.Pos, across) < 0f)
            {
                float hold = Scalar.SmoothStep(LaneCapture, 2f * LaneCapture, Math.Abs(s.Pos.Y - cutoff.Pos.Y));
                pos += across * ((memberSide - refSide) * hold);
            }
            return new RefState(pos, new Vec3(faded.Vel.X, leader.Vel.Y, faded.Vel.Z), new Vec3(faded.Acc.X, 0f, faded.Acc.Z));
        }

        private static float Filter(float current, float target, float dt)
        {
            if (dt <= 0f) return current;
            float tau = target < current ? SigmaTauDown : SigmaTauUp;
            return current + (target - current) * (1f - (float)Math.Exp(-dt / tau));
        }

        private static RefState Blend(in RefState a, in RefState b, float t) =>
            new RefState(Vec3.Lerp(a.Pos, b.Pos, t), Vec3.Lerp(a.Vel, b.Vel, t), Vec3.Lerp(a.Acc, b.Acc, t));

        private static Vector2 Flat(Vec3 v) => new Vector2(v.X, v.Z);
    }
}
