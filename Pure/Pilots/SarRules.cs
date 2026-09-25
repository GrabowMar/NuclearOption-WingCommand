using System;

namespace WingCommand
{
    /// <summary>What search and rescue costs (R6 ruling): a local search costs half the lost airframe's value and a rescue earns the same
    /// bounty, with a 10 CR floor for an airframe of unknown value; the sandbox searches for free. The game's values are millions, read as
    /// credits (the old flat 10,000,000 was never affordable).</summary>
    internal static class SarRules
    {
        public const float Floor = 10f, Share = 0.5f;

        public static float LocalCost(float value, bool sandbox) => sandbox ? 0f : Bounty(value);

        public static float Bounty(float value) => (float)Math.Round(Math.Max(value, Floor) * Share, MidpointRounding.AwayFromZero);
    }
}
