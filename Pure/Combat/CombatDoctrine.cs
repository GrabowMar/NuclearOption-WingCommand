using System;

namespace WingCommand
{
    /// <summary>After Winchester (spec M5 §6.1): back into formation, home to the reserve, or home to refit.</summary>
    internal enum WinchesterAction : byte { Rejoin, Rtb, Refit }

    /// <summary>After bingo: home to the reserve, or home to refit.</summary>
    internal enum BingoAction : byte { Rtb, Refit }

    /// <summary>Where a member goes after it is taken back from combat or reaches bingo in formation (spec M5 §6.1): one
    /// rule for every take-back.</summary>
    internal static class CombatDoctrine
    {
        public static bool FollowOn(TransitionReason reason, WinchesterAction winchester, BingoAction bingo, out RecoveryIntent intent)
        {
            intent = RecoveryIntent.Rtb;
            if (reason == TransitionReason.Fuel)
            {
                intent = bingo == BingoAction.Refit ? RecoveryIntent.Refit : RecoveryIntent.Rtb;
                return true;
            }
            if (reason != TransitionReason.Winchester || winchester == WinchesterAction.Rejoin) return false;
            intent = winchester == WinchesterAction.Refit ? RecoveryIntent.Refit : RecoveryIntent.Rtb;
            return true;
        }
    }

    /// <summary>Spec M5 §6.3: an engaged wing facing at least <c>ratio</c> hostiles per engaged member for
    /// <see cref="DwellSeconds"/> falls back. Engage while outnumbered is refused once; pressing it again within
    /// <see cref="ConfirmSeconds"/> overrides the judge until <see cref="Reset"/>. A ratio of 0 turns it off.</summary>
    internal sealed class OutnumberedJudge
    {
        public static float DwellSeconds = 5f, ConfirmSeconds = 10f, RadiusMetres = 15000f;

        private float clock, refusedAt = float.NegativeInfinity;

        public bool Overridden { get; private set; }

        public static bool Outnumbered(int hostiles, int members, float ratio) =>
            ratio > 0f && members > 0 && hostiles >= ratio * members;

        public static float ThreatMinRadarAlt = 10f, ThreatMinAntiAir = 0.2f;

        /// <summary>A hostile air threat (review M5d I2): an accurate track (not a last known position), off the ground,
        /// and at or above the game's own anti-air cut-off (transports and bombers are not).</summary>
        public static bool IsAirThreat(bool accurate, float radarAlt, float antiAir) =>
            accurate && radarAlt > ThreatMinRadarAlt && antiAir >= ThreatMinAntiAir;

        /// <summary>True on the tick the wing has been outnumbered for the dwell (then the clock restarts).</summary>
        public bool Update(int hostiles, int members, float ratio, float dt)
        {
            if (Overridden || !Outnumbered(hostiles, members, ratio))
            {
                clock = 0f;
                return false;
            }
            clock += dt;
            if (clock < DwellSeconds) return false;
            clock = 0f;
            return true;
        }

        public bool AllowEngage(int hostiles, int members, float ratio, float now)
        {
            if (!Outnumbered(hostiles, members, ratio)) return true;
            // A refusal from a later clock (the previous mission's) is no confirmation (review M5d I1).
            if (now >= refusedAt && now - refusedAt <= ConfirmSeconds)
            {
                Overridden = true;
                return true;
            }
            refusedAt = now;
            return false;
        }

        public void Reset()
        {
            clock = 0f;
            refusedAt = float.NegativeInfinity;
            Overridden = false;
        }
    }

    /// <summary>Spec M5 §6.4: the game's target score (<c>opportunity × (1 + threat) / range</c>, halved beyond 1.2 × the
    /// weapon's range) under reservation pressure: members already after the target, and the player's own target as
    /// <see cref="PlayerTargetWeight"/> members, beyond the <c>capacity</c> the weapon needs.</summary>
    internal static class TargetSpread
    {
        public static float SaturationPenalty = 1.5f, PlayerTargetWeight = 2f, MinRange = 500f;

        public static float Score(float opportunity, float threat, float range, float maxRange, int committed, bool playerTarget, int capacity)
        {
            float score = opportunity * (1f + threat) / Math.Max(range, MinRange);
            if (range > maxRange * 1.2f) score *= 0.5f;
            float load = committed + (playerTarget ? PlayerTargetWeight : 0f);
            return score / (1f + Math.Max(load - capacity + 1f, 0f) * SaturationPenalty);
        }
    }
}
