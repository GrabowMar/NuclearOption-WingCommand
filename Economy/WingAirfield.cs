using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Runway lookup and the launch pose, plus the watchdog that gets a delivery rolling.
    ///
    /// A fixed-wing requisition is placed on the takeoff strip and left to the stock AI, but
    /// "left to it" needs one guard. <c>AIPilotTaxiState</c> steers from
    /// <c>PathfindingAgent.GetSteerpoint</c>, and that returns nothing at all when its
    /// waypoint list is empty — on which frame the taxi state zeroes the throttle and
    /// returns <i>before</i> the twelve-metre test that would have handed the aircraft to
    /// takeoff. Thirty seconds under one metre per second later its stuck timer fires and
    /// the pilot ejects on the runway. A field whose taxi network does not reach its own
    /// threshold puts every delivery into exactly that hole.
    ///
    /// So the transition is not left to chance: once the aircraft is verifiably on the
    /// runway and pointing down it, this hands it to <c>AIPilotTakeoffState</c> itself.
    /// That is the one pose the stock takeoff state is safe to enter from — it is the pose
    /// its own hold-short logic would have entered from — and entering it anywhere else is
    /// what turns a delivery into a grass cut. Nothing here moves an aircraft.
    /// </summary>
    internal static class WingAirfield
    {
        /// <summary>
        /// How long the stock taxi state gets to make its own move first.
        ///
        /// On a healthy field it needs a single tick: the aircraft is put down inside the
        /// twelve metres at which taxi queues for takeoff and switches states itself. Six
        /// seconds is therefore not a wait, it is unambiguous evidence that the pathfinder
        /// gave taxi nothing to steer with — and it is a fifth of the thirty-second stuck
        /// timer that would otherwise end in an ejection on the runway.
        /// </summary>
        private const float TaxiGrace = 6f;

        /// <summary>Speed under which a taxiing delivery counts as not having got going.</summary>
        private const float StalledSpeed = 1.5f;

        /// <summary>Radius within which an aircraft is considered to be at a field.</summary>
        private const float FieldRadius = 4000f;

        private sealed class Launch
        {
            internal Aircraft Aircraft;
            internal Airbase.Runway Runway;
            internal bool Reverse;
            internal float SpawnedAt;
            internal bool Queued;
            internal bool Reported;
            internal bool Warned;
        }

        private static readonly List<Launch> launches = new List<Launch>();

        /// <summary>The pose a requisition is spawned at, and the strip it belongs to.</summary>
        internal struct LaunchPose
        {
            public Airbase.Runway Runway;
            public bool Reverse;

            /// <summary>
            /// The threshold the aircraft is placed at. Held as a transform rather than a
            /// point so that the departure lane measures its clearance against the live
            /// strip: a floating-origin shift or a moving carrier deck moves both, and a
            /// stationary aircraft must not read as one that has cleared the spot.
            /// </summary>
            public Transform Threshold;

            public GlobalPosition Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
        }

        // ------------------------------------------------------------------ runway choice

        /// <summary>
        /// The strip at this field best placed to launch the airframe, or false when it has
        /// none.
        ///
        /// <c>Airbase.GetTakeoffRunway</c> would be the obvious call and is deliberately not
        /// used: it takes an <c>Aircraft</c>, and there is no aircraft yet — that is the
        /// whole point of asking. It also calls <c>SetUsageDirection</c>, which claims the
        /// strip as a side effect of a question. This walks the same array against the same
        /// criteria from a position instead.
        /// </summary>
        internal static bool TryFindTakeoffRunway(Airbase airbase, AircraftDefinition definition,
                                                  Vector3 from, out Airbase.Runway runway,
                                                  out bool reverse)
        {
            runway = null;
            reverse = false;
            if (airbase == null || airbase.disabled || airbase.runways == null) return false;

            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            float run = LaunchGeometry.TakeoffRun(parameters != null ? parameters.takeoffSpeed : 0f);

            float best = float.MaxValue;
            for (int i = 0; i < airbase.runways.Length; i++)
            {
                Airbase.Runway candidate = airbase.runways[i];
                if (candidate == null || candidate.Start == null || candidate.End == null) continue;
                if (!LaunchGeometry.IsUsable(candidate.Takeoff, candidate.Length, run,
                                             candidate.IsLevel()))
                    continue;

                float toStart = (candidate.Start.position - from).sqrMagnitude;
                float toEnd = (candidate.End.position - from).sqrMagnitude;
                bool useReverse = LaunchGeometry.PreferReverse(toStart, toEnd, candidate.Reversable);
                float distance = useReverse ? toEnd : toStart;
                if (distance >= best) continue;

                best = distance;
                runway = candidate;
                reverse = useReverse;
            }

            return runway != null;
        }

        /// <summary>Whether this field could launch the airframe on a strip at all.</summary>
        internal static bool HasTakeoffRunway(Airbase airbase, AircraftDefinition definition) =>
            airbase != null &&
            TryFindTakeoffRunway(airbase, definition, airbase.transform.position, out _, out _);

        /// <summary>
        /// Whether this aircraft has somewhere it can actually land.
        ///
        /// The same query <c>AIPilotLandingState</c> builds for itself, asked before the
        /// state is entered rather than after — because when that search fails the state
        /// does not report it, it ejects the pilot and sets the pilot state to null.
        /// </summary>
        internal static bool HasLandingRunway(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.NetworkHQ == null) return false;

            AircraftParameters parameters = aircraft.GetAircraftParameters();
            if (parameters == null) return false;

            float mass = aircraft.GetMass();
            float maxWeight = aircraft.definition.aircraftInfo.maxWeight;
            float landingSpeed = maxWeight > 0f
                ? Mathf.Sqrt(mass / maxWeight) * parameters.landingSpeed
                : parameters.landingSpeed;

            var query = new RunwayQuery
            {
                RunwayType = RunwayQueryType.Landing,
                MinSize = parameters.verticalLanding
                    ? aircraft.definition.length
                    : parameters.takeoffDistance,
                LandingSpeed = parameters.verticalLanding ? 0f : landingSpeed,
                TailHook = aircraft.weaponManager != null && aircraft.weaponManager.HasTailHook(),
            };

            Airbase airbase = aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, query);
            return airbase != null && airbase.RequestLanding(aircraft, query).HasValue;
        }

        // -------------------------------------------------------------------- launch pose

        /// <summary>
        /// Where to put a requisitioned aircraft so that it starts its sortie on the runway
        /// rather than inside a shelter facing the wrong way.
        ///
        /// The arithmetic is the game's own, from <c>Hangar.SpawnAircraft</c>: the airframe's
        /// <c>spawnOffset</c> lifted along the pad's up and pushed along its forward, and the
        /// definition's <c>restRotation</c> applied on top so the model sits on its gear.
        /// The threshold stands in for the pad, with one difference that matters — a saved
        /// runway's <c>Start</c> and <c>End</c> are bare transforms carrying position only,
        /// so their rotation is meaningless and the heading has to come from
        /// <c>GetDirection</c>.
        /// </summary>
        internal static bool TryBuildLaunchPose(Airbase airbase, AircraftDefinition definition,
                                                Vector3 from, out LaunchPose pose)
        {
            pose = default(LaunchPose);
            if (definition == null) return false;
            if (!TryFindTakeoffRunway(airbase, definition, from, out Airbase.Runway runway,
                                      out bool reverse))
                return false;

            Transform threshold = reverse ? runway.End : runway.Start;
            if (threshold == null) return false;

            Vector3 direction = runway.GetDirection(reverse);
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            float along = LaunchGeometry.ThresholdOffset(definition.length, definition.width);

            // Height comes from the threshold rather than from a raycast: the transform is
            // on the paved surface by construction, and the strip is level or it would not
            // have passed IsUsable. The vertical term is not decoration — Aircraft.
            // SpawnedInPosition derives radarAlt as (ground distance - spawnOffset.y), and
            // SetStartingAiState reads radarAlt > spawnOffset.y + 1 to decide whether this
            // aircraft is flying. Lift it by exactly spawnOffset.y and radarAlt comes out at
            // zero, which is what puts the pilot into taxi instead of into combat.
            //
            // spawnOffset.z is deliberately NOT applied. It is how far forward of a hangar's
            // pad marker that airframe sits, and the along-track offset above is already the
            // complete answer to the same question for a runway. Adding both would push a
            // large airframe past the twelve metres inside which the stock taxi state hands
            // off to takeoff, which is the entire point of capping the offset.
            Vector3 position = threshold.position
                             + Vector3.up * definition.spawnOffset.y
                             + direction * along;

            pose.Runway = runway;
            pose.Reverse = reverse;
            pose.Threshold = threshold;
            pose.Position = position.ToGlobalPosition();
            pose.Rotation = Quaternion.LookRotation(direction, Vector3.up) *
                            Quaternion.Euler(definition.restRotation);
            // A carrier deck is moving. Matching it is the difference between a delivery
            // that is stationary relative to the ship and one that is thrown off the stern.
            pose.Velocity = runway.GetVelocity();
            return true;
        }

        // ------------------------------------------------------------------- launch watch

        /// <summary>
        /// Start watching a delivery that was placed on a runway, so the departure can be
        /// confirmed on a later physics step rather than assumed on the spawn line.
        /// </summary>
        internal static void WatchLaunch(Aircraft aircraft, in LaunchPose pose)
        {
            if (aircraft == null || pose.Runway == null) return;
            Forget(aircraft);
            launches.Add(new Launch
            {
                Aircraft = aircraft,
                Runway = pose.Runway,
                Reverse = pose.Reverse,
                SpawnedAt = Time.timeSinceLevelLoad,
            });
        }

        internal static void Forget(Aircraft aircraft)
        {
            for (int i = launches.Count - 1; i >= 0; i--)
                if (launches[i].Aircraft == aircraft) Release(launches[i], i);
        }

        internal static void Tick()
        {
            for (int i = launches.Count - 1; i >= 0; i--)
            {
                Launch launch = launches[i];
                Aircraft aircraft = launch.Aircraft;

                if (aircraft == null || aircraft.disabled || launch.Runway == null)
                {
                    Release(launch, i);
                    continue;
                }

                Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
                if (pilot == null || pilot.dead || pilot.ejected)
                {
                    Release(launch, i);
                    continue;
                }

                // Airborne, or already rolling under the stock takeoff state: the delivery
                // has left and the wing's own handoff owns it from here.
                if (pilot.flightInfo != null && pilot.flightInfo.HasTakenOff)
                {
                    Release(launch, i);
                    continue;
                }
                if (pilot.currentState is AIPilotTakeoffState)
                {
                    Report(launch, "entered the stock takeoff run");
                    Release(launch, i);
                    continue;
                }

                // Anything other than taxi means someone else — a player takeover, a
                // recovery, an ejection — now owns the aircraft.
                if (!(pilot.currentState is AIPilotTaxiState))
                {
                    Release(launch, i);
                    continue;
                }

                if (Time.timeSinceLevelLoad - launch.SpawnedAt < TaxiGrace) continue;
                if (aircraft.speed > StalledSpeed) continue;

                float dot = Vector3.Dot(aircraft.transform.forward,
                                        launch.Runway.GetDirection(launch.Reverse).normalized);
                if (!LaunchGeometry.OnRunway(launch.Runway.AircraftOnRunway(aircraft), dot))
                {
                    // Not where the pose said it would be. Leave it to the stock AI rather
                    // than firewalling the throttle at whatever it is actually pointing at.
                    if (!launch.Warned)
                    {
                        launch.Warned = true;
                        Plugin.Logger.LogWarning(
                            "[Airfield] " + aircraft.unitName + " is not on " +
                            launch.Runway.GetName(launch.Reverse) + " after launch (dot=" +
                            dot.ToString("F2") + "); leaving it to the stock taxi");
                    }
                    continue;
                }

                // On the strip, aligned, and the stock taxi has not got it moving. Take the
                // transition it was going to make anyway. Queue first so that any other
                // aircraft asking for this runway holds short instead of rolling into us.
                if (!launch.Queued)
                {
                    launch.Runway.QueueTakeoff(aircraft);
                    launch.Queued = true;
                }

                // IsAvailableForTakeoff, not ClearForTakeoff. The latter ignores its own
                // checkCrossing argument and recurses into every crossing runway
                // unconditionally, and crossings are recorded on both strips — so two
                // runways that cross each other call each other until the stack runs out.
                if (!launch.Runway.IsAvailableForTakeoff(aircraft)) continue;

                pilot.AITakeoffState = new AIPilotTakeoffState();
                pilot.SwitchState(pilot.AITakeoffState);
                Report(launch, "handed to takeoff on " + launch.Runway.GetName(launch.Reverse));

                // The takeoff state owns the slot now, and gives it back itself — either
                // immediately in RegisterStartTakeoff on a strip that allows simultaneous
                // departures, or at RegisterTakeoffLeftRunway once it is airborne on one
                // that does not. Popping our own entry here would hand the runway to the
                // next aircraft while this one is still accelerating down it.
                launch.Queued = false;
                Release(launch, i);
            }
        }

        internal static void Reset()
        {
            for (int i = launches.Count - 1; i >= 0; i--) Release(launches[i], i);
            launches.Clear();
        }

        /// <summary>
        /// Drop a watch, giving back the runway slot it took.
        ///
        /// Neither stock taxi nor stock takeoff dequeues on the way out, so a delivery that
        /// is abandoned between the two would leave its runway — and every runway crossing
        /// it — permanently held against the rest of the mission.
        /// </summary>
        private static void Release(Launch launch, int index)
        {
            if (launch.Queued && launch.Runway != null && launch.Aircraft != null)
            {
                launch.Runway.DequeueTakeoff(launch.Aircraft);
                launch.Queued = false;
            }
            if (index >= 0 && index < launches.Count && launches[index] == launch)
                launches.RemoveAt(index);
            else
                launches.Remove(launch);
        }

        private static void Report(Launch launch, string what)
        {
            if (launch.Reported || launch.Aircraft == null) return;
            launch.Reported = true;
            Plugin.LogVerbose("[Airfield] " + launch.Aircraft.unitName + " " + what);
        }

        /// <summary>
        /// Take an aircraft out of every takeoff queue at its field.
        ///
        /// <c>Runway.DequeueTakeoff</c> only pops the head, and only when the head is the
        /// aircraft named or an entry that has already been destroyed — so this is safe to
        /// call on an aircraft that was never queued, and on one queued behind somebody
        /// else it correctly does nothing rather than jumping the line.
        /// </summary>
        internal static void DrainTakeoffQueue(Aircraft aircraft)
        {
            if (aircraft == null) return;
            Forget(aircraft);

            Airbase field = FieldUnder(aircraft);
            if (field == null || field.runways == null) return;
            for (int i = 0; i < field.runways.Length; i++)
            {
                Airbase.Runway runway = field.runways[i];
                if (runway != null && runway.Takeoff) runway.DequeueTakeoff(aircraft);
            }
        }

        /// <summary>
        /// Take an aircraft off every landing list at its field.
        ///
        /// <c>AIPilotLandingState</c> registers the arrival but only deregisters it on two
        /// of its exits, and <c>LeaveState</c> does not deregister at all — so a wingman
        /// taken out of the landing state by anything else stays on the list. A runway with
        /// a non-empty landing list refuses every takeoff, and <c>MonitorLandings</c> only
        /// drops entries whose aircraft has been destroyed. That self-heals for an RTB,
        /// which is despawned moments later, and does not for a refit, which stays on the
        /// field and would jam the strip it is about to launch from.
        /// </summary>
        internal static void DrainLandingList(Aircraft aircraft)
        {
            if (aircraft == null) return;

            Airbase field = FieldUnder(aircraft);
            if (field == null || field.runways == null) return;
            for (int i = 0; i < field.runways.Length; i++)
            {
                Airbase.Runway runway = field.runways[i];
                if (runway != null && runway.Landing) runway.DeregisterLanding(aircraft);
            }
        }

        // ------------------------------------------------------------------------ lookup

        /// <summary>
        /// The friendly field an aircraft is standing at, for a refit that has to relaunch
        /// from wherever the stock landing left it.
        /// </summary>
        internal static Airbase FieldUnder(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null) return null;

            Airbase best = null;
            float bestSq = FieldRadius * FieldRadius;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                float sq = (airbase.transform.position - aircraft.transform.position).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = airbase;
            }
            return best;
        }
    }
}
