using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Standing rules of engagement for the whole wing.
    ///
    /// This is deliberately separate from <see cref="WingOrder"/>: an order is a one-off
    /// command ("rejoin now"), a posture is the policy a wingman applies continuously while
    /// holding formation.
    /// </summary>
    internal enum WingPosture
    {
        /// <summary>
        /// Holds the slot no matter what. Intercepts inbound missiles, engages close air
        /// threats from the slot, and attacks ground targets only while the player is
        /// attacking ground.
        /// </summary>
        Defensive,

        /// <summary>
        /// Breaks formation to fight aircraft, then rejoins automatically. Engages ground
        /// targets from the slot without waiting for the player.
        /// </summary>
        Aggressive,
    }

    /// <summary>Resolves what a wingman may shoot at, given posture and situation.</summary>
    internal static class PostureRules
    {
        /// <summary>
        /// Whether this posture permits leaving formation to fight the given target.
        /// Ground attack never justifies a break: manoeuvring buys little against ground
        /// units, so wingmen shoot those from the slot in both postures.
        /// </summary>
        public static bool MayBreakFor(WingPosture posture, Unit target)
        {
            if (posture != WingPosture.Aggressive || target == null) return false;
            return target.definition.typeIdentity.air > 0.5f;
        }

        /// <summary>What the wingman may currently fire at.</summary>
        public static WingWeapons.Allow WeaponsFree(WingPosture posture, Aircraft aircraft)
        {
            if (posture == WingPosture.Aggressive)
                return WingWeapons.Allow.AirAndGround;

            // Defensive: missile defence takes priority over everything else, because a
            // missile in the air is the most time-critical thing on the battlefield.
            if (Plugin.Config2.MissileDefence.Value && WingWeapons.HasMissileDefence(aircraft))
            {
                MissileWarning warning = aircraft.GetMissileWarningSystem();
                if (warning != null && warning.IsWarning())
                    return WingWeapons.Allow.MissilesOnly;

                Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
                MissileWarning leaderWarning = leader != null ? leader.GetMissileWarningSystem() : null;
                if (leaderWarning != null && leaderWarning.IsWarning())
                    return WingWeapons.Allow.MissilesOnly;
            }

            // Ground attack only while the player is attacking ground.
            if (PlayerFireWatcher.GroundAttackOpen)
                return WingWeapons.Allow.AirAndGround;

            return WingWeapons.Allow.AirOnly;
        }

        /// <summary>Engagement range for a posture, in metres.</summary>
        public static float EngageRange(WingPosture posture)
        {
            return posture == WingPosture.Aggressive
                ? Plugin.Config2.AggressiveEngageRange.Value
                : Plugin.Config2.DefensiveEngageRange.Value;
        }
    }
}
