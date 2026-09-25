namespace WingCommand
{
    /// <summary>How the game's combat state tries to leave: to its landing state (no target for 15 s, or low fuel), to
    /// transport (a helicopter with cargo).</summary>
    internal enum NativeExit : byte { None, Landing, Transport }

    /// <summary>An engaged member as the supervisor sees it.</summary>
    internal struct CombatSituation
    {
        /// <summary>Distance to the wing's anchor (the player, escortee or task lead), m.</summary>
        public float AnchorDistance;
        /// <summary>Munitions aboard as a fraction of a full load (1 with no weapon stations).</summary>
        public float Ammo;
        public bool Bingo, AnchorPresent;
        public NativeExit Exit;
        /// <summary>How long the game's combat state has had no target.</summary>
        public float NoTargetSeconds;
    }

    /// <summary>Spec M5 §2.2: an engaged member stays in the game's combat state until it would leave it (no target, low
    /// fuel, cargo), has had no target for <see cref="NoTargetSeconds"/> (review M5a I2: it would fly to mission
    /// objectives), is at bingo fuel, has no ammunition left, or is more than <see cref="LeashMetres"/> from the wing's
    /// anchor; then it is taken back into formation with the reason.</summary>
    internal static class CombatSupervisor
    {
        public static float LeashMetres = 20000f, NoTargetSeconds = 15f;

        public static bool TakeBack(in CombatSituation s, out TransitionReason reason)
        {
            reason = s.Bingo ? TransitionReason.Fuel
                : s.Exit != NativeExit.None || s.NoTargetSeconds > NoTargetSeconds ? TransitionReason.NoTarget
                : s.Ammo <= 0f ? TransitionReason.Winchester
                : s.AnchorPresent && s.AnchorDistance > LeashMetres ? TransitionReason.Leash
                : TransitionReason.None;
            return reason != TransitionReason.None;
        }
    }
}
