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

        /// <summary>True while the player is actively attacking ground targets.</summary>
        public static bool GroundAttackOpen => Time.timeSinceLevelLoad < windowUntil;

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

            windowUntil = Time.timeSinceLevelLoad + Plugin.Settings.MirrorWindowSeconds.Value;

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Plugin.Logger.LogInfo(
                    "[Wing] player attacking ground - wing weapons free for " +
                    Plugin.Settings.MirrorWindowSeconds.Value + "s");
            }
        }
    }
}
