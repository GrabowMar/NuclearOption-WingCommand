using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Watches the player's own trigger so Defensive wingmen can mirror their ground
    /// attack — "fire on ground targets only when I fire".
    ///
    /// <c>Pilot.onFire</c> is a public event on the player's pilot, so this needs no
    /// patching. Firing an anti-surface weapon opens a time-boxed window during which
    /// Defensive wingmen may engage ground targets; the window closes on its own, so the
    /// wing goes quiet again once the player stops attacking.
    /// </summary>
    internal static class PlayerFireWatcher
    {
        private static Pilot watched;
        private static float windowUntil;
        private static Unit lastPlayerTarget;

        /// <summary>True while the player is actively attacking ground targets.</summary>
        public static bool GroundAttackOpen => Time.timeSinceLevelLoad < windowUntil;

        /// <summary>
        /// The player's most recent ground target, so wingmen can concentrate fire rather
        /// than each picking something different. May be null.
        /// </summary>
        public static Unit PreferredTarget =>
            GroundAttackOpen && lastPlayerTarget != null && !lastPlayerTarget.disabled
                ? lastPlayerTarget
                : null;

        /// <summary>Rebind to the player's current aircraft. Cheap enough to call each frame.</summary>
        public static void Track(Aircraft playerAircraft)
        {
            Pilot pilot = playerAircraft != null ? WingRegistry.PrimaryPilot(playerAircraft) : null;
            if (pilot == watched) return;

            if (watched != null) watched.onFire -= OnPlayerFired;
            watched = pilot;
            if (watched != null) watched.onFire += OnPlayerFired;
        }

        public static void Reset()
        {
            if (watched != null) watched.onFire -= OnPlayerFired;
            watched = null;
            windowUntil = 0f;
            lastPlayerTarget = null;
        }

        private static void OnPlayerFired()
        {
            Aircraft aircraft = watched != null ? watched.aircraft : null;
            if (aircraft == null) return;

            WeaponStation station = aircraft.weaponManager?.currentWeaponStation;
            if (station == null || station.WeaponInfo == null) return;

            // Only an anti-surface shot counts. Firing an air-to-air missile should not
            // turn the wing loose on a village.
            RoleIdentity role = station.WeaponInfo.effectiveness;
            if (role.antiSurface <= role.antiAir) return;

            windowUntil = Time.timeSinceLevelLoad + Plugin.Config2.MirrorWindowSeconds.Value;
            lastPlayerTarget = watched.GetPrimaryTarget();

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Plugin.Logger.LogInfo(
                    "[Wing] player attacking ground - wing weapons free for " +
                    Plugin.Config2.MirrorWindowSeconds.Value + "s");
            }
        }
    }
}
