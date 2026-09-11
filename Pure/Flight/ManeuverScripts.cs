using System;
using System.Collections.Generic;

// Unity populates JSON fields through reflection.
#pragma warning disable CS0649

namespace WingCommand
{
    [Serializable]
    internal sealed class ManeuverScripts
    {
        public ManeuverRecipe[] maneuvers;

        public bool IsValid()
        {
            if (maneuvers == null || maneuvers.Length != 5) return false;
            var seen = new HashSet<ManeuverKind>();
            foreach (var recipe in maneuvers)
            {
                if (recipe == null || !Enum.TryParse(recipe.kind, out ManeuverKind kind) ||
                    !Enum.IsDefined(typeof(ManeuverKind), kind) || recipe.kind != kind.ToString() || ManeuverCatalog.RotaryCapable(kind) ||
                    !seen.Add(kind) || recipe.phases == null || recipe.phases.Length == 0 ||
                    recipe.phases.Length > 8) return false;
                foreach (var phase in recipe.phases)
                    if (phase == null || !phase.IsValid()) return false;
                var last = recipe.phases[recipe.phases.Length - 1];
                if (last.until != "recover" || last.bankTarget != 0) return false;
            }
            return true;
        }

        public ManeuverRecipe Find(ManeuverKind kind) =>
            Array.Find(maneuvers, recipe => recipe.kind == kind.ToString());
    }

    [Serializable]
    internal sealed class ManeuverRecipe
    {
        public string kind;
        public ManeuverPhase[] phases;
    }

    [Serializable]
    internal sealed class ManeuverPhase
    {
        public float throttle, pitch, roll, bankTarget, bankGain, rollDamping;
        public float pitchLevelGain, minPitch, maxPitch, rollLimit;
        public string until;
        public float amount;

        public bool IsValid() =>
            In(throttle, 0, 1) && In(pitch, -1, 1) && In(roll, -1, 1) &&
            In(bankTarget, -180, 180) && In(bankGain, 0, 0.1f) && In(rollDamping, 0, 1) &&
            In(pitchLevelGain, 0, 2) && In(minPitch, -1, 1) && In(maxPitch, minPitch, 1) &&
            In(rollLimit, 0.01f, 1) &&
            ((until == "pitch" || until == "roll") ? In(amount, 1, 360) :
             until == "seconds" ? In(amount, 0.05f, 5) :
             (until == "bank" || until == "recover") && amount == 0);

        // Measure arc completion from integrated body rates, not elapsed time alone.
        public bool Complete(float pitchDegrees, float rollDegrees, float seconds,
                             float bankError, float rollRate, float noseY)
        {
            switch (until)
            {
                case "pitch": return pitchDegrees >= amount;
                case "roll": return rollDegrees >= amount;
                case "seconds": return seconds >= amount;
                case "bank": return Math.Abs(bankError) < 8 && Math.Abs(rollRate) < 0.25f;
                case "recover": return Math.Abs(bankError) < 8 && Math.Abs(rollRate) < 0.25f &&
                                       Math.Abs(noseY) < 0.2f;
                default: return false;
            }
        }

        private static bool In(float value, float min, float max) => value >= min && value <= max;
    }
}
#pragma warning restore CS0649
