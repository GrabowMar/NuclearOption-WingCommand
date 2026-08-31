using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Works out which wingman to credit when something they were shooting at dies.
    ///
    /// Nuclear Option's own scoring is not exposed to a plugin in a form that names the
    /// killer of an arbitrary unit, so this infers it from what the mod already knows: the
    /// shots it told a wingman to take. A contact that a wingman fired on and that stops
    /// existing within the credit window is counted as that wingman's.
    ///
    /// It is an inference, and it is worth being plain about what it can get wrong. A
    /// target finished off by the player or by friendly AI moments after a wingman shot at
    /// it is credited to the wingman, and a unit that despawns rather than dies looks the
    /// same as one destroyed. Both err towards generosity, which is the right direction for
    /// a flavour statistic driving a deliberately small rank effect — and neither can
    /// double-count, because a target is only ever credited once.
    /// </summary>
    internal static class WingKillCredit
    {
        /// <summary>How long after a shot a target's death still counts as that pilot's.</summary>
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

        /// <summary>Remember that this aircraft has just fired on this target.</summary>
        public static void NoteShot(Aircraft shooter, Unit target)
        {
            if (!Plugin.Config2.PilotProgression.Value) return;
            if (shooter == null || target == null || target is Missile) return;

            float expires = Time.timeSinceLevelLoad + CreditWindow;

            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Shooter != shooter || pending[i].Target != target) continue;

                // Re-attacking the same contact extends the claim rather than queueing a
                // second one, so a long engagement cannot pay out twice.
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

        /// <summary>
        /// Settle outstanding claims. Throttled: this walks a short list looking for a
        /// state change that takes seconds, and there is nothing to gain from doing it at
        /// frame rate.
        /// </summary>
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

                // Drop every other outstanding claim on the same contact: one dead unit is
                // one kill, however many wingmen were shooting at it.
                for (int j = pending.Count - 1; j >= 0; j--)
                {
                    if (pending[j].Target == victim) pending.RemoveAt(j);
                }

                WingPilotRoster.NoteKill(shooter, victim);
            }
        }
    }
}
