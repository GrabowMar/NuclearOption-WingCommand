using System.Collections.Generic;
using HarmonyLib;
using RoadPathfinding;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Rebuild a delivery's native taxi route when it has clearly left the taxi network.
    /// Stock taxi continues to steer, yield, queue for the runway, and enter takeoff.
    /// </summary>
    [HarmonyPatch]
    internal static class DeliveryTaxiRouteGuard
    {
        private const float RouteCheckSeconds = 0.25f;
        private const float OffTaxiRouteMetres = 12f;
        private const float RunwayApproachMetres = 100f;
        private const float StartupGraceSeconds = 12f;

        private static readonly Dictionary<int, float> nextRouteCheck = new Dictionary<int, float>();

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
        private static void RestoreRoute(Pilot pilot, RoadNetwork ___taxiNetwork,
            PathfindingAgent ___pathfinder, Transform ___destinationPoint, bool ___toRunway)
        {
            if (!___toRunway || !IsDelivery(pilot) || ___taxiNetwork == null || !___taxiNetwork.Exists() ||
                ___pathfinder == null || ___destinationPoint == null || pilot.flightInfo == null) return;

            Aircraft aircraft = pilot.aircraft;
            float now = Time.timeSinceLevelLoad;
            int id = aircraft.GetInstanceID();
            if (nextRouteCheck.TryGetValue(id, out float next) && now < next) return;
            nextRouteCheck[id] = now + RouteCheckSeconds;

            float destinationDistance = Vector3.Distance(aircraft.transform.position, ___destinationPoint.position);
            if (destinationDistance <= RunwayApproachMetres) return;
            if (!___taxiNetwork.TryGetNearestPoint(aircraft.GlobalPosition(), out GlobalPosition nearestPoint, out _)) return;

            bool offTaxiRoute = FastMath.Distance(aircraft.GlobalPosition(), nearestPoint) > OffTaxiRouteMetres;
            bool stalled = now - pilot.flightInfo.spawnTime >= StartupGraceSeconds && aircraft.speed < 1f;
            if (!offTaxiRoute && !stalled) return;

            // Pathfind ignores an unchanged target. Clearing first forces a route from the
            // aircraft's current position through the airport's own taxi network.
            ___pathfinder.ClearDestination();
            ___pathfinder.Pathfind(___taxiNetwork, ___destinationPoint.GlobalPosition(), null);
            Plugin.Logger.LogWarning("[Taxi] rebuilt route for " + aircraft.unitName +
                " at " + aircraft.NetworkspawningHangar?.parentAirbase?.name +
                " reason=" + (offTaxiRoute ? "off-network" : "stalled") +
                " distance=" + destinationDistance.ToString("F0"));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AIPilotTaxiState), nameof(AIPilotTaxiState.LeaveState))]
        private static void Forget(Pilot ___pilot)
        {
            if (___pilot?.aircraft != null) nextRouteCheck.Remove(___pilot.aircraft.GetInstanceID());
        }

        internal static void Reset() => nextRouteCheck.Clear();
    }
}