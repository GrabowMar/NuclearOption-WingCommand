namespace WingCommand
{
    /// <summary>The radio lines that went on air (spec M7b §3 LOG), oldest first; a fixed ring that overwrites the
    /// oldest line when full.</summary>
    internal sealed class RadioLog
    {
        public const int Capacity = 64;
        private readonly float[] times = new float[Capacity];
        private readonly string[] texts = new string[Capacity];
        private int next;

        public int Count { get; private set; }

        public void Push(float time, string text)
        {
            times[next] = time;
            texts[next] = text ?? "";
            next = (next + 1) % Capacity;
            if (Count < Capacity) Count++;
        }

        private int Index(int i) => (next - Count + i + Capacity) % Capacity;

        public float TimeAt(int i) => times[Index(i)];

        public string TextAt(int i) => texts[Index(i)];

        public void Clear()
        {
            Count = 0;
            next = 0;
        }
    }
}
