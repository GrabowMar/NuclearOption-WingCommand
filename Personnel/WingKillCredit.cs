using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Infers kill credit from recent wingman shots; native scoring does not expose arbitrary
    /// killers. Despawns and friendly finishing shots may count. Each target is credited once. ponytail:
    /// shot-window heuristic; use native killer attribution if it becomes available.</summary>
    internal static class WingKillCredit
    {
        /// <summary>Review M5g C1: the game's kill message names the killer — a wing pilot's kill is credited from it
        /// (host only; never a friendly or a missile).</summary>
        public static void Credit(PersistentID killerID, PersistentID killedID)
        {
            if (!UnitRegistry.TryGetPersistentUnit(killerID, out PersistentUnit k) || !(k.unit is Aircraft shooter) || !shooter.IsServer) return;
            // The persistent record outlives the destroyed victim (its type and side stay known).
            if (!UnitRegistry.TryGetPersistentUnit(killedID, out PersistentUnit victim) || victim.unit is Missile ||
                victim.GetHQ() == shooter.NetworkHQ) return;
            string type = victim.definition != null ? victim.definition.unitName : victim.unitName;
            WingPilotRoster.NoteKill(shooter, killedID.Id, type, Plugin.Settings.PilotProgression.Value);
        }

        private static string TypeOf(Unit u) =>
            u == null ? "target" : u.definition != null ? u.definition.unitName : u.unitName;

        /// <summary>Seconds after a shot during which target disappearance earns credit.</summary>
        private const float CreditWindow = 25f;

        private sealed class PendingCredit
        {
            public Aircraft Shooter;
            public Unit Target;
            public float ExpiresAt;
        }

        private static readonly List<PendingCredit> pending = new List<PendingCredit>();
        private static float nextTick;

        public static void Reset()
        {
            pending.Clear();
            nextTick = 0f;
        }

        /// <summary>Record a wingman's shot at a target.</summary>
        public static void NoteShot(Aircraft shooter, Unit target)
        {
            if (!Plugin.Settings.PilotProgression.Value) return;
            if (shooter == null || target == null || target is Missile) return;

            float expires = Time.timeSinceLevelLoad + CreditWindow;

            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Shooter != shooter || pending[i].Target != target) continue;

                // Extend the existing claim so repeated attacks cannot award duplicate kills.
                pending[i].ExpiresAt = expires;
                return;
            }

            pending.Add(new PendingCredit
            {
                Shooter = shooter,
                Target = target,
                ExpiresAt = expires,
            });
        }

        /// <summary>Settle shot claims periodically; target disappearance does not need a frame-rate
        /// scan.</summary>
        public static void Tick()
        {
            if (pending.Count == 0 || Time.timeSinceLevelLoad < nextTick) return;
            nextTick = Time.timeSinceLevelLoad + 0.5f;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingCredit credit = pending[i];

                if (credit.Shooter == null || credit.Shooter.disabled ||
                    Time.timeSinceLevelLoad >= credit.ExpiresAt)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (credit.Target != null && !credit.Target.disabled) continue;

                Unit victim = credit.Target;
                Aircraft shooter = credit.Shooter;
                pending.RemoveAt(i);

                // Remove competing claims so one target awards one kill.
                for (int j = pending.Count - 1; j >= 0; j--)
                {
                    if (!ReferenceEquals(pending[j].Target, victim)) continue;
                    pending.RemoveAt(j);
                    if (j < i) i--;
                }

                WingPilotRoster.NoteKill(shooter, victim != null ? victim.persistentID.Id : 0u, TypeOf(victim), true);
            }
        }
    }

    // Harmony calls the prefix by name.
#pragma warning disable IDE0051
    /// <summary>Review M5g C1: every kill message (host and received) passes the killer and victim to the kill credit.</summary>
    [HarmonyPatch]
    internal static class WingKillMessagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.FirstMethod(typeof(MessageManager),
            method => method.Name.StartsWith("UserCode_RpcKillMessage_"));

        [HarmonyPrefix]
        private static void Prefix(PersistentID killerID, PersistentID killedID) => WingKillCredit.Credit(killerID, killedID);
    }
#pragma warning restore IDE0051
}
