using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Spec M7 §1.4: the wing's radio. New wing events become calls in the speaker's persona, go through the
    /// <see cref="RadioQueue"/>, and come out as a subtitle in the game's message feed and, with voice on, through the
    /// game's text-to-speech: one voice for the wing, each line purging the one before (a real cut for an emergency), and
    /// the queue held while it still speaks (review M7a I3).</summary>
    internal sealed class RadioDirector : IWingService
    {
        public static RadioDirector Instance { get; private set; }

        public string Name => "Radio";
        public RadioQueue Queue { get; private set; } = new RadioQueue();

        private EventCursor cursor;
        private WingEventRing ring;
        private int seed, asks;
        private bool voiceBroken;
        private WindowsTTS voice;

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
                string name = call.Name == "WINCHESTER" ? RadioCalls.WinchesterLine(Plugin.Settings.AfterWinchester.Value) : call.Name;
                Say(speaker, call.Class, name, detail, call.WingWide, now);
            }
            while (Queue.Queued > 0 && Queue.Next(now, out RadioLine line, Speaking())) Transmit(line);
        }

        /// <summary>A call in <paramref name="speaker"/>'s persona (<paramref name="name"/>: a chatter line, see
        /// <see cref="ChatterDialogue.Event"/>).</summary>
        public void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide) =>
            Say(speaker, cls, name, detail, wingWide, Time.time);

        /// <summary>Exact words from <paramref name="speaker"/>. False when the radio will not say it (off, or dropped).</summary>
        public bool SayText(WingMember speaker, RadioClass cls, string key, string text, bool wingWide) =>
            Enqueue(speaker, cls, key, text, wingWide, Time.time);

        /// <summary>The answer to a player's question: never a repeat of an earlier answer (review M7a I4). False when the
        /// radio will not say it.</summary>
        public bool Answer(WingMember speaker, string key, string text) =>
            Enqueue(speaker, RadioClass.Tactical, key + ":" + ++asks, text, false, Time.time);

        private void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide, float now)
        {
            WingPilot pilot = WingPilotRoster.Of(speaker);
            ChatterPersona persona = pilot != null ? pilot.Persona : ChatterPersona.Professional;
            Enqueue(speaker, cls, name, ChatterDialogue.Event(persona, name, detail, seed++), wingWide, now);
        }

        private bool Enqueue(WingMember speaker, RadioClass cls, string key, string text, bool wingWide, float now)
        {
            RadioLevel level = Plugin.Settings.Radio.Value;
            if (level == RadioLevel.Off || (level == RadioLevel.Essential && cls == RadioClass.Chatter)) return false;
            WingPilot pilot = WingPilotRoster.Of(speaker);
            string who = pilot != null && !string.IsNullOrEmpty(pilot.Callsign) ? pilot.Callsign : "#" + speaker.Number;
            return Queue.Enqueue(new RadioLine { Speaker = speaker.Brain.Slot, Class = cls, Key = key, WingWide = wingWide, Text = who + ": " + text }, now);
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
            try
            {
                // The game's voice speaks asynchronously and purges what it was saying (SVSFlagsAsync | SVSFPurgeBeforeSpeak).
                if (voice == null) voice = new WindowsTTS();
                voice.Speak(PlayerSettings.chatTtsSpeed, PlayerSettings.chatTtsVolume, line.Text.Replace(": ", ", "), false);
            }
            catch (Exception e)
            {
                VoiceFailed(e);
            }
        }

        /// <summary>The voice is still speaking the last line (the queue waits for it, but an emergency cuts in).</summary>
        private bool Speaking()
        {
            if (voice == null || voiceBroken) return false;
            try
            {
                return voice.IsPlaying();
            }
            catch (Exception e)
            {
                VoiceFailed(e);
                return false;
            }
        }

        private void VoiceFailed(Exception e)
        {
            if (voiceBroken) return;
            voiceBroken = true;
            voice = null;
            Plugin.Logger.LogWarning($"[Radio] text-to-speech failed; voice is off until restart: {e.Message}");
        }

        private bool VoiceOn()
        {
            if (voiceBroken) return false;
            RadioVoice v = Plugin.Settings.RadioVoiceMode.Value;
            return v == RadioVoice.On || (v == RadioVoice.FollowGame && PlayerSettings.chatTts);
        }
    }
}
