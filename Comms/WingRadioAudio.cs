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

        private static float lastPlayed = -100f;

        /// <summary>Stop retrying after a playback failure.</summary>
        private static bool unavailable;

        /// <summary>Distinct cockpit avionics audio cues.</summary>
        public enum Earcon
        {
            Transmission,
            Wilco,
            Unable,
            ThreatAlarm,
            Splash,
            RoeCycle,
            RadialTick,
        }

        private static AudioClip wilcoClip;
        private static AudioClip unableClip;
        private static AudioClip threatClip;
        private static AudioClip splashClip;
        private static AudioClip roeClip;
        private static AudioClip radialTickClip;
        private static float lastTickPlayed = -100f;

        /// <summary>Play the native transmission-opening click.</summary>
        public static void Transmission() => Play(Earcon.Transmission);

        /// <summary>Play a distinct avionics earcon.</summary>
        public static void Play(Earcon earcon)
        {
            if (unavailable || Plugin.Settings == null || Plugin.Settings.Radio.Value == ChatterLevel.Off) return;
            if (earcon == Earcon.Transmission && Plugin.Settings.Radio.Value != ChatterLevel.TextAndTone) return;
            if (earcon == Earcon.Transmission && Time.unscaledTime - lastPlayed < MinimumGap) return;
            if (earcon == Earcon.RadialTick)
            {
                if (Time.unscaledTime - lastTickPlayed < 0.045f) return;
                lastTickPlayed = Time.unscaledTime;
            }

            try
            {
                if (SoundManager.i == null) return;

                AudioClip clip = ResolveClip(earcon);
                if (clip == null) return;

                if (earcon == Earcon.ThreatAlarm)
                    SoundManager.PlayRadarWarningOneShot(clip);
                else
                    SoundManager.PlayInterfaceOneShot(clip);

                lastPlayed = Time.unscaledTime;
            }
            catch (Exception e)
            {
                unavailable = true;
                Plugin.LogVerbose("[Comms] audio playback unavailable: " + e.Message);
            }
        }

        private static AudioClip ResolveClip(Earcon earcon)
        {
            switch (earcon)
            {
                case Earcon.Transmission:
                    GameAssets assets = GameAssets.i;
                    return assets != null ? assets.radioStatic : null;
                case Earcon.Wilco:
                    return wilcoClip ?? (wilcoClip = CreateChime("Earcon_Wilco", 520f, 780f, 0.09f, 0.22f));
                case Earcon.Unable:
                    return unableClip ?? (unableClip = CreateBuzz("Earcon_Unable", 240f, 0.12f, 0.20f));
                case Earcon.ThreatAlarm:
                    return threatClip ?? (threatClip = CreateWarble("Earcon_Threat", 950f, 1300f, 0.14f, 0.30f));
                case Earcon.Splash:
                    return splashClip ?? (splashClip = CreateChime("Earcon_Splash", 880f, 1100f, 0.07f, 0.25f));
                case Earcon.RoeCycle:
                    return roeClip ?? (roeClip = CreateChime("Earcon_Roe", 650f, 650f, 0.05f, 0.18f));
                case Earcon.RadialTick:
                    return radialTickClip ?? (radialTickClip = CreateChime("Earcon_RadialTick", 1200f, 1400f, 0.025f, 0.12f));
                default:
                    return null;
            }
        }

        private static AudioClip CreateChime(string name, float fStart, float fEnd, float duration, float volume)
        {
            const int sampleRate = 22050;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = t / duration;
                float freq = Mathf.Lerp(fStart, fEnd, progress);
                float envelope = Mathf.Sin(Mathf.PI * progress);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateBuzz(string name, float freq, float duration, float volume)
        {
            const int sampleRate = 22050;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = t / duration;
                float envelope = Mathf.Sin(Mathf.PI * progress);
                // Square wave approximation for buzz
                float sin = Mathf.Sin(2f * Mathf.PI * freq * t);
                data[i] = (sin >= 0f ? 1f : -1f) * envelope * volume * 0.5f;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateWarble(string name, float fStart, float fEnd, float duration, float volume)
        {
            const int sampleRate = 22050;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = t / duration;
                float envelope = Mathf.Sin(Mathf.PI * progress);
                float freq = Mathf.Lerp(fStart, fEnd, Mathf.PingPong(progress * 4f, 1f));
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Retry audio discovery next mission.</summary>
        public static void Reset()
        {
            lastPlayed = -100f;
            unavailable = false;
        }
    }
}
