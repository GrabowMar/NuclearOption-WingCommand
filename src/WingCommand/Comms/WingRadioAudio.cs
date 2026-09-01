using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The click that goes with a squadron transmission.
    ///
    /// The subtitle card appeared in silence, which reads as a caption rather than a radio
    /// call. The game already has exactly the right sound and already plays it for its own
    /// radio traffic: <c>MessageManager.HQMessageInternal</c>, <c>MissionMessages</c> and
    /// <c>FactionHQ</c> all open a message with <c>GameAssets.i.radioStatic</c> through
    /// <c>SoundManager.PlayInterfaceOneShot</c>. Borrowing it means wing chatter sounds like
    /// the rest of the mission's radio instead of like a mod, and costs no shipped assets.
    ///
    /// Both singletons are loaded asynchronously by the game (<c>ResourcesAsyncLoader</c>),
    /// so neither is guaranteed to exist when a call is made — the first transmission can
    /// land during a scene load. Everything here is best-effort: a transmission that cannot
    /// be voiced still shows its subtitle, and one failure stops further attempts rather than
    /// throwing once per line for the rest of the mission.
    /// </summary>
    internal static class WingRadioAudio
    {
        /// <summary>
        /// Shortest gap between clicks, in seconds.
        ///
        /// The subtitle queue deliberately staggers a flight answering in turn, and each of
        /// those is a separate transmission. Without a floor here a four-ship acknowledging
        /// one order fires four overlapping clicks into the same one-shot source, which
        /// sounds like static rather than like a radio.
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
