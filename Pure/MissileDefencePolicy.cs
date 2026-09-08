namespace WingCommand
{
    internal static class MissileDefencePolicy
    {
        public static bool PreferInterception(WingRoe roe, string seeker, bool armed,
            float impactSeconds, bool hasCover, float playerDistance, float leashRadius) =>
            roe == WingRoe.Hold && seeker == "SARH" && armed && impactSeconds > 3f &&
            !(hasCover && playerDistance >= 0f && leashRadius > 0f && playerDistance <= leashRadius);
    }
}
