using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Builds runway launch poses and watches stalled native taxi. Missing pathfinder waypoints
    /// can prevent the native taxi-to-takeoff transition and eventually eject the pilot. After verifying
    /// runway position and alignment, hand off to native takeoff without moving the aircraft.</summary>
    internal static class WingAirfield
    {
        /// <summary>Seconds allowed for native taxi before intervening, well before its stuck timer ejects
        /// the pilot.</summary>
        private const float TaxiGrace = 6f;

        /// <summary>Maximum speed considered a stalled taxi departure.</summary>
        private const float StalledSpeed = 1.5f;

        /// <summary>Distance defining field proximity.</summary>
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

        /// <summary>Spawn pose and its departure runway.</summary>
        internal struct LaunchPose
        {
            public Airbase.Runway Runway;
            public bool Reverse;

            /// <summary>Live threshold transform for lane clearance, accounting for floating-origin shifts
            /// and carrier motion.</summary>
            public Transform Threshold;

            public GlobalPosition Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
        }

        // Runway selection.

        /// <summary>Find a suitable launch strip before an aircraft exists, without GetTakeoffRunway's
        /// direction-claim side effect. Match native direction rules: retain recent usage heading,
        /// otherwise choose the end nearest the field.</summary>
        internal static bool TryFindTakeoffRunway(Airbase airbase, AircraftDefinition definition,
                                                  out Airbase.Runway runway,
                                                  out bool reverse)
        {
            runway = null;
            reverse = false;
            if (airbase == null || airbase.disabled || airbase.runways == null) return false;

            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            float run = LaunchGeometry.TakeoffRun(parameters != null ? parameters.takeoffSpeed : 0f);

            // Carrier catapults bypass land-runway length and slope checks.
            bool catapult = airbase.AttachedAirbase;
            Transform from = airbase.transform;

            // Choose the nearest land strip or the longest carrier strip.
            float best = catapult ? float.MinValue : float.MaxValue;
            for (int i = 0; i < airbase.runways.Length; i++)
            {
                Airbase.Runway candidate = airbase.runways[i];
                if (candidate == null || candidate.Start == null || candidate.End == null) continue;
                if (!LaunchGeometry.IsUsable(candidate.Takeoff, candidate.Length, run,
                                             candidate.IsLevel(), catapult))
                    continue;

                float toStart = (candidate.Start.position - from.position).sqrMagnitude;
                float toEnd = (candidate.End.position - from.position).sqrMagnitude;
                bool locked = LaunchGeometry.OperatingDirectionLocked(
                    Time.timeSinceLevelLoad - candidate.LastUsed);
                Airbase.Runway.RunwayDistanceResult measured = candidate.GetDistance(from);
                bool useReverse = LaunchGeometry.PreferReverse(
                    toStart, toEnd, candidate.Reversable, locked, measured.Reverse);

                if (catapult)
                {
                    if (candidate.Length <= best) continue;
                    best = candidate.Length;
                }
                else
                {
                    if (measured.Distance >= best) continue;
                    best = measured.Distance;
                }
                runway = candidate;
                reverse = useReverse;
            }

            if (catapult && runway == null) LogCarrierRunwayMiss(airbase, definition, run);

            return runway != null;
        }

        /// <summary>Log each carrier's runway rejection once per session under VerboseLogging; legitimate
        /// launch limits are not release-log warnings.</summary>
        private static readonly HashSet<int> carrierMissLogged = new HashSet<int>();

        private static void LogCarrierRunwayMiss(Airbase airbase, AircraftDefinition definition,
                                                 float run)
        {
            if (airbase == null || Plugin.Settings == null ||
                !Plugin.Settings.VerboseLogging.Value ||
                !carrierMissLogged.Add(airbase.GetInstanceID()))
                return;

            var sb = new System.Text.StringBuilder();
            sb.Append("[Carrier] ").Append(airbase.name)
              .Append(" has no usable takeoff strip for ")
              .Append(definition != null ? definition.unitName : "?")
              .Append(" (need >= ").Append(LaunchGeometry.MinimumRunwayLength.ToString("0"))
              .Append("m, roll ").Append(run.ToString("0")).Append("m). runways=")
              .Append(airbase.runways != null ? airbase.runways.Length : 0);

            if (airbase.runways != null)
            {
                for (int i = 0; i < airbase.runways.Length; i++)
                {
                    Airbase.Runway r = airbase.runways[i];
                    if (r == null) { sb.Append(" [").Append(i).Append(":null]"); continue; }
                    sb.Append(" [").Append(i)
                      .Append(" takeoff=").Append(r.Takeoff)
                      .Append(" landing=").Append(r.Landing)
                      .Append(" len=").Append(r.Length.ToString("0"))
                      .Append(" level=").Append(r.Start != null && r.End != null && r.IsLevel())
                      .Append(" ends=").Append(r.Start != null && r.End != null)
                      .Append("]");
                }
            }

            Airbase.VerticalLandingPoint[] vls = airbase.verticalLandingPoints;
            sb.Append(" verticalLandingPoints=").Append(vls != null ? vls.Length : 0);

            Plugin.LogVerbose(sb.ToString());
        }

        /// <summary>Whether this field has a compatible takeoff strip.</summary>
        internal static bool HasTakeoffRunway(Airbase airbase, AircraftDefinition definition) =>
            airbase != null &&
            TryFindTakeoffRunway(airbase, definition, out _, out _);

        /// <summary>Preflight the native landing-runway search; entering landing with no usable runway
        /// ejects the pilot instead of reporting failure.</summary>
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

        // Launch placement.

        /// <summary>Build a runway spawn using native spawnOffset and restRotation conventions. Runway
        /// endpoint rotations are not meaningful; derive heading from GetDirection.</summary>
        internal static bool TryBuildLaunchPose(Airbase airbase, AircraftDefinition definition,
                                                out LaunchPose pose)
        {
            pose = default(LaunchPose);
            if (definition == null) return false;
            if (!TryFindTakeoffRunway(airbase, definition, out Airbase.Runway runway,
                                      out bool reverse))
                return false;

            Transform threshold = reverse ? runway.End : runway.Start;
            if (threshold == null) return false;

            Vector3 direction = runway.GetDirection(reverse);
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            float along = LaunchGeometry.ThresholdOffset(definition.length, definition.width);

            // Use threshold height plus spawnOffset.y so native radar altitude classifies the aircraft
            // as grounded. Along-track placement already supplies the forward offset; adding
            // spawnOffset.z could exceed taxi's 12 m takeoff-handoff window.
            Vector3 groundPlane = threshold.position + direction * along;
            Vector3 position = groundPlane + Vector3.up * definition.spawnOffset.y;

            // Correct custom-airbase endpoint height against nearby pavement hits. Reject distant roof
            // or sea hits so the correction cannot worsen placement.
            if (Physics.Raycast(groundPlane + Vector3.up * 200f, Vector3.down,
                                out RaycastHit ground, 400f, PhysicsLayers.StaticsMask,
                                QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(ground.point.y - threshold.position.y) <= 15f)
            {
                position.y = ground.point.y + definition.spawnOffset.y;
            }

            pose.Runway = runway;
            pose.Reverse = reverse;
            pose.Threshold = threshold;
            pose.Position = position.ToGlobalPosition();
            pose.Rotation = Quaternion.LookRotation(direction, Vector3.up) *
                            Quaternion.Euler(definition.restRotation);
            // Match runway velocity to keep carrier spawns stationary relative to the deck.
            pose.Velocity = runway.GetVelocity();

            Plugin.LogVerbose(
                "[Airfield] " + definition.unitName + " pose on " +
                runway.GetName(reverse) + ": len=" + runway.Length.ToString("0") +
                " level=" + runway.IsLevel() +
                " thresholdY=" + threshold.position.y.ToString("0.0") +
                " spawnY=" + position.y.ToString("0.0"));
            return true;
        }

        /// <summary>Wait for native landing/departure claims and physical runway clearance before spawning.</summary>
        internal static bool LaunchSpotBlocked(LaunchPose pose, AircraftDefinition definition,
                                               out string blocker)
        {
            blocker = null;
            List<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled) continue;
                float clear = LaunchGeometry.SpawnClearance(
                    definition != null ? Mathf.Max(definition.length, definition.width) : 0f,
                    a.definition != null ? Mathf.Max(a.definition.length, a.definition.width) : 0f);
                bool nearSpawn = (pose.Position - a.GlobalPosition()).sqrMagnitude <= clear * clear;
                bool onStrip = (a.disabled || a.radarAlt <= 15f) && pose.Runway.AircraftOnRunway(a);
                if (!nearSpawn && !onStrip) continue;
                blocker = a.disabled ? a.unitName + " (wreck)" : a.unitName;
                return true;
            }

            // Disabled aircraft leave UnitRegistry before their colliders disappear. Detached parts
            // can also remain after the parent aircraft is destroyed.
            float radius = Mathf.Max(pose.Runway.GetWidth() * 0.5f,
                definition != null ? Mathf.Max(definition.length, definition.width) * 0.5f : 14f);
            Collider[] obstacles = Physics.OverlapCapsule(pose.Runway.Start.position,
                pose.Runway.End.position, radius + LaunchGeometry.ThresholdMargin,
                ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider obstacle in obstacles)
            {
                UnitPart part = obstacle.GetComponentInParent<UnitPart>();
                Aircraft aircraft = obstacle.GetComponentInParent<Aircraft>();
                if (aircraft == null && (part == null ||
                    (!(part.parentUnit is Aircraft) && !part.IsDetached()))) continue;
                Aircraft owner = aircraft != null ? aircraft : part?.parentUnit as Aircraft;
                bool hasOwner = aircraft != null || (part != null && part.parentUnit != null);
                if (LaunchGeometry.CanClearRunwayDebris(
                    NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active,
                    hasOwner, owner != null && owner.disabled, part != null && part.IsDetached()))
                {
                    // Match native WaitRemoveAircraft: destruction also removes its detached parts
                    // and propagates through the network identity. Never destroy a live part owner.
                    if (owner != null) Object.Destroy(owner.gameObject);
                    else part.RemovePart();
                    blocker = "clearing runway debris";
                    // Unity destruction is deferred. Recheck physical clearance on the next attempt.
                    return true;
                }
                blocker = "aircraft or debris on runway";
                return true;
            }
            if (!pose.Runway.IsAvailableForTakeoff(null) || pose.Runway.CrossingRunwaysInUse())
            {
                blocker = "runway traffic";
                return true;
            }
            return false;
        }

        // Departure monitoring.

        /// <summary>Watch runway placement until later physics updates confirm departure.</summary>
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

                // Once native takeoff is confirmed, the wing owns the airborne handoff.
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

                // Stop intervening when taxi is no longer the active owner.
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
                    // Do not force takeoff from a displaced or misaligned pose; leave control to native
                    // AI.
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

                // Queue the aligned, stalled runway aircraft before entering takeoff so other traffic
                // holds short.
                if (!launch.Queued)
                {
                    launch.Runway.QueueTakeoff(aircraft);
                    launch.Queued = true;
                }

                // Use IsAvailableForTakeoff. ClearForTakeoff ignores checkCrossing and can recurse
                // indefinitely through mutually crossing strips.
                if (!launch.Runway.IsAvailableForTakeoff(aircraft)) continue;

                pilot.AITakeoffState = new AIPilotTakeoffState();
                pilot.SwitchState(pilot.AITakeoffState);
                Report(launch, "handed to takeoff on " + launch.Runway.GetName(launch.Reverse));

                // Native takeoff now releases the queue at its appropriate launch phase. Dequeuing here
                // would admit traffic while this aircraft is still accelerating.
                launch.Queued = false;
                Release(launch, i);
            }
        }

        internal static void Reset()
        {
            for (int i = launches.Count - 1; i >= 0; i--) Release(launches[i], i);
            launches.Clear();
        }

        /// <summary>Release the launch watch and its runway claim; native taxi/takeoff do not dequeue on
        /// interrupted exit.</summary>
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

        /// <summary>Try native dequeue on every field runway. It removes only this aircraft at the head,
        /// or destroyed head entries; it does not remove this aircraft from behind another live
        /// entry.</summary>
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

        /// <summary>Remove completed landing claims. Native LeaveState can retain live aircraft on the
        /// landing list, blocking takeoff indefinitely after refit; despawn only self-clears plain
        /// RTB.</summary>
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

        // Field lookup.

        /// <summary>Find the friendly field beneath the aircraft for relaunch after native
        /// landing.</summary>
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
