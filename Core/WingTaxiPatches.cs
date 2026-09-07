using HarmonyLib;

namespace WingCommand
{
    /// <summary>
    /// The one stock transition this mod rewrites: the taxi a wingman is given <i>after</i>
    /// it lands.
    ///
    /// <c>AIPilotLandingState</c> hands a stopped aircraft to <c>AIPilotTaxiState</c> to
    /// find a service point, and <c>AIHeloLandingState</c> reaches the same end by its own
    /// route. That taxi is not a way home: with <c>HasTakenOff</c> already true it looks for
    /// the nearest service point, and if there is none — or if it is already close enough to
    /// one — it sets <c>disembarking</c> and ejects the pilot on the apron. For a faction AI
    /// that is fine, the sortie is over. For a wingman the player ordered home it destroys
    /// the crew and leaves an abandoned airframe blocking the field, moments before
    /// <see cref="WingRecovery"/> would have credited it back into stock.
    ///
    /// So an inbound wingman is parked instead. Parked is what the aircraft ends up as
    /// anyway; skipping the taxi only skips the ejection. Outbound taxi — a delivery on the
    /// runway, a refit leaving the apron — is never touched: rewriting that is what every
    /// failed attempt in <c>docs/airfield-findings.md</c> did.
    ///
    /// The runway queue is drained at the same time. Neither <c>AIPilotTaxiState</c> nor
    /// <c>AIPilotTakeoffState</c> dequeues in <c>LeaveState</c>, so an aircraft that enters
    /// the takeoff queue and then leaves by any route other than actually taking off holds
    /// that strip — and every strip crossing it — against the rest of the mission.
    /// </summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.SwitchState))]
    internal static class WingInboundTaxiPatch
    {
        // Harmony invokes this callback through reflection.
#pragma warning disable IDE0051
        // Void, not bool: this redirects the transition, it never cancels it. The original
        // still runs, with a different destination state.
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, ref PilotBaseState state)
        {
            if (__instance == null) return;

            // Only the aircraft this mod is responsible for. A faction AI taxiing to
            // resupply is the game working as designed.
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing == null) return;

            Aircraft aircraft = __instance.aircraft;
            if (aircraft == null) return;

            bool ours = wing.Find(aircraft) != null || WingDeparture.Contains(aircraft);
            bool hasTakenOff = __instance.flightInfo != null && __instance.flightInfo.HasTakenOff;

            // Outbound taxi — a delivery on the threshold, a refit leaving its parking spot —
            // is left exactly as the game wrote it.
            if (!TaxiRewritePolicy.ShouldPark(ours, state is AIPilotTaxiState, hasTakenOff))
                return;

            Plugin.LogVerbose(
                "[Recovery] " + aircraft.unitName +
                " has landed; parking instead of taxiing to a service point");
            state = __instance.parkedState;
        }
#pragma warning restore IDE0051
    }

    /// <summary>
    /// Helicopters never enter inbound taxi: <c>AIHeloLandingState</c> ejects on the pad.
    /// A parked jet's landing state does the same after ten seconds on the ground. For a
    /// refit that eject is fatal — <see cref="WingMember.CompleteRefit"/> needs a living
    /// seated pilot. For RTB it is the disembark we want, so this only suppresses the
    /// stock eject while a refit is waiting on the pad.
    /// </summary>
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

    /// <summary>
    /// Give back a takeoff slot whenever a wingman leaves taxi by any route other than
    /// starting its takeoff run.
    ///
    /// Separate from <see cref="WingInboundTaxiPatch"/> because it is about the state being
    /// left rather than the one being entered, and because both prefixes have to run: Harmony
    /// applies them in registration order and neither refuses the call.
    /// </summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.SwitchState))]
    internal static class WingTakeoffQueuePatch
    {
        // Harmony invokes this callback through reflection.
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
