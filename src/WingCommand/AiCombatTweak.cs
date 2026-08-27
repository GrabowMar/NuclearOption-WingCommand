using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Scales AI pilot skill and bravery when a pilot enters the stock combat state.
    ///
    /// Both are plain public fields on <c>Aircraft</c> that the combat AI reads every
    /// tick: <c>skill</c> drives aim error and missile reaction time, <c>bravery</c>
    /// feeds target selection (<c>CombatAI.ChooseHQTarget</c>) and threat avoidance.
    /// Scaling them is a far safer lever than patching the private <c>AttackMode</c>
    /// chooser, and it changes behaviour rather than just numbers on a HUD.
    ///
    /// The original values are captured per aircraft so repeated state entries cannot
    /// compound the multiplier, and so the tweak can be switched off mid-mission.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), nameof(AIPilotCombatModes.EnterState))]
    internal static class AiCombatTweak
    {
        private struct Baseline
        {
            public float Skill;
            public float Bravery;
        }

        private static readonly Dictionary<Aircraft, Baseline> baselines =
            new Dictionary<Aircraft, Baseline>();

        /// <summary>
        /// Drop the captured baselines when a mission ends.
        ///
        /// PruneDead already caps the dictionary, so this is not a leak fix — it is here
        /// so every static that survives a mission is cleared in the same place, rather
        /// than this one quietly holding references to destroyed aircraft until the cap
        /// happens to be reached.
        /// </summary>
        public static void Reset() => baselines.Clear();

        [HarmonyPostfix]
        private static void Postfix(AIPilotCombatModes __instance, Pilot pilot)
        {
            if (pilot == null) return;

            Aircraft aircraft = pilot.aircraft;
            if (aircraft == null || aircraft.Player != null) return;

            RebalanceMissileAlert(__instance, aircraft);

            if (!baselines.TryGetValue(aircraft, out Baseline baseline))
            {
                baseline = new Baseline { Skill = aircraft.skill, Bravery = aircraft.bravery };
                baselines[aircraft] = baseline;
            }

            if (!Plugin.Config2.AiTweakEnabled.Value)
            {
                aircraft.skill = baseline.Skill;
                aircraft.bravery = baseline.Bravery;
                return;
            }

            aircraft.skill = Mathf.Max(0.01f, baseline.Skill * Plugin.Config2.AiSkillScale.Value);
            aircraft.bravery = Mathf.Max(0.01f, baseline.Bravery * Plugin.Config2.AiBraveryScale.Value);

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[AI] {aircraft.unitName} skill {baseline.Skill:F2}->{aircraft.skill:F2} " +
                    $"bravery {baseline.Bravery:F2}->{aircraft.bravery:F2}");
            }

            PruneDead();
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

        /// <summary>Keep the baseline table from growing across a long mission.</summary>
        private static void PruneDead()
        {
            if (baselines.Count < 128) return;

            var stale = new List<Aircraft>();
            foreach (KeyValuePair<Aircraft, Baseline> kv in baselines)
            {
                if (kv.Key == null || kv.Key.disabled) stale.Add(kv.Key);
            }
            foreach (Aircraft a in stale) baselines.Remove(a);
        }
    }
}
