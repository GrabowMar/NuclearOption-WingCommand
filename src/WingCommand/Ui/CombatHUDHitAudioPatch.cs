using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Restores hitmarker audio confirmation when flying in 3rd-person external/orbit camera views.
    ///
    /// In vanilla Nuclear Option, CombatHUD.DisplayHit checks whether the current camera
    /// state is cockpitState. If the player is in orbitState or chaseState, the method
    /// returns early, silencing the hit confirmation audio cue.
    /// This patch hooks CombatHUD.DisplayHit and plays the hitmarker sound through
    /// SoundManager.PlayInterfaceOneShot throttled to prevent audio overlap.
    /// </summary>
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

            // Player is in external orbit or chase camera view
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
