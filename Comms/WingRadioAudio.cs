using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Opens transmissions with the native radio click. Missing scene singletons are retried;
    /// playback exceptions disable audio until mission reset. Subtitles remain available.
    /// </summary>
    internal static class WingRadioAudio
    {
        /// <summary>
        /// Minimum seconds between clicks, preventing overlapping acknowledgements.
        /// </summary>
        private const float MinimumGap = 0.35f;

        private static float lastPlayed = float.MinValue;

        /// <summary>Set once the game's audio has been found to be unreachable, to stop retrying.</summary>
        private static bool unavailable;

        /// <summary>Open a transmission with the game's own radio click.</summary>
        public static void Transmission()
        {
            if (unavailable || Plugin.Settings.Radio.Value != ChatterLevel.TextAndTone) return;
            if (Time.unscaledTime - lastPlayed < MinimumGap) return;

            try
            {
                GameAssets assets = GameAssets.i;
                AudioClip clip = assets != null ? assets.radioStatic : null;
                if (clip == null || SoundManager.i == null) return;

                SoundManager.PlayInterfaceOneShot(clip);
                lastPlayed = Time.unscaledTime;
            }
            catch (Exception e)
            {
                // Not worth a warning per line. Radio chatter is cosmetic, and the subtitle
                // carries the actual information either way.
                unavailable = true;
                Plugin.Logger.LogInfo(
                    "[Comms] radio click unavailable; chatter will be silent: " + e.Message);
            }
        }

        /// <summary>Allow the audio to be found again on the next mission.</summary>
        public static void Reset()
        {
            lastPlayed = float.MinValue;
            unavailable = false;
        }
    }
}
