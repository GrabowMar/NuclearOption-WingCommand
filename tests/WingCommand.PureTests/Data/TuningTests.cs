using System;
using System.Reflection;
using Xunit;

namespace WingCommand.PureTests
{
    internal static class TuningProbe
    {
        public static float Gain = 1f;
        public static int Count = 3;
        public static bool Enabled = true;
        public const float Fixed = 2f;
        public static readonly float Frozen = 4f;
    }

    public class TuningTests
    {
        private static readonly Type[] Probe = { typeof(TuningProbe) };

        [Fact]
        public void ApplySetsNamedStaticFieldsAndReportsTheRest()
        {
            float gain = TuningProbe.Gain;
            int count = TuningProbe.Count;
            bool enabled = TuningProbe.Enabled;
            try
            {
                var rejected = Tuning.Apply(
                    "{ \"TuningProbe.Gain\": 2.5, \"tuningprobe.count\": 7, \"TuningProbe.Enabled\": false, " +
                    "\"TuningProbe.Fixed\": 9, \"TuningProbe.Frozen\": 9, \"TuningProbe.Missing\": 1, " +
                    "\"Other.Gain\": 1, \"TuningProbe.Gain2\": \"x\" }", Probe);
                Assert.Equal(2.5f, TuningProbe.Gain);
                Assert.Equal(7, TuningProbe.Count);
                Assert.False(TuningProbe.Enabled);
                Assert.Equal(new[] { "TuningProbe.Fixed", "TuningProbe.Frozen", "TuningProbe.Missing", "Other.Gain", "TuningProbe.Gain2" },
                    rejected.ToArray());
            }
            finally
            {
                TuningProbe.Gain = gain;
                TuningProbe.Count = count;
                TuningProbe.Enabled = enabled;
            }
        }

        [Fact]
        public void NonObjectJsonIsRejectedWhole() =>
            Assert.Equal(new[] { "(not a JSON object)" }, Tuning.Apply("[1, 2]", Probe).ToArray());

        [Fact]
        public void ExportListsEveryTunableFieldAndAppliesBackUnchanged()
        {
            string json = Tuning.Export(Probe);
            Assert.Contains("\"TuningProbe.Gain\": 1", json);
            Assert.Contains("\"TuningProbe.Count\": 3", json);
            Assert.Contains("\"TuningProbe.Enabled\": true", json);
            Assert.DoesNotContain("Fixed", json);
            Assert.DoesNotContain("Frozen", json);
            Assert.Empty(Tuning.Apply(json, Probe));
        }

        [Fact]
        public void EveryTunedTypeExposesItsParametersAsStaticFields()
        {
            foreach (Type t in Tuning.Types)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    Assert.False(f.IsLiteral && f.FieldType == typeof(float), $"{t.Name}.{f.Name} is const; make it static");
        }

        [Fact]
        public void ProductionExportRoundTrips() => Assert.Empty(Tuning.Apply(Tuning.Export(Tuning.Types), Tuning.Types));
    }
}
