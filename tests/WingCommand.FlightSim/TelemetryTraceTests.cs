using System;
using System.IO;
using Xunit;

namespace WingCommand.FlightSim
{
    public class TelemetryTraceTests
    {
        [Fact]
        public void SimTracesUseTheInGameTelemetrySchema()
        {
            var leader = new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), 40f, 3);
            var ring = new TelemetryRing();
            for (int tick = 0; tick < 5 * 60; tick++)
            {
                wing.Step();
                if (tick % 3 == 0)
                    for (int i = 0; i < 3; i++) ring.Push(wing.Row(i));
            }
            string csv = TelemetryCsv.Write(ring);
            Assert.StartsWith(TelemetryCsv.Header, csv);
            Assert.Equal(300, ring.Count);
            string dir = Environment.GetEnvironmentVariable("WC_TRACE_DIR");
            if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "sim-level-finger-four.csv"), csv);
        }
    }
}
