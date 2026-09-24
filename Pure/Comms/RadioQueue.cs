using System;

namespace WingCommand
{
    /// <summary>Radio priority, highest last (spec M7 §1.2).</summary>
    internal enum RadioClass : byte { Chatter, Status, Tactical, Emergency }

    /// <summary>One line waiting for the channel.</summary>
    internal struct RadioLine
    {
        public int Speaker;
        public RadioClass Class;
        /// <summary>Dedupe key, scoped to the speaker unless <see cref="WingWide"/>.</summary>
        public string Key;
        public bool WingWide;
        public string Text;
        public float Queued;
        /// <summary>Seconds it waited behind a line of its class or higher (not counted toward its age), and when that was
        /// last measured.</summary>
        public float Blocked, Seen;
    }

    /// <summary>Spec M7 §1.2: the wing's one radio channel.
    /// <list type="bullet">
    /// <item>Higher classes first, FIFO within one; a line holds the channel for its <see cref="Airtime"/>.</item>
    /// <item>An Emergency goes on at once, cutting anything but another Emergency, and ignores the speaker gap.</item>
    /// <item>Outside Emergency a speaker waits <see cref="SpeakerGap"/> after their last line ended; while the highest
    /// class's lines wait on it, lower classes wait too (no starving it until it goes stale).</item>
    /// <item>A line waiting longer than its class's age is dropped — time spent behind a line of its own class or higher
    /// does not count (review M7a I2: a second emergency behind a 3 s one went stale before it was heard); the same key
    /// queued is replaced in place; a key sent within <see cref="RepeatSeconds"/> is dropped.</item>
    /// <item>A held channel (the voice still speaking: review M7a I3) is busy for everything but an Emergency.</item>
    /// <item>Full: a new line evicts the oldest line of the lowest class below its own, else it is dropped.</item>
    /// </list>
    /// Fixed arrays: enqueue and next allocate nothing.</summary>
    internal sealed class RadioQueue
    {
        public static float SpeakerGap = 1.5f, RepeatSeconds = 8f, AirBase = 0.8f, AirPerChar = 0.055f, AirMin = 1.2f, AirMax = 6f;
        public static float EmergencyAge = 2f, TacticalAge = 4f, StatusAge = 10f, ChatterAge = 20f;
        public const int Capacity = 16, MaxSpeakers = 16, RecentCapacity = 32;

        private struct Recent
        {
            public string Key;
            public int Speaker;
            public bool WingWide;
            public float At;
        }

        private readonly RadioLine[] lines = new RadioLine[Capacity];
        private readonly float[] lastEnd = new float[MaxSpeakers];
        private readonly Recent[] recent = new Recent[RecentCapacity];
        private readonly int[] sent = new int[4];
        private int count, recentNext;
        private float busyFrom = float.NegativeInfinity, busyUntil = float.NegativeInfinity;
        private RadioClass currentClass;

        public RadioQueue()
        {
            for (int i = 0; i < MaxSpeakers; i++) lastEnd[i] = float.NegativeInfinity;
            for (int i = 0; i < RecentCapacity; i++) recent[i].At = float.NegativeInfinity;
        }

        public int Queued => count;
        public int DroppedStale { get; private set; }
        public int DroppedRepeat { get; private set; }
        public int DroppedFull { get; private set; }
        /// <summary>The shortest wait of a speaker between their lines outside Emergency (+inf until one).</summary>
        public float MinSpeakerGap { get; private set; } = float.PositiveInfinity;

        public int SentOf(RadioClass c) => sent[(int)c];
        public bool Busy(float now) => now < busyUntil;

        public static float Airtime(string text)
        {
            float t = AirBase + AirPerChar * (text?.Length ?? 0);
            return t < AirMin ? AirMin : t > AirMax ? AirMax : t;
        }

        private static float Age(RadioClass c) =>
            c == RadioClass.Emergency ? EmergencyAge : c == RadioClass.Tactical ? TacticalAge : c == RadioClass.Status ? StatusAge : ChatterAge;

        private static int SpeakerSlot(int speaker) => speaker >= 0 && speaker < MaxSpeakers ? speaker : MaxSpeakers - 1;

        private static bool SameScope(string key, int speaker, bool wing, string otherKey, int otherSpeaker, bool otherWing) =>
            string.Equals(key, otherKey, StringComparison.Ordinal) &&
            (wing && otherWing || !wing && !otherWing && SpeakerSlot(speaker) == SpeakerSlot(otherSpeaker));

        /// <summary>False when the line was dropped (sent recently, or the queue is full of lines at least as urgent).</summary>
        public bool Enqueue(in RadioLine line, float now)
        {
            for (int i = 0; i < RecentCapacity; i++)
            {
                Recent r = recent[i];
                if (r.Key != null && now - r.At < RepeatSeconds && SameScope(line.Key, line.Speaker, line.WingWide, r.Key, r.Speaker, r.WingWide))
                {
                    DroppedRepeat++;
                    return false;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (!SameScope(line.Key, line.Speaker, line.WingWide, lines[i].Key, lines[i].Speaker, lines[i].WingWide)) continue;
                lines[i] = line;
                Stamp(ref lines[i], now);
                return true;
            }
            if (count == Capacity)
            {
                int victim = -1;
                for (int i = 0; i < count; i++)
                    if (victim < 0 || lines[i].Class < lines[victim].Class) victim = i;   // the first found is the oldest
                if (victim < 0 || lines[victim].Class >= line.Class)
                {
                    DroppedFull++;
                    return false;
                }
                RemoveAt(victim);
            }
            lines[count] = line;
            Stamp(ref lines[count], now);
            count++;
            return true;
        }

        private static void Stamp(ref RadioLine l, float now)
        {
            l.Queued = l.Seen = now;
            l.Blocked = 0f;
        }

        /// <summary>True when <paramref name="line"/> goes on the channel now. <paramref name="held"/>: the channel is still
        /// in use (the voice has not finished the last line).</summary>
        public bool Next(float now, out RadioLine line, bool held = false)
        {
            line = default;
            float until = held ? Math.Max(busyUntil, now) : busyUntil;
            for (int i = count - 1; i >= 0; i--)
            {
                if (currentClass >= lines[i].Class)
                {
                    float from = Math.Max(lines[i].Seen, busyFrom), to = Math.Min(now, until);
                    if (to > from) lines[i].Blocked += to - from;
                }
                lines[i].Seen = now;
                if (now - lines[i].Queued - lines[i].Blocked <= Age(lines[i].Class)) continue;
                RemoveAt(i);
                DroppedStale++;
            }
            if (count == 0) return false;
            RadioClass top = RadioClass.Chatter;
            for (int i = 0; i < count; i++)
                if (lines[i].Class > top) top = lines[i].Class;
            int pick = -1;
            for (int i = 0; i < count && pick < 0; i++)
            {
                if (lines[i].Class != top) continue;
                if (top == RadioClass.Emergency || now - lastEnd[SpeakerSlot(lines[i].Speaker)] >= SpeakerGap - 1e-4f) pick = i;
            }
            if (pick < 0) return false;
            bool emergency = top == RadioClass.Emergency;
            if ((now < busyUntil || held) && (!emergency || currentClass == RadioClass.Emergency)) return false;
            line = lines[pick];
            RemoveAt(pick);
            int slot = SpeakerSlot(line.Speaker);
            if (!emergency && !float.IsNegativeInfinity(lastEnd[slot]))
                MinSpeakerGap = Math.Min(MinSpeakerGap, now - lastEnd[slot]);
            float air = Airtime(line.Text);
            busyFrom = now;
            busyUntil = now + air;
            currentClass = line.Class;
            lastEnd[slot] = now + air;
            recent[recentNext] = new Recent { Key = line.Key, Speaker = line.Speaker, WingWide = line.WingWide, At = now };
            recentNext = (recentNext + 1) % RecentCapacity;
            sent[(int)line.Class]++;
            return true;
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < count - 1; i++) lines[i] = lines[i + 1];
            count--;
            lines[count] = default;
        }
    }
}
