using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Plays native radio clicks. Retry missing singletons; disable playback after exceptions
    /// until mission reset. Subtitles remain available.</summary>
    internal static class WingRadioAudio
    {
        /// <summary>Minimum click spacing in seconds to prevent overlap.</summary>
        private const float MinimumGap = 0.35f;

        private static float lastPlayed = float.MinValue;

        /// <summary>Stop retrying after a playback failure.</summary>
        private static bool unavailable;

        /// <summary>Play the native transmission-opening click.</summary>
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
                // Disable failed audio quietly; subtitles still carry the message.
                unavailable = true;
                Plugin.LogVerbose(
                    "[Comms] radio click unavailable; chatter will be silent: " + e.Message);
            }
        }

        /// <summary>Retry audio discovery next mission.</summary>
        public static void Reset()
        {
            lastPlayed = float.MinValue;
            unavailable = false;
        }
    }
}
