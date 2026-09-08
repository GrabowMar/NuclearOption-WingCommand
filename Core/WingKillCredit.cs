using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
 /// <summary>Infers kill credit from recent wingman shots; native scoring does not expose arbitrary
 /// killers. Despawns and friendly finishing shots may count. Each target is credited once. ponytail:
 /// shot-window heuristic; use native killer attribution if it becomes available.</summary>
    internal static class WingKillCredit
    {
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

                WingPilotRoster.NoteKill(shooter, victim);
            }
        }
    }
}
