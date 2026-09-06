using System;
using System.Reflection;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>
    /// Repairs the stock missile-warning subscription when this mod moves a pilot back
    /// into combat. It deliberately does not alter global AI skill or bravery.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), nameof(AIPilotCombatModes.EnterState))]
    internal static class AiCombatTweak
    {
        [HarmonyPostfix]
        private static void Postfix(AIPilotCombatModes __instance, Pilot pilot)
        {
            if (pilot == null) return;

            Aircraft aircraft = pilot.aircraft;
            if (aircraft == null || aircraft.Player != null) return;

            RebalanceMissileAlert(__instance, aircraft);
        }

        private static readonly MethodInfo MissileAlertHandler =
            AccessTools.Method(typeof(AIPilotCombatModes), "AICombat_OnMissileAlert");

        private static bool loggedRebalanceFailure;

        /// <summary>
        /// The stock state subscribes in its constructor but unsubscribes in LeaveState.
        /// Remove then add on entry to restore the handler without duplicating its subscription.
        /// </summary>
        private static void RebalanceMissileAlert(AIPilotCombatModes state, Aircraft aircraft)
        {
            if (state == null || MissileAlertHandler == null) return;

            MissileWarning warning = aircraft.GetMissileWarningSystem();
            if (warning == null) return;

            try
            {
                var handler = (Action<MissileWarning.OnMissileWarning>)Delegate.CreateDelegate(
                    typeof(Action<MissileWarning.OnMissileWarning>), state, MissileAlertHandler);

                warning.onMissileWarning -= handler;
                warning.onMissileWarning += handler;
            }
            catch (Exception e)
            {
                if (!loggedRebalanceFailure)
                {
                    loggedRebalanceFailure = true;
                    Plugin.Logger.LogWarning(
                        "Could not rebalance the AI missile-alert subscription; wingmen may " +
                        "stop reacting to missiles after leaving formation. " + e.Message);
                }
            }
        }

    }
}
