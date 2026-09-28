using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    public enum AvUiCue { Hover, Navigate, Press, Engage, Release, Confirm, Caution }

    /// <summary>Quiet, scene-owned, two-dimensional feedback for cockpit controls.</summary>
    public static class AvUiSound
    {
        private const int SampleRate = 44100;
        private static readonly AudioClip[] clips = new AudioClip[7];
        private static readonly float[] gains = { 0f, .15f, .15f, .18f, .13f, .20f, .17f };
        private static AudioSource source;
        private static float lastHover = -1f;
        private static float lastAction = -1f;

        /// <summary>Independent UI level; zero mutes every cue.</summary>
        public static float Volume { get; set; } = .7f;

        public static void Play(AvUiCue cue)
        {
            if (Volume <= 0f || cue == AvUiCue.Hover) return;
            if (!Ensure()) return;
            float now = Time.unscaledTime;
            if (cue == AvUiCue.Hover)
            {
                if (now - lastHover < .09f || now - lastAction < .06f) return;
                lastHover = now;
            }
            else
            {
                if (now - lastAction < .025f) return;
                lastAction = now;
            }
            source.pitch = 1f;
            source.PlayOneShot(clips[(int)cue], gains[(int)cue] * Mathf.Clamp01(Volume));
        }

        /// <summary>Compatibility for feature-owned controls.</summary>
        public static void Tick(float volume = .4f) => Play(AvUiCue.Press);

        public static void Reset()
        {
            if (source != null) Object.Destroy(source.gameObject);
            source = null;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null) Object.Destroy(clips[i]);
                clips[i] = null;
            }
            lastHover = lastAction = -1f;
        }

        private static bool Ensure()
        {
            if (source != null && clips[0] != null) return true;
            Reset();
            var host = new GameObject("NOAvionics.UiSound", typeof(AudioSource));
            source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            for (int i = 0; i < clips.Length; i++) clips[i] = Build((AvUiCue)i);
            return clips[0] != null;
        }

        private static AudioClip Build(AvUiCue cue)
        {
            // Soft attack, low-mid fundamental, sparse harmonics and a little filtered
            // contact texture. Every tail reaches zero to avoid a cut when repeated.
            float seconds, frequency, glide, decay, texture;
            switch (cue)
            {
                case AvUiCue.Hover:    seconds = .035f; frequency = 300f; glide = 0f; decay = 55f; texture = .02f; break;
                case AvUiCue.Navigate: seconds = .060f; frequency = 350f; glide = -25f; decay = 48f; texture = .035f; break;
                case AvUiCue.Engage:   seconds = .080f; frequency = 390f; glide = -30f; decay = 38f; texture = .035f; break;
                case AvUiCue.Release:  seconds = .065f; frequency = 285f; glide = -20f; decay = 44f; texture = .03f; break;
                case AvUiCue.Confirm:  seconds = .085f; frequency = 420f; glide = -20f; decay = 36f; texture = .03f; break;
                case AvUiCue.Caution:  seconds = .090f; frequency = 250f; glide = -15f; decay = 34f; texture = .04f; break;
                default:               seconds = .060f; frequency = 320f; glide = -25f; decay = 46f; texture = .04f; break;
            }

            int length = Mathf.CeilToInt(seconds * SampleRate);
            var clip = AudioClip.Create("NOAvionics." + cue, length, 1, SampleRate, false);
            if (clip == null) return null;
            var data = new float[length];
            uint seed = 0x9e3779b9u + (uint)cue * 2654435761u;
            float filtered = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Min(1f, t / .005f);
                float tail = Mathf.Min(1f, (seconds - t) / .014f);
                float envelope = attack * Mathf.Exp(-decay * t) * Mathf.Max(0f, tail);
                float phase = 2f * Mathf.PI * (frequency * t + .5f * glide * t * t);
                float body = Mathf.Sin(phase) + .07f * Mathf.Sin(phase * 2f);
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                float noise = ((seed & 0xffffu) / 32767.5f) - 1f;
                filtered += (noise - filtered) * .16f;
                float contact = filtered * texture * Mathf.Exp(-t * 75f);
                data[i] = Mathf.Clamp((body * .52f + contact) * envelope, -.85f, .85f);
            }
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>Feedback for native bezel buttons borrowed by the map rail.</summary>
    public sealed class AvClickSound : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
    {
        private Button button;

        private void Awake() => button = GetComponent<Button>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button == null || button.interactable) AvUiSound.Play(AvUiCue.Hover);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left ||
                (button != null && !button.interactable)) return;
            AvUiSound.Play(AvUiCue.Navigate);
        }
    }
}
