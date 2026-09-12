namespace WingCommand
{
    internal static class MissileDefencePolicy
    {
        /// <summary>Saturation only yields inside the existing defensive reaction window, or to a
        /// close missile whose velocity may still be changing rapidly after launch.</summary>
        public static bool InterruptsSaturation(float distance, float closingSpeed) =>
            distance <= 1000f || (closingSpeed > 0f && distance / closingSpeed <= WingTuning.ChaffWindowSeconds);

        public static bool PreferInterception(WingRoe roe, string seeker, bool armed,
            float impactSeconds, bool hasCover, float playerDistance, float leashRadius) =>
            roe == WingRoe.Hold && seeker == "SARH" && armed && impactSeconds > 3f &&
            !(hasCover && playerDistance >= 0f && leashRadius > 0f && playerDistance <= leashRadius);
    }
}
