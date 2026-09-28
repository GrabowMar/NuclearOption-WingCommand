using UnityEngine;

namespace WingCommand
{
    /// <summary>The game's pause key, held off while a WMC text field has the keyboard or a map order is armed (spec bezel v2 §6: Esc
    /// cancels those instead of pausing the game), through one <see cref="KeyHold"/> so the two never restore it under each other.
    /// The value the player had is put back when the last holder lets go; while held it is asserted every frame (the game turns it
    /// back on when a menu closes).</summary>
    internal static class PauseKeyHold
    {
        private static readonly KeyHold hold = new KeyHold();
        private static bool applied, was;

        public static void Set(int owner, bool on, bool now = false)
        {
            hold.Set(owner, on, Time.frameCount, now);
            Apply();
        }

        /// <summary>The panel went away (a mission ended, a reset): every holder lets go now, so the pause key is never left off.</summary>
        public static void ReleaseAll()
        {
            hold.Clear();
            Apply();
        }

        /// <summary>Every frame (WmcPanel.Tick).</summary>
        public static void Tick() => Apply();

        private static void Apply()
        {
            if (hold.Held(Time.frameCount))
            {
                if (!applied)
                {
                    was = GameplayUI.AllowPauseKeybind;
                    applied = true;
                }
                GameplayUI.AllowPauseKeybind = false;
                return;
            }
            if (!applied) return;
            applied = false;
            GameplayUI.AllowPauseKeybind = was;
        }
    }
}
