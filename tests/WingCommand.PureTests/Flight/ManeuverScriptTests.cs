using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace WingCommand.PureTests
{
    public class ManeuverScriptTests
    {
        private static ManeuverScripts Defaults() => JsonSerializer.Deserialize<ManeuverScripts>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maneuvers.json")),
            new JsonSerializerOptions { IncludeFields = true });

        [Fact]
        public void PresetsCoverEveryAerobaticOrderAndFinishUpright()
        {
            var scripts = Defaults();
            Assert.True(scripts.IsValid());
            foreach (ManeuverKind kind in Enum.GetValues(typeof(ManeuverKind)))
                if (!ManeuverCatalog.RotaryCapable(kind)) Assert.NotNull(scripts.Find(kind));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(1.01f)]
        [InlineData(-0.01f)]
        public void UnsafeThrottleInvalidatesTheWholeOverride(float throttle)
        {
            var scripts = Defaults();
            scripts.maneuvers[0].phases[0].throttle = throttle;
            Assert.False(scripts.IsValid());
        }

        [Fact]
        public void DuplicateOrMissingRecipesAreRejected()
        {
            var scripts = Defaults();
            scripts.maneuvers[1].kind = scripts.maneuvers[0].kind;
            Assert.False(scripts.IsValid());
            scripts = Defaults();
            scripts.maneuvers[0].phases = null;
            Assert.False(scripts.IsValid());
        }

        [Fact]
        public void PitchArcWaitsForActualRotationRegardlessOfTime()
        {
            var phase = Defaults().Find(ManeuverKind.Loop).phases[0];
            Assert.False(phase.Complete(179, 360, 100, 0, 0, 0));
            Assert.True(phase.Complete(180, 0, 1, 0, 0, 1));
        }

        [Fact]
        public void RecoveryRequiresLevelBankSettledRollAndLevelNose()
        {
            var phases = Defaults().Find(ManeuverKind.SplitS).phases;
            var phase = phases[phases.Length - 1];
            Assert.False(phase.Complete(0, 0, 10, 180, 0, 0));
            Assert.False(phase.Complete(0, 0, 10, 0, 1, 0));
            Assert.False(phase.Complete(0, 0, 10, 0, 0, -0.8f));
            Assert.True(phase.Complete(0, 0, 10, 2, 0.1f, 0.05f));
            phase.bankTarget = 180;
            var scripts = Defaults();
            scripts.Find(ManeuverKind.SplitS).phases = phases;
            Assert.False(scripts.IsValid());
        }
    }
}
