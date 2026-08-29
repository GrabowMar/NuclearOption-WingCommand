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
        public static void Reset() { }

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
        /// Works around an unbalanced subscription in the stock combat state.
        ///
        /// <c>AIPilotCombatModes</c> subscribes <c>AICombat_OnMissileAlert</c> to
        /// <c>MissileWarning.onMissileWarning</c> in its *constructor*, but unsubscribes it
        /// in <c>LeaveState</c>. Its other three event handlers are added in
        /// <c>EnterState</c> and so re-attach correctly; this one does not. The first time
        /// a pilot leaves the combat state the handler is gone for good, and that AI never
        /// reacts to a missile warning again.
        ///
        /// Vanilla rarely leaves the combat state, so this is mostly latent — but this mod
        /// switches pilots in and out of it constantly, which would turn a latent bug into
        /// a guaranteed one. Removing then re-adding normalises the invocation list to
        /// exactly one entry, so it is a no-op on the first entry and a repair on later ones.
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
