using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Spec M7 §1.4: the wing's radio. New wing events become calls in the speaker's persona, go through the
    /// <see cref="RadioQueue"/>, and come out as a subtitle in the game's message feed and, with voice on, through the
    /// game's text-to-speech.</summary>
    internal sealed class RadioDirector : IWingService
    {
        public static RadioDirector Instance { get; private set; }

        public string Name => "Radio";
        public RadioQueue Queue { get; private set; } = new RadioQueue();

        private EventCursor cursor;
        private WingEventRing ring;
        private int seed;
        private bool voiceBroken;

        public RadioDirector() => Instance = this;

        public void Activate()
        {
            Queue = new RadioQueue();
            ring = WingService.Instance?.Events;
            cursor.Seen = ring?.Total ?? 0;
        }

        public void Deactivate() { }

        public void FixedTick(float dt) { }

        public void Tick(float dt)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return;
            if (!ReferenceEquals(ring, wing.Events))
            {
                ring = wing.Events;
                cursor.Seen = 0;
            }
            float now = Time.time;
            while (cursor.Next(ring, out WingEvent e))
            {
                if (!RadioCalls.For(e, out RadioCall call)) continue;
                WingMember speaker = SpeakerOf(wing, e.Member);
                if (speaker == null) continue;
                string detail = call.Name == "JOKER" ? Math.Max(1, (int)Math.Ceiling(speaker.Bingo.SecondsToBingo / 60f)).ToString() : null;
                Say(speaker, call.Class, call.Name, detail, call.WingWide, now);
            }
            while (Queue.Next(now, out RadioLine line)) Transmit(line);
        }

        /// <summary>A call in <paramref name="speaker"/>'s persona (<paramref name="name"/>: a chatter line, see
        /// <see cref="ChatterDialogue.Event"/>).</summary>
        public void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide) =>
            Say(speaker, cls, name, detail, wingWide, Time.time);

        /// <summary>Exact words from <paramref name="speaker"/> (a query's answer).</summary>
        public void SayText(WingMember speaker, RadioClass cls, string key, string text, bool wingWide) =>
            Enqueue(speaker, cls, key, text, wingWide, Time.time);

        private void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide, float now)
        {
            WingPilot pilot = WingPilotRoster.Of(speaker);
            ChatterPersona persona = pilot != null ? pilot.Persona : ChatterPersona.Professional;
            Enqueue(speaker, cls, name, ChatterDialogue.Event(persona, name, detail, seed++), wingWide, now);
        }

        private void Enqueue(WingMember speaker, RadioClass cls, string key, string text, bool wingWide, float now)
        {
            RadioLevel level = Plugin.Settings.Radio.Value;
            if (level == RadioLevel.Off || (level == RadioLevel.Essential && cls == RadioClass.Chatter)) return;
            WingPilot pilot = WingPilotRoster.Of(speaker);
            string who = pilot != null && !string.IsNullOrEmpty(pilot.Callsign) ? pilot.Callsign : "#" + speaker.Number;
            Queue.Enqueue(new RadioLine { Speaker = speaker.Brain.Slot, Class = cls, Key = key, WingWide = wingWide, Text = who + ": " + text }, now);
        }

        /// <summary>The member in the event's slot; for a wing-level event (or one whose member has gone), the first
        /// member.</summary>
        private static WingMember SpeakerOf(WingService wing, int slot)
        {
            WingMember first = null;
            foreach (WingMember m in wing.Members)
            {
                if (m.Released) continue;
                if (m.Brain.Slot == slot) return m;
                if (first == null) first = m;
            }
            return first;
        }

        private void Transmit(in RadioLine line)
        {
            WingToast.Show(line.Text);
            if (!VoiceOn()) return;
            Speak(line.Text.Replace(": ", ", ")).Forget();
        }

        private bool VoiceOn()
        {
            if (voiceBroken) return false;
            RadioVoice v = Plugin.Settings.RadioVoiceMode.Value;
            return v == RadioVoice.On || (v == RadioVoice.FollowGame && PlayerSettings.chatTts);
        }

        private async UniTaskVoid Speak(string text)
        {
            try
            {
                await WindowsTTS.SpeakAsync(PlayerSettings.chatTtsSpeed, PlayerSettings.chatTtsVolume, text, false);
            }
            catch (Exception e)
            {
                if (voiceBroken) return;
                voiceBroken = true;
                Plugin.Logger.LogWarning($"[Radio] text-to-speech failed; voice is off until restart: {e.Message}");
            }
        }
    }
}
