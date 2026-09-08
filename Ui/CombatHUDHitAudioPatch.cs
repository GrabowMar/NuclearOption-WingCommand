using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Restores throttled hit-confirmation audio in orbit/chase views, where native
    /// CombatHUD.DisplayHit returns before playing its cockpit-only cue.</summary>
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.DisplayHit))]
    internal static class CombatHUDHitAudioPatch
    {
        private static float lastSoundTime;

        [HarmonyPrefix]
        private static void Prefix(CombatHUD __instance, GlobalPosition hitPosition, Unit hitUnit)
        {
            if (!Plugin.Settings.ExternalHitmarkerAudio.Value) return;

            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || cam.currentState == cam.cockpitState) return;

            // Play the supplemental cue in external orbit or chase view.
            if (cam.currentState == cam.orbitState || cam.currentState == cam.chaseState)
            {
                if (Time.unscaledTime - lastSoundTime >= 0.06f)
                {
                    lastSoundTime = Time.unscaledTime;
                    AudioClip clip = GameAssets.i != null ? GameAssets.i.hitMarkerSound : null;
                    if (clip != null)
                    {
                        SoundManager.PlayInterfaceOneShot(clip);
                    }
                }
            }
        }
    }
}
