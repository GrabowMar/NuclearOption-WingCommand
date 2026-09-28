namespace WingCommand
{
    /// <summary>Why a member left the wing (spec WMC rebuild R3; the LOST alert, the log and the radio): released or home
    /// first (a returned aircraft is disabled but safe), then an ejection, then a kill; nothing known is "gone".</summary>
    internal static class MemberLoss
    {
        public static TransitionReason Of(bool released, bool home, bool disabled, bool dead, bool ejected) =>
            released || home ? TransitionReason.Released
            : ejected ? TransitionReason.Ejected
            : dead || disabled ? TransitionReason.Killed
            : TransitionReason.Gone;

        /// <summary>A loss the player is warned about (a release is not).</summary>
        public static bool IsLoss(TransitionReason r) =>
            r == TransitionReason.Killed || r == TransitionReason.Ejected || r == TransitionReason.Gone;
    }
}
