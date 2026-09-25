using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One <see cref="AirframeProfile"/> per unit name, built from the aircraft's native numbers and the
    /// data layers on first use. Rejected override keys are logged once per unit.</summary>
    internal static class WingProfiles
    {
        private static readonly Dictionary<string, AirframeProfile> cache = new Dictionary<string, AirframeProfile>();

        public static AirframeProfile For(Aircraft a)
        {
            ProfileInputs inputs;
            try
            {
                inputs = ProfileReader.Read(a);
            }
            catch (System.Exception e)
            {
                // Spec §8: a failed derivation flies the generic profile of the aircraft's class (a helicopter must
                // never get the fixed-wing stack).
                Plugin.Logger.LogWarning("[Profile] could not read the airframe's numbers, using the generic profile: " + e.Message);
                return AirframeProfile.Derive(new ProfileInputs { Class = SafeClassOf(a) });
            }
            string key = inputs.UnitName ?? "generic";
            if (cache.TryGetValue(key, out AirframeProfile p)) return p;
            var rejected = new List<string>();
            p = WingData.Profiles.Build(inputs, rejected);
            foreach (string r in rejected) Plugin.Logger.LogWarning("[Data] airframe override ignored: " + r);
            cache[key] = p;
            Plugin.LogVerbose($"[Profile] {key}: stall {p.StallSpeed:0} m/s, corner {p.CornerSpeed:0}, max {p.MaxSpeed:0}, " +
                $"g {p.GLimit:0.0}, roll {p.RollRateMaxDps:0} deg/s");
            return p;
        }

        public static void Clear() => cache.Clear();

        private static AirframeClass SafeClassOf(Aircraft a)
        {
            try
            {
                return ProfileReader.ClassOf(a);
            }
            catch (System.Exception)
            {
                return AirframeClass.FixedWing;
            }
        }
    }
}
