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

        /// <summary>Spec M7 §2: new contacts are called once a second.</summary>
        public ContactWatch Contacts { get; } = new ContactWatch();
        public int ContactsCalled { get; private set; }
        private readonly ContactSample[] samples = new ContactSample[ContactWatch.Capacity];
        private readonly Unit[] units = new Unit[ContactWatch.Capacity];
        private readonly Vec3[] tracked = new Vec3[ContactWatch.Capacity];
        private readonly int[] report = new int[4];
        private static readonly int[] NoReport = new int[0];
        private float contactClock;
        // The one contact call waiting for the channel (review M7a-2 I1: the next is chosen only once it is on air).
        private string pendingKey;
        private uint pendingId;

        public RadioDirector()
        {
            Instance = this;
            WingPilotRoster.Killed += OnKill;
        }

        private void OnKill(Aircraft shooter, uint victimId, string victimType)
        {
            WingMember m = WingService.Instance?.MemberOf(shooter);
            if (m != null && !m.Released) SayKill(m, victimId, victimType);
        }

        public void Activate()
        {
            Queue = new RadioQueue();
            WingRadioAudio.Reset();
            VoicePacks.Activate();
            Contacts.Clear();
            ContactsCalled = 0;
            pendingKey = null;
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
            VoicePacks.Tick();
            while (cursor.Next(ring, out WingEvent e))
            {
                if (!RadioCalls.For(e, out RadioCall call)) continue;
                WingMember speaker = SpeakerOf(wing, e.Member);
                if (speaker == null) continue;
                string detail = call.Name == "JOKER" ? Math.Max(1, (int)Math.Ceiling(speaker.Bingo.SecondsToBingo / 60f)).ToString() : null;
                string name = call.Name == "WINCHESTER" ? RadioCalls.WinchesterLine(Plugin.Settings.AfterWinchester.Value) : call.Name;
                Say(speaker, call.Class, name, detail, call.WingWide, now);
            }
            if ((contactClock += dt) >= 1f)
            {
                contactClock = 0f;
                CallContacts(wing, now);
            }
            while (Queue.Queued > 0 && Queue.Next(now, out RadioLine line, Speaking())) Transmit(line);
        }

        /// <summary>Spec M7 §2.3: new air threats (and, scouting, ground units) called by the flying member nearest them, with
        /// BRA from the player.</summary>
        private void CallContacts(WingService wing, float now)
        {
            WingConfig cfg = Plugin.Settings;
            Contacts.Ground = wing.Planner.Active && wing.Planner.Current.Scout;
            if (!cfg.ContactCalls.Value && !Contacts.Ground) return;
            if (pendingKey != null && !Queue.Holds(pendingKey))
            {
                // Gone from the queue without going on air: dropped (stale or full); called again when it next qualifies.
                Contacts.Forget(pendingId);
                pendingKey = null;
            }
            int n = wing.KnownContacts(Contacts, cfg.ContactCalls.Value, samples, units, tracked);
            // While a call waits, the watch still tracks and forgets, but chooses nothing new.
            int k = Contacts.Update(samples, n, now, pendingKey != null ? NoReport : report);
            for (int i = 0; i < k; i++)
            {
                int at = report[i];
                ContactSample c = samples[at];
                Unit u = units[at];
                WingMember speaker = NearestFlying(wing, tracked[at]);
                if (speaker == null) continue;
                Vec3 from = wing.Player != null ? wing.Player.GlobalPosition().ToVec3() : speaker.Last.Pos;
                Vec3 vel = u.rb != null ? u.rb.velocity.ToVec3() : Vec3.Zero;
                string bra = Bra.Format(from, tracked[at], vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
                string type = u.definition != null ? u.definition.unitName : u.unitName;
                string key = (c.Air ? "BANDIT:" : "CONTACT:") + c.Id;
                bool queued = c.Air
                    ? SayText(speaker, RadioClass.Tactical, key, $"Bandit, {bra}. {type}.", true)
                    : SayText(speaker, RadioClass.Status, key, $"Contact, {bra}. {type}.", true);
                if (queued)
                {
                    pendingKey = key;
                    pendingId = c.Id;
                }
                else Contacts.Forget(c.Id);
            }
        }

        private static WingMember NearestFlying(WingService wing, Vec3 at)
        {
            WingMember best = null;
            float bestD = float.MaxValue;
            foreach (WingMember m in wing.Members)
            {
                if (m.Released || !m.Alive || m.OnGround) continue;
                float d = (m.Last.Pos - at).SqrLength;
                if (d >= bestD) continue;
                bestD = d;
                best = m;
            }
            return best;
        }

        /// <summary>A call in <paramref name="speaker"/>'s persona (<paramref name="name"/>: a chatter line, see
        /// <see cref="ChatterDialogue.Event"/>).</summary>
        public void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide) =>
            Say(speaker, cls, name, detail, wingWide, Time.time);

        /// <summary>Spec M7 §3: a kill, keyed by the victim so one kill is one call.</summary>
        public void SayKill(WingMember speaker, uint victim, string type)
        {
            WingPilot pilot = WingPilotRoster.Of(speaker);
            ChatterPersona persona = pilot != null ? pilot.Persona : ChatterPersona.Professional;
            Enqueue(speaker, RadioClass.Tactical, "SPLASH:" + victim, ChatterDialogue.Event(persona, "SPLASH", type, seed++), true, Time.time);
        }

        /// <summary>Exact words from <paramref name="speaker"/>. False when the radio will not say it (off, or dropped).</summary>
        public bool SayText(WingMember speaker, RadioClass cls, string key, string text, bool wingWide) =>
            Enqueue(speaker, cls, key, text, wingWide, Time.time);

        /// <summary>The answer to a player's question: never a repeat of an earlier answer (review M7a I4). False when the
        /// radio will not say it.</summary>
        public bool Answer(WingMember speaker, string key, string text) =>
            Enqueue(speaker, RadioClass.Tactical, key + ":" + ++asks, text, false, Time.time, answer: true);

        private void Say(WingMember speaker, RadioClass cls, string name, string detail, bool wingWide, float now)
        {
            WingPilot pilot = WingPilotRoster.Of(speaker);
            ChatterPersona persona = pilot != null ? pilot.Persona : ChatterPersona.Professional;
            Enqueue(speaker, cls, name, ChatterDialogue.Event(persona, name, detail, seed++), wingWide, now);
        }

        private bool Enqueue(WingMember speaker, RadioClass cls, string key, string text, bool wingWide, float now, bool answer = false)
        {
            RadioLevel level = Plugin.Settings.Radio.Value;
            if (level == RadioLevel.Off || (level == RadioLevel.Essential && cls == RadioClass.Chatter)) return false;
            WingPilot pilot = WingPilotRoster.Of(speaker);
            string who = pilot != null && !string.IsNullOrEmpty(pilot.Callsign) ? pilot.Callsign : "#" + speaker.Number;
            return Queue.Enqueue(new RadioLine
            {
                Speaker = speaker.Brain.Slot, Class = cls, Key = key, WingWide = wingWide, Text = who + ": " + text, Answer = answer,
                Voice = speaker.Voice,
            }, now);
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
            // Spec M5 §9.3: the game's radio static on every line, a threat warble on an emergency.
            WingRadioAudio.Play(line.Class == RadioClass.Emergency ? WingRadioAudio.Earcon.ThreatAlarm : WingRadioAudio.Earcon.Transmission);
            WingToast.Show(line.Text);
            if (line.Key == pendingKey)
            {
                // Contact calls count when they go on air (review M7a-2 m3).
                ContactsCalled++;
                pendingKey = null;
            }
            // A voice pack clip for this call (spec M7 §5) instead of the TTS; either way the other voice stops (review
            // M7d I2: an emergency cutting in must not talk over the line it cut).
            if (VoicePacks.TryPlay(line.Voice + 2, CallOf(line.Key)))
            {
                SilenceTts();
                return;
            }
            VoicePacks.Stop();
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
        private void SilenceTts()
        {
            if (voice == null || voiceBroken) return;
            try
            {
                // The game's Speak purges what it was saying before the (empty) new line.
                voice.Speak(PlayerSettings.chatTtsSpeed, PlayerSettings.chatTtsVolume, "", false);
            }
            catch (Exception e)
            {
                VoiceFailed(e);
            }
        }

        /// <summary>The call a line's key names ("SPLASH:1234" → "SPLASH").</summary>
        private static string CallOf(string key)
        {
            if (key == null) return null;
            int colon = key.IndexOf(':');
            return colon < 0 ? key : key.Substring(0, colon);
        }

        private bool Speaking()
        {
            if (VoicePacks.Playing) return true;
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
