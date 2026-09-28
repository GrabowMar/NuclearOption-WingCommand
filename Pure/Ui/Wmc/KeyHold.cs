namespace WingCommand
{
    /// <summary>One hold on a game key shared by its owners (critic §14.6: the text field and an armed map order both hold the pause
    /// key; two save-and-restore holders left it stuck). Held while any owner holds, and — unless released now — through the frame the
    /// last one let go, so the Esc that ended the edit or disarmed the order never pauses the game.</summary>
    internal sealed class KeyHold
    {
        public const int Field = 0, Map = 1;

        private int owners, releaseAt = -1;

        public void Set(int owner, bool on, int frame, bool now = false)
        {
            int bit = 1 << owner;
            if (on)
            {
                owners |= bit;
                releaseAt = -1;
                return;
            }
            if ((owners & bit) == 0) return;
            owners &= ~bit;
            if (owners == 0) releaseAt = now ? frame : frame + 1;
        }

        /// <summary>Every holder and any pending release, gone now.</summary>
        public void Clear()
        {
            owners = 0;
            releaseAt = -1;
        }

        public bool Held(int frame) => owners != 0 || (releaseAt >= 0 && frame < releaseAt);
    }
}
