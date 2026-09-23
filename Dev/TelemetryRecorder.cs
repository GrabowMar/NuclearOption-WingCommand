using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WingCommand
{
    /// <summary>A 120 s telemetry ring per wing slot, sampled at 20 Hz while dev tools are on (spec §8). Dumped
    /// to <c>v1/telemetry/*.csv</c> on the hotkey, or automatically on a GCAS activation or collision emergency
    /// (at most once per <see cref="AutoDumpCooldown"/>). Same schema as the FlightSim traces.</summary>
    internal static class TelemetryRecorder
    {
        public static float AutoDumpCooldown = 10f;

        private static readonly TelemetryRing[] rings = new TelemetryRing[FormationCatalog.MaxSlots];
        private static float lastAutoDump = float.NegativeInfinity;

        public static void Sample(WingMember m, WingFrame frame, float time)
        {
            int slot = m.Brain.Slot;
            TelemetryRing ring = rings[slot] ?? (rings[slot] = new TelemetryRing());
            ring.Push(TelemetryRows.From(time, slot, m.Last, m.Brain, frame.Slots[slot].Ref.Pos));
        }

        public static void AutoDump(string reason, float time)
        {
            if (time - lastAutoDump < AutoDumpCooldown) return;
            lastAutoDump = time;
            Dump(reason);
        }

        public static string Dump(string reason)
        {
            var sb = new StringBuilder(TelemetryCsv.Header);
            int count = 0;
            foreach (TelemetryRing ring in rings)
            {
                if (ring == null) continue;
                for (int i = 0; i < ring.Count; i++)
                {
                    sb.Append('\n');
                    TelemetryCsv.AppendRow(sb, ring[i]);
                    count++;
                }
            }
            if (count == 0)
            {
                WingToast.Show("No telemetry yet (dev tools record while wingmen fly)");
                return null;
            }
            try
            {
                string dir = Path.Combine(WingConfig.DataRoot, "telemetry");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir,
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + reason + ".csv");
                File.WriteAllText(path, sb.ToString());
                Plugin.Logger.LogInfo($"[Telemetry] {count} rows → {path}");
                WingToast.Show("Telemetry saved");
                return path;
            }
            catch (IOException e)
            {
                Plugin.Logger.LogWarning("[Telemetry] could not write: " + e.Message);
                return null;
            }
        }

        public static void Clear()
        {
            foreach (TelemetryRing ring in rings) ring?.Clear();
            lastAutoDump = float.NegativeInfinity;
        }
    }
}
