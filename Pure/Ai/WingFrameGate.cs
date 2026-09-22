namespace WingCommand
{
    /// <summary>Skips optional polish for a couple of frames after a hitch. Survival, formation
    /// physics, and HUD never consult this gate.</summary>
    internal static class WingFrameGate
    {
        public const float HitchSeconds = 0.0335f;
        public const int RecoverFrames = 2;

        private static int recoverLeft;

        public static bool Recovering => recoverLeft > 0;

        public static void Reset() => recoverLeft = 0;

        public static void NoteFrame(float unscaledDeltaSeconds)
        {
            if (unscaledDeltaSeconds >= HitchSeconds)
                recoverLeft = RecoverFrames;
            else if (recoverLeft > 0)
                recoverLeft--;
        }
    }
}
