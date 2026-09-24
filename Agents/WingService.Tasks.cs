using UnityEngine;

namespace WingCommand
{
    /// <summary>Wing tasks (spec M4 §2): the planner flies a virtual lead the formation forms on while a task runs.</summary>
    internal sealed partial class WingService
    {
        public readonly WingPlanner Planner = new WingPlanner();

        /// <summary>An anchor below this is on the surface: a task's lead starts from the wing instead.</summary>
        public static float AnchorAirborneHeight = 5f;

        /// <summary>Gives the whole wing a task (Form cancels it); the refusal carries its reason.</summary>
        public OrderResult Order(WingTask task)
        {
            OrderResult r = Planner.Apply(task, Snapshot(), missionTime, Events);
            // A task replaces an escort (review M4a I5): the wing forms on the task's lead, with the player's shapes. A
            // plain anchor (the harness's lead, standing in for the player) stays, so Form returns to it.
            if (r.Accepted && task != null && task.Kind != TaskKind.Form && Escorting)
            {
                Anchor = null;
                Escorting = false;
            }
            Plugin.Logger.LogInfo(r.Accepted
                ? $"[Wing] task {(task != null ? task.Kind : TaskKind.Form)} ordered"
                : $"[Wing] task {(task != null ? task.Kind : TaskKind.Form)} refused: {r.Reason}");
            return r;
        }

        private bool plannerWas;

        /// <summary>The planner flies its lead; when the wing switches between the lead and the player (a task starting or
        /// ending) the leader estimate starts afresh.</summary>
        private void StepPlanner(float dt)
        {
            if (Planner.Active) Planner.Step(Snapshot(), missionTime, dt, Events);
            if (Planner.Active == plannerWas) return;
            plannerWas = Planner.Active;
            Wing?.ResetLeader();
        }

        /// <summary>While a task runs, the wing's collision body 0 is the player's aircraft (flying, not a member), not the
        /// lead's empty point (review M4a C2).</summary>
        private AnchorSample PlayerBody()
        {
            Aircraft p = Player;
            if (!Flying(p) || IsMember(p)) return default;
            return new AnchorSample { Pos = p.GlobalPosition().ToVec3(), Vel = p.rb != null ? p.rb.velocity.ToVec3() : Vec3.Zero, Present = true };
        }

        /// <summary>What the planner sees: members (those recovering apart), the anchor, the airborne members' mean
        /// position and velocity (all members' while none is airborne), the slowest cruise and highest loaded minimum of
        /// the members that are not recovering, the wing's floor and the map's size. Allocation-free.</summary>
        private WingSnapshot Snapshot()
        {
            var s = new WingSnapshot { FloorY = floor != null ? floor.Value : float.NaN, CruiseSpeed = float.MaxValue, AllRotary = true };
            Vec3 sum = Vec3.Zero, vel = Vec3.Zero, allSum = Vec3.Zero, allVel = Vec3.Zero;
            int airborne = 0;
            foreach (WingMember m in Members)
            {
                if (m.Released) continue;
                s.Members++;
                if (m.Recovery != null)
                {
                    s.Recovering++;
                    continue;
                }
                allSum += m.Last.Pos;
                allVel += m.Last.Vel;
                s.CruiseSpeed = System.Math.Min(s.CruiseSpeed, m.Profile.CruiseSpeed);
                s.MinSpeed = System.Math.Max(s.MinSpeed, m.Profile.MinimumSpeed(1f));
                s.AllRotary &= m.Profile.Class == AirframeClass.Rotary;
                if (m.OnGround) continue;
                sum += m.Last.Pos;
                vel += m.Last.Vel;
                airborne++;
                s.WingAirborne = true;
            }
            int active = s.Members - s.Recovering;
            if (active == 0)
            {
                s.CruiseSpeed = 0f;
                s.AllRotary = false;
            }
            if (airborne > 0)
            {
                s.Centroid = sum * (1f / airborne);
                s.MeanVel = vel * (1f / airborne);
            }
            else if (active > 0)
            {
                s.Centroid = allSum * (1f / active);
                s.MeanVel = allVel * (1f / active);
            }
            Unit u = Anchor != null ? Anchor : (Unit)Player;
            if (Alive(u))
            {
                s.AnchorPresent = true;
                s.AnchorPos = u.GlobalPosition().ToVec3();
                s.AnchorVel = u.rb != null ? u.rb.velocity.ToVec3() : Vec3.Zero;
                Transform t = u.transform;
                s.AnchorFwd = t.forward.ToVec3();
                // Bank as SignedAngle(Cross(up, fwd), right, fwd), the project's rule.
                s.AnchorBankDeg = u is Aircraft ? Vector3.SignedAngle(Vector3.Cross(Vector3.up, t.forward), t.right, t.forward) : 0f;
                s.AnchorAirborne = u is Aircraft a && a.radarAlt > AnchorAirborneHeight;
            }
            var map = NetworkSceneSingleton<LevelInfo>.i != null ? NetworkSceneSingleton<LevelInfo>.i.LoadedMapSettings : null;
            if (map != null)
            {
                s.MapHalfX = map.MapSize.x * 0.5f;
                s.MapHalfZ = map.MapSize.y * 0.5f;
            }
            return s;
        }
    }
}
