using System;
using System.Reflection;
using HarmonyLib;

// Harmony calls the postfix by name.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Spec M5 §9.1: the game's combat state subscribes to the aircraft's missile warnings once, in its
    /// constructor; <c>LeaveState</c> unsubscribes and <c>EnterState</c> never subscribes again. Every wing member leaves
    /// the combat state when Wing Command adopts it, so an engaged member fought blind to missiles (no evasion, no
    /// countermeasures). Each entry of a wing member re-attaches the handler (removed first: never twice). The game's own
    /// AI is left as it is.</summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), nameof(AIPilotCombatModes.EnterState))]
    internal static class MissileAlertRepair
    {
        /// <summary>Re-attachments this session (automation reads it).</summary>
        public static int Repairs { get; private set; }

        private static readonly MethodInfo Handler = AccessTools.Method(typeof(AIPilotCombatModes), "AICombat_OnMissileAlert");
        private static bool loggedFailure;

        private static void Postfix(AIPilotCombatModes __instance, Pilot pilot)
        {
            Aircraft aircraft = pilot != null ? pilot.aircraft : null;
            WingService wing = WingService.Instance;
            if (aircraft == null || wing == null || Handler == null || !wing.IsMember(aircraft)) return;
            try
            {
                MissileWarning warning = aircraft.GetMissileWarningSystem();
                if (warning == null) return;
                var handler = (Action<MissileWarning.OnMissileWarning>)Delegate.CreateDelegate(
                    typeof(Action<MissileWarning.OnMissileWarning>), __instance, Handler);
                warning.onMissileWarning -= handler;
                warning.onMissileWarning += handler;
                Repairs++;
            }
            catch (Exception e)
            {
                if (loggedFailure) return;
                loggedFailure = true;
                Plugin.Logger.LogWarning($"[Native] could not re-attach the combat state's missile warnings: {e.Message}");
            }
        }
    }
}
