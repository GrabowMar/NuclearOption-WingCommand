using HarmonyLib;

namespace WingCommand
{
 /// <summary>Redirect inbound post-landing taxi to parking so native service-point logic cannot eject
 /// returning crew before settlement. Leave outbound delivery/refit taxi untouched. Drain runway queues
 /// because native taxi/takeoff LeaveState does not dequeue interrupted departures.</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.SwitchState))]
    internal static class WingInboundTaxiPatch
    {
        // Harmony calls this callback by reflection.
#pragma warning disable IDE0051
        // A void prefix redirects the destination state while allowing the original transition to run.
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, ref PilotBaseState state)
        {
            if (__instance == null) return;

            // Limit interception to mod-owned aircraft; native faction resupply taxi remains unchanged.
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing == null) return;

            Aircraft aircraft = __instance.aircraft;
            if (aircraft == null) return;

            bool ours = wing.Find(aircraft) != null || WingDeparture.Contains(aircraft);
            bool hasTakenOff = __instance.flightInfo != null && __instance.flightInfo.HasTakenOff;

            // Preserve outbound delivery and refit taxi.
            if (!TaxiRewritePolicy.ShouldPark(ours, state is AIPilotTaxiState, hasTakenOff))
                return;

            Plugin.LogVerbose(
                "[Recovery] " + aircraft.unitName +
                " has landed; parking instead of taxiing to a service point");
            state = __instance.parkedState;
        }
#pragma warning restore IDE0051
    }

 /// <summary>Suppress native pad/runway ejection only while refit needs a seated pilot. Plain RTB may
 /// disembark normally for settlement.</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
    internal static class WingRefitEjectPatch
    {
#pragma warning disable IDE0051
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            if (__instance == null) return true;

            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing == null) return true;

            WingMember member = wing.Find(__instance);
            bool ours = member != null || WingDeparture.Contains(__instance);
            Pilot pilot = WingRegistry.PrimaryPilot(__instance);
            bool hasTakenOff = pilot != null && pilot.flightInfo != null &&
                               pilot.flightInfo.HasTakenOff;
            bool refit = member != null && member.RefitPending;
            if (!TaxiRewritePolicy.ShouldSuppressEjection(ours, refit, hasTakenOff))
                return true;

            if (pilot != null && !(pilot.currentState is PilotParkedState) &&
                pilot.parkedState != null)
                pilot.SwitchState(pilot.parkedState);

            Plugin.LogVerbose(
                "[Recovery] " + __instance.unitName +
                " held in the seat for refit; stock eject skipped");
            return false;
        }
#pragma warning restore IDE0051
    }

 /// <summary>Release takeoff queues when taxi ends without entering takeoff. This leaving-state check
 /// runs alongside inbound transition redirection; neither prefix cancels the call.</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.SwitchState))]
    internal static class WingTakeoffQueuePatch
    {
        // Harmony calls this callback by reflection.
#pragma warning disable IDE0051
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, PilotBaseState state)
        {
            if (__instance == null || __instance.aircraft == null) return;

            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing == null) return;

            Aircraft aircraft = __instance.aircraft;
            bool ours = wing.Find(aircraft) != null || WingDeparture.Contains(aircraft);
            bool leavingDeparture = __instance.currentState is AIPilotTaxiState ||
                                    __instance.currentState is AIPilotTakeoffState;
            if (!TaxiRewritePolicy.ShouldDrainQueue(ours, leavingDeparture,
                                                    state is AIPilotTakeoffState))
                return;

            WingAirfield.DrainTakeoffQueue(aircraft);
        }
#pragma warning restore IDE0051
    }
}
