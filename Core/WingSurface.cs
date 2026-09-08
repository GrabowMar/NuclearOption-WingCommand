using UnityEngine;

namespace WingCommand
{
    /// <summary>Publishes surface-member destination and effort from wing slots and directives. Registered
    /// behaviours supply vehicle-specific control. Read each fixed update because leaders move, orders
    /// change, and targets die.</summary>
    public static class WingSurface
    {
        /// <summary>Surface-control task for the current tick.</summary>
        public readonly struct Task
        {
            /// <summary>Destination in world coordinates.</summary>
            public Vector3 Destination { get; }

            /// <summary>Stopping distance from Destination, in metres.</summary>
            public float ArriveRadius { get; }

            /// <summary>Hold current position even if the destination moves; this is independent of
            /// arrival.</summary>
            public bool Hold { get; }

            /// <summary>Suggested power fraction, 0-1. Station keeping uses less than repositioning to
            /// reduce oscillation; controllers may ignore it.</summary>
            public float Effort { get; }

            /// <summary>Explicit target, or null.</summary>
            public Unit Target { get; }

            public Task(Vector3 destination, float arriveRadius, bool hold, float effort, Unit target)
            {
                Destination = destination;
                ArriveRadius = arriveRadius;
                Hold = hold;
                Effort = effort;
                Target = target;
            }
        }

        /// <summary>Read a commanded surface member's task; return false for other aircraft.</summary>
        public static bool TryGetTask(Aircraft aircraft, out Task task)
        {
            task = default;

            if (aircraft == null) return false;

            WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
            if (member == null || !member.IsSurface) return false;

            task = Resolve(member);
            return true;
        }

        private static Task Resolve(WingMember member)
        {
            WingDirective directive = member.Directive;
            Unit target = directive.Target != null && !directive.Target.disabled
                ? directive.Target
                : null;

            switch (directive.Order)
            {
                // Hold the named map point after arrival.
                case WingOrder.OrbitHere:
                case WingOrder.LandHere:
                    return directive.HasPoint
                        ? new Task(directive.Point.AsVector3(), StationRadius, hold: false, effort: 0.7f, target: null)
                        : Halt(member);

                case WingOrder.MoveToPoint:
                    return directive.HasPoint
                        ? new Task(directive.Point.AsVector3(), StationRadius, hold: false, effort: 1f, target: null)
                        : Halt(member);

                // Stop at stand-off range so the hull stays outside weapon minimum ranges.
                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                case WingOrder.JamTarget:
                    return target != null
                        ? new Task(StandOff(member, target), StandOffRadius, hold: false, effort: 1f, target: target)
                        : Slot(member, target);

                // Retreat behind the leader relative to the target.
                case WingOrder.FallBack:
                case WingOrder.ReturnToBase:
                    return Slot(member, null);

                default:
                    return Slot(member, target);
            }
        }

        /// <summary>Stopping radius for surface slots and waypoints, in metres.</summary>
        private const float StationRadius = 150f;

        /// <summary>Minimum target approach radius for surface members, in metres.</summary>
        private const float StandOffRadius = 400f;

        /// <summary>Preferred stand-off distance while attacking.</summary>
        private const float StandOffDistance = 3000f;

        private static Task Halt(WingMember member) =>
            new Task(member.Aircraft.transform.position, StationRadius, hold: true, effort: 0f, target: null);

        private static Task Slot(WingMember member, Unit target)
        {
            Aircraft leader = member.Leader;
            if (leader == null || leader.disabled) return Halt(member);

            // Recompute the shared slot solver with zero stack and Trail geometry as the leader moves.
            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward,
                member.Slot,
                FormationShape.Trail,
                WingFormation.SlotSpacing * WingTuning.SurfaceSpacingScale,
                stack: 0f);

            Vector3 destination = leader.transform.position + offset;
            destination.y = member.Aircraft.transform.position.y;

            return new Task(destination, StationRadius, hold: false, effort: 0.85f, target: target);
        }

        private static Vector3 StandOff(WingMember member, Unit target)
        {
            Vector3 self = member.Aircraft.transform.position;
            Vector3 hostile = target.transform.position;

            Vector3 toSelf = self - hostile;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 1f) toSelf = Vector3.forward;

            Vector3 point = hostile + toSelf.normalized * StandOffDistance;
            point.y = self.y;
            return point;
        }
    }
}
