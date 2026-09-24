using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace WingCommand
{
    /// <summary>Spec M7 §5: Yappinator-format voice packs for wingman calls. The packs named in Radio/VoicePacks are found
    /// under Wing Command's voicepacks folder or Yappinator's own audio folder, their clips loaded without blocking
    /// (polled each frame), dealt one pack per wingman round robin, and played on one radio audio source (a new line
    /// stops the last: an emergency cuts in as the TTS does).</summary>
    internal static class VoicePacks
    {
        private sealed class Pack
        {
            public string Name;
            public readonly VoicePackIndex Index = new VoicePackIndex();
            public readonly List<string> Files = new List<string>();
            public AudioClip[] Clips;
            public int Next;
        }

        private struct Loading
        {
            public Pack Pack;
            public int File;
            public UnityWebRequest Request;
        }

        private static readonly List<Pack> packs = new List<Pack>();
        private static readonly List<Loading> loading = new List<Loading>();
        private static AudioSource source;
        private static string loadedSetting;
        private static bool failed;

        public static bool Playing => source != null && source.isPlaying;

        /// <summary>Silence the pack voice (the TTS is about to speak).</summary>
        public static void Stop()
        {
            if (source != null && source.isPlaying) source.Stop();
        }

        private static IEnumerable<string> Roots()
        {
            yield return Path.Combine(WingConfig.DataRoot, "voicepacks");
            yield return Path.Combine(Paths.PluginPath, "WSOYappinator", "audio");
        }

        /// <summary>At each mission start: (re)load the packs named in the setting when it changed.</summary>
        public static void Activate()
        {
            string setting = Plugin.Settings.VoicePacks.Value ?? "";
            if (setting == loadedSetting) return;
            Clear();
            foreach (string raw in setting.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                // One bad pack never takes the radio down with it (review M7d I3).
                try
                {
                    Load(name);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"[Radio] voice pack \"{name}\" could not be read: {e.Message}");
                }
            }
            loadedSetting = setting;
        }

        private static void Load(string name)
        {
            string folder = Find(name);
            if (folder == null)
            {
                Plugin.Logger.LogWarning($"[Radio] voice pack \"{name}\" not found under {string.Join(" or ", Roots())}");
                return;
            }
            var pack = new Pack { Name = name };
            foreach (string file in Directory.GetFiles(folder))
            {
                AudioType type = TypeOf(file);
                if (type == AudioType.UNKNOWN) continue;
                int index = pack.Files.Count;
                pack.Files.Add(file);
                int before = pack.Index.Count;
                pack.Index.Add(index, Path.GetFileNameWithoutExtension(file));
                if (pack.Index.Count == before) continue;
                // The raw path, as Yappinator: Unity turns a Windows path into a file URI itself ('#' and '+' survive).
                UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(file, type);
                req.SendWebRequest();
                loading.Add(new Loading { Pack = pack, File = index, Request = req });
            }
            pack.Clips = new AudioClip[pack.Files.Count];
            packs.Add(pack);
            Plugin.Logger.LogInfo($"[Radio] voice pack \"{name}\": {pack.Index.Count} clips for wingman calls");
        }

        private static string Find(string name)
        {
            foreach (string root in Roots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                    if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(Path.Combine(dir, "eventPriorities.txt"))) return dir;
            }
            return null;
        }

        private static AudioType TypeOf(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }

        private static void Clear()
        {
            foreach (Loading l in loading) l.Request.Dispose();
            loading.Clear();
            foreach (Pack p in packs)
                if (p.Clips != null)
                    foreach (AudioClip c in p.Clips)
                        if (c != null) UnityEngine.Object.Destroy(c);
            packs.Clear();
        }

        /// <summary>Every frame: finish the loads that are done.</summary>
        public static void Tick()
        {
            for (int i = loading.Count - 1; i >= 0; i--)
            {
                Loading l = loading[i];
                if (!l.Request.isDone) continue;
                loading.RemoveAt(i);
                try
                {
                    AudioClip clip = l.Request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(l.Request) : null;
                    // Only a decoded clip plays; anything else falls back to the TTS (review M7d I4).
                    if (clip != null && clip.loadState == AudioDataLoadState.Loaded) l.Pack.Clips[l.File] = clip;
                    else
                    {
                        if (clip != null) UnityEngine.Object.Destroy(clip);
                        Plugin.Logger.LogWarning($"[Radio] {l.Pack.Files[l.File]}: {(l.Request.error ?? "could not be decoded")}");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"[Radio] {l.Pack.Files[l.File]}: {e.Message}");
                }
                finally
                {
                    l.Request.Dispose();
                }
            }
        }

        /// <summary>Plays wingman #<paramref name="number"/>'s pack clip for <paramref name="call"/>. False when there is
        /// none (the caller speaks it with the TTS).</summary>
        public static bool TryPlay(int number, string call)
        {
            if (failed || packs.Count == 0) return false;
            Pack pack = packs[VoicePackIndex.PackFor(number, packs.Count)];
            foreach (string evt in VoicePackIndex.EventsFor(call))
            {
                IReadOnlyList<int> files = pack.Index.Clips(evt);
                for (int k = 0; k < files.Count; k++)
                {
                    AudioClip clip = pack.Clips[files[(pack.Next + k) % files.Count]];
                    if (clip == null) continue;
                    pack.Next++;
                    return Play(clip);
                }
            }
            return false;
        }

        private static bool Play(AudioClip clip)
        {
            try
            {
                if (source == null)
                {
                    var go = new GameObject("WingCommandRadioVoice") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    source = go.AddComponent<AudioSource>();
                    source.spatialBlend = 0f;
                    source.playOnAwake = false;
                }
                source.Stop();
                source.volume = Mathf.Clamp01(Plugin.Settings.VoicePackVolume.Value);
                source.clip = clip;
                source.Play();
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Plugin.Logger.LogWarning($"[Radio] voice pack playback failed; packs are off until restart: {e.Message}");
                return false;
            }
        }
    }
}
