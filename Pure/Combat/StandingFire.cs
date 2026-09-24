namespace WingCommand
{
    /// <summary>What a member holding formation may shoot at (spec M5 §8.2).</summary>
    internal enum StandingMode : byte { None, Cover, Opportunity }

    /// <summary>Spec M5 §8: standing fire from the slot under the wing's doctrine. Hold fires at nothing, Cover at aircraft
    /// threatening the leader, Air/Ground/Both at targets of opportunity of those classes; only shots whose launch
    /// envelope the member meets from where it flies (range, the target's altitude band with the game's distance-scaled
    /// floor, the station's off-boresight limit).</summary>
    internal static class StandingFire
    {
        public static float CheckSeconds = 0.5f, FireSeconds = 4f;

        public static StandingMode Decide(TargetPolicy targets, out DoctrineAllow allow)
        {
            allow = WingDoctrineRules.StandingAllow(targets);
            if (targets == TargetPolicy.Cover) return StandingMode.Cover;
            return allow == DoctrineAllow.None ? StandingMode.None : StandingMode.Opportunity;
        }

        public static bool Allows(DoctrineAllow allow, bool air) =>
            allow == DoctrineAllow.AirAndGround || (air ? allow == DoctrineAllow.AirOnly : allow == DoctrineAllow.GroundOnly);

        /// <summary>The game's own envelope (as <c>CombatAI.AnalyzeTarget</c> and 0.9's shot check): a 0 max range or
        /// off-boresight limit means none.</summary>
        public static bool InEnvelope(float range, float minRange, float maxRange, float targetAlt, float minAlt, float maxAlt,
            float offBoresightDeg, float maxOffBoresightDeg)
        {
            if (maxRange > 0f && range > maxRange) return false;
            if (range < minRange) return false;
            float floor = maxRange > 0f ? minAlt * range / maxRange : minAlt;
            if (targetAlt < floor || targetAlt > maxAlt) return false;
            return maxOffBoresightDeg <= 0f || offBoresightDeg <= maxOffBoresightDeg;
        }
    }

    /// <summary>A member's standing-fire clock: a check every <see cref="StandingFire.CheckSeconds"/>, a shot at most
    /// every <see cref="StandingFire.FireSeconds"/>.</summary>
    internal sealed class FireCadence
    {
        private float check, sinceShot = float.MaxValue;

        /// <summary>True when a check is due and a shot is allowed.</summary>
        public bool Due(float dt)
        {
            check += dt;
            if (sinceShot < float.MaxValue) sinceShot += dt;
            if (check < StandingFire.CheckSeconds - 1e-4f) return false;
            check = 0f;
            return sinceShot >= StandingFire.FireSeconds - 1e-4f;
        }

        public void Fired() => sinceShot = 0f;
    }
}
