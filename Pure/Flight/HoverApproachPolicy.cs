namespace WingCommand
{
    /// <summary>Shared landing and cargo gate: arrest travel and centre over the point before descent.</summary>
    internal static class HoverApproachPolicy
    {
        public static bool Settled(float speed, float horizontalError) =>
            speed < 6f && horizontalError < 30f;
    }
}
