using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoadPathfinding;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Keep wing departures on the native taxiway route and slow before its corners.
    /// Native taxi still owns obstacle braking, runway queues and the takeoff transition.
    /// </summary>
    [HarmonyPatch]
    internal static class DeliveryTaxiRouteGuard
    {
        private sealed class RouteState
        {
            internal Pilot Pilot;
            internal float NextRouteCheck;
            internal float LastRebuild = float.NegativeInfinity;
            internal float SpeedLimit = TaxiRoutePolicy.MaximumSpeed;
            internal float CornerRadius;
            internal bool NoRoute;
        }

        private static readonly Dictionary<PathfindingAgent, RouteState> routes =
            new Dictionary<PathfindingAgent, RouteState>();
        private static readonly MethodInfo nextWaypoints =
            AccessTools.Method(typeof(PathfindingAgent), "GetNextWaypoints");
        private static readonly AccessTools.FieldRef<PathfindingAgent, List<GlobalPosition>> waypointList =
            AccessTools.FieldRefAccess<PathfindingAgent, List<GlobalPosition>>("waypoints");
        private static readonly AccessTools.FieldRef<PathfindingAgent, List<Node>> nodeList =
            AccessTools.FieldRefAccess<PathfindingAgent, List<Node>>("nodes");

        private static bool IsDelivery(Pilot pilot)
        {
            Aircraft aircraft = pilot?.aircraft;
            if (aircraft == null || !aircraft.LocalSim || aircraft.Player != null || pilot.dead || pilot.ejected)
                return false;
            WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
            return member != null && member.DeliveryPending;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AIPilotTaxiState), nameof(AIPilotTaxiState.FixedUpdateState))]
        private static void PrepareRoute(Pilot pilot, RoadNetwork ___taxiNetwork,
            PathfindingAgent ___pathfinder, Transform ___destinationPoint, bool ___toRunway,
            bool ___yielding, bool ___waitingAtRunwayCrossing, bool ___waitingForTakeoffClearance,
            bool ___disembarking, float ___brakeUrgency, float ___stuckTimerSpeed)
        {
            if (!___toRunway || !IsDelivery(pilot) || ___pathfinder == null ||
                ___destinationPoint == null || ___taxiNetwork == null || !___taxiNetwork.Exists() ||
                pilot.flightInfo == null) return;

            if (!routes.TryGetValue(___pathfinder, out RouteState state))
                routes.Add(___pathfinder, state = new RouteState
                {
                    Pilot = pilot,
                    CornerRadius = TaxiRoutePolicy.CornerRadius(pilot.aircraft.maxRadius),
                });
            state.NoRoute = false;
            state.SpeedLimit = TaxiRoutePolicy.MaximumSpeed;

            Aircraft aircraft = pilot.aircraft;
            float now = Time.timeSinceLevelLoad;
            if (now < state.NextRouteCheck) return;
            state.NextRouteCheck = now + 0.25f;
            bool waiting = ___yielding || ___waitingAtRunwayCrossing ||
                           ___waitingForTakeoffClearance || ___disembarking || ___brakeUrgency > 0.1f;
            bool hasRoute = waypointList(___pathfinder).Count > 0 || nodeList(___pathfinder).Count > 0;
            bool offNetwork = ___taxiNetwork.TryGetNearestPoint(aircraft.GlobalPosition(),
                out GlobalPosition nearestPoint, out _) &&
                FastMath.Distance(aircraft.GlobalPosition(), nearestPoint) > 12f;
            // Hangar exits intentionally start off-network. Let the original apron route
            // carry the airframe out before considering a network recovery.
            if (FastMath.InRange(aircraft.startPosition, aircraft.GlobalPosition(), aircraft.maxRadius * 3f))
                offNetwork = false;
            // Native Pathfind ignores targets within 10 m of its stored destination;
            // ClearDestination there would empty the final approach and strand takeoff.
            if (!TaxiRoutePolicy.ShouldRebuild(waiting, hasRoute, offNetwork, ___stuckTimerSpeed,
                now - pilot.flightInfo.spawnTime, now - state.LastRebuild,
                Vector3.Distance(aircraft.transform.position, ___destinationPoint.position))) return;

            state.LastRebuild = now;
            ___pathfinder.ClearDestination();
            ___pathfinder.Pathfind(___taxiNetwork, ___destinationPoint.GlobalPosition(), null);
            Plugin.Logger.LogWarning("[Taxi] rebuilt native route for " + aircraft.unitName +
                " reason=" + (!hasRoute ? "missing route" : offNetwork ? "off-network" : "stalled"));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathfindingAgent), nameof(PathfindingAgent.GetSteerpoint))]
        private static bool FollowTaxiway(PathfindingAgent __instance, GlobalPosition position,
            Vector3 forward, List<GlobalPosition> ___waypoints, List<Node> ___nodes,
            ref SteeringInfo? __result)
        {
            if (!routes.TryGetValue(__instance, out RouteState state) || !IsDelivery(state.Pilot) ||
                !(state.Pilot.currentState is AIPilotTaxiState)) return true;

            // Preserve the game's route and its node expansion. Only progression differs:
            // no 20 m corner skip, no 60 m heading-based shortcut across the apron.
            while (true)
            {
                if (___waypoints.Count < 3 && ___nodes.Count > 0)
                    ___waypoints.AddRange((List<GlobalPosition>)nextWaypoints.Invoke(
                        __instance, new object[] { ___nodes }));
                if (___waypoints.Count == 0)
                {
                    state.NoRoute = true;
                    __result = SteeringInfo.None;
                    return false;
                }

                Vector3 to = ___waypoints[0] - position;
                Vector3 next = ___waypoints.Count > 1 ? ___waypoints[1] - ___waypoints[0] : Vector3.zero;
                to.y = 0f;
                next.y = 0f;
                // Keep the final point so the native runway transition can observe arrival
                // on its one-second clearance check instead of accelerating without a route.
                if (___waypoints.Count > 1 && TaxiRoutePolicy.CanAdvance(
                    to.x, to.z, next.x, next.z, state.CornerRadius))
                {
                    ___waypoints.RemoveAt(0);
                    continue;
                }

                forward.y = 0f;
                float heading = Vector3.Angle(forward, to);
                float corner = next.sqrMagnitude > 0.01f ? Vector3.Angle(to, next) : 0f;
                state.SpeedLimit = TaxiRoutePolicy.SpeedLimit(to.magnitude, corner, heading, state.CornerRadius);
                // Scale the rounded corner to the airframe's wheelbase without discarding
                // a bend at the stock 20 m approach / 60 m behind-heading distances.
                Vector3 aim = to;
                float anticipation = state.CornerRadius * 2f;
                if (next.sqrMagnitude > 0.01f && to.magnitude < anticipation)
                    aim += next.normalized * Mathf.Clamp(anticipation - to.magnitude, 0f, state.CornerRadius);
                __result = new SteeringInfo(aim, corner);
                return false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AIPilotTaxiState), nameof(AIPilotTaxiState.FixedUpdateState))]
        private static void LimitTaxiSpeed(Pilot pilot, PathfindingAgent ___pathfinder,
            bool ___yielding, bool ___disembarking)
        {
            if (___pathfinder == null || !routes.TryGetValue(___pathfinder, out RouteState state) ||
                !IsDelivery(pilot) || !(pilot.currentState is AIPilotTaxiState) ||
                ___yielding || ___disembarking) return;
            var inputs = pilot.aircraft.GetInputs();
            if (inputs == null) return;
            if (state.NoRoute)
            {
                inputs.throttle = 0.01f;
                inputs.brake = 1f;
                return;
            }
            float speed = pilot.aircraft.speed;
            inputs.throttle = Mathf.Min(inputs.throttle,
                Mathf.Clamp(0.5f + (state.SpeedLimit - speed) * 0.3f, 0.01f, 0.5f));
            if (speed > state.SpeedLimit) inputs.brake = Mathf.Max(inputs.brake, 1f);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AIPilotTaxiState), nameof(AIPilotTaxiState.LeaveState))]
        private static void Forget(PathfindingAgent ___pathfinder)
        {
            if (___pathfinder != null) routes.Remove(___pathfinder);
        }

        internal static void Reset() => routes.Clear();
    }
}
