using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Where a wingman with no autopilot should be going, and how hard.
    ///
    /// The division of labour behind <see cref="WingBehaviours.Surface"/>. Wing Command
    /// answers *where* — it owns the roster, the slot geometry and the standing directive,
    /// and none of that is worth reimplementing outside. The registered behaviour answers
    /// *how*, because that is a control loop whose gains depend entirely on what the vehicle
    /// is: a light truck and a fleet carrier answer the helm three orders of magnitude apart,
    /// and the plugin that knows which mod supplied the hull is the one that can tune it.
    ///
    /// Read this every fixed update rather than caching it. A slot moves with the leader, an
    /// order changes under the player's hand, and a target dies.
    /// </summary>
    public static class WingSurface
    {
        /// <summary>What a surface member should be doing this tick.</summary>
        public readonly struct Task
        {
            /// <summary>The world point to make for.</summary>
            public Vector3 Destination { get; }

            /// <summary>Metres from <see cref="Destination"/> at which to stop driving.</summary>
            public float ArriveRadius { get; }

            /// <summary>
            /// Hold position rather than make for anything.
            ///
            /// Distinct from having arrived: a held unit stays put even when the destination
            /// moves away from it, which is what "Hold Station" means and what a wingman
            /// with nowhere useful to go should do.
            /// </summary>
            public bool Hold { get; }

            /// <summary>
            /// Fraction of full power this task wants, 0-1.
            ///
            /// Station keeping asks for less than a repositioning run so a column does not
            /// oscillate around its slot, and the caller is free to ignore it.
            /// </summary>
            public float Effort { get; }

            /// <summary>The unit this member has been told to prosecute, or null.</summary>
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

        /// <summary>
        /// The current task for a surface wingman, or false when this aircraft is not one
        /// under command.
        /// </summary>
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
                // A named point on the map. The wing holds it once it arrives rather than
                // drifting off it, which is the difference between "hold" and "go".
                case WingOrder.OrbitHere:
                case WingOrder.LandHere:
                    return directive.HasPoint
                        ? new Task(directive.Point.AsVector3(), StationRadius, hold: false, effort: 0.7f, target: null)
                        : Halt(member);

                case WingOrder.MoveToPoint:
                    return directive.HasPoint
                        ? new Task(directive.Point.AsVector3(), StationRadius, hold: false, effort: 1f, target: null)
                        : Halt(member);

                // Close to a stand-off distance and let the engagement code do the shooting.
                // Driving all the way onto a target is how a hull ends up inside the minimum
                // range of everything it carries.
                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                case WingOrder.JamTarget:
                    return target != null
                        ? new Task(StandOff(member, target), StandOffRadius, hold: false, effort: 1f, target: target)
                        : Slot(member, target);

                // Break contact: put the leader between us and whatever we were shooting at.
                case WingOrder.FallBack:
                case WingOrder.ReturnToBase:
                    return Slot(member, null);

                default:
                    return Slot(member, target);
            }
        }

        /// <summary>Metres from a slot or waypoint at which a hull stops driving.</summary>
        private const float StationRadius = 150f;

        /// <summary>Metres from an assigned target a hull closes to and no further.</summary>
        private const float StandOffRadius = 400f;

        /// <summary>How far off a target to sit while prosecuting it.</summary>
        private const float StandOffDistance = 3000f;

        private static Task Halt(WingMember member) =>
            new Task(member.Aircraft.transform.position, StationRadius, hold: true, effort: 0f, target: null);

        private static Task Slot(WingMember member, Unit target)
        {
            Aircraft leader = member.Leader;
            if (leader == null || leader.disabled) return Halt(member);

            // The same solver the aircraft use, flattened: stack zero and a column astern.
            // Deliberately re-derived here rather than cached on the member, because the slot
            // is a function of where the leader is right now.
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
