using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WingCommand
{
    /// <summary>Flies wingman #2 through <see cref="StepSequence"/> open loop (dev tools only, at least
    /// <see cref="MinAgl"/> above the ground). It records 60 Hz telemetry, saves it as CSV, fits the profile
    /// with <see cref="ProfileFit"/> and writes <c>airframes.calibrated.json</c>. New members use the fitted
    /// numbers; the tested member returns to formation seeded from the inputs it was left with.</summary>
    internal static class StepTest
    {
        public static float MinAgl = 1500f;

        private static readonly TelemetryRing ring = new TelemetryRing();
        private static WingMember member;
        private static float t;

        public static void Start(WingService wing)
        {
            if (member != null)
            {
                WingToast.Show("Step test already running");
                return;
            }
            if (wing == null || wing.Members.Count == 0)
            {
                WingToast.Show("Call a wingman first");
                return;
            }
            WingMember m = wing.Members[0];
            if (m.Last.RadarAlt < MinAgl)
            {
                WingToast.Show($"#2 must be above {MinAgl:0} m for a step test");
                return;
            }
            member = m;
            t = 0f;
            ring.Clear();
            WingToast.Show("#2 step test: 30 s, open loop");
        }

        /// <summary>Fly <paramref name="m"/> if it is under test; returns true when this wrote its controls.</summary>
        public static bool Fly(WingMember m, float dt)
        {
            if (member == null || m != member) return false;
            StepCommand c = StepSequence.At(t);
            if (c.Done || !m.Alive)
            {
                Finish(m);
                return false;
            }
            var o = new ControlOutput { Pitch = c.Pitch, Roll = c.Roll, Throttle = c.Throttle, Airbrake = c.Throttle <= 0f };
            ControlWriter.Fly(m.Aircraft, o);
            float speed = m.Last.Vel.Length;
            ring.Push(new TelemetryRow
            {
                Time = t, Member = m.Brain.Slot, Pos = m.Last.Pos, Vel = m.Last.Vel, BankDeg = m.Last.BankDeg, Nz = m.Last.Nz,
                Tas = m.Last.Tas, RollRate = m.Last.P, AccelAlong = speed > 1f ? Vec3.Dot(m.Last.Acc, m.Last.Vel / speed) : 0f,
                Throttle = o.Throttle, Pitch = o.Pitch, Roll = o.Roll, Airbrake = o.Airbrake,
            });
            t += dt;
            return true;
        }

        private static void Finish(WingMember m)
        {
            member = null;
            if (m.Alive) m.Brain.Track(m.Last, EngineSticks.ToPure(ControlWriter.Read(m.Aircraft.GetInputs())), m.Profile);
            string unit = m.Aircraft != null ? m.Aircraft.definition.unitName : "unknown";
            try
            {
                string dir = Path.Combine(WingConfig.DataRoot, "telemetry");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "steptest-" + unit + "-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"), TelemetryCsv.Write(ring));

                Dictionary<string, float> fit = ProfileFit.Fit(ring);
                if (fit.Count == 0)
                {
                    WingToast.Show("Step test: nothing to fit (see the CSV)");
                    return;
                }
                string path = Path.Combine(WingConfig.DataRoot, "airframes.calibrated.json");
                File.WriteAllText(path, ProfileFit.Merge(File.Exists(path) ? File.ReadAllText(path) : null, unit, fit));
                WingData.LoadProfiles(Plugin.Logger);
                WingProfiles.Clear();
                var sb = new StringBuilder("Calibrated " + unit + ":");
                foreach (KeyValuePair<string, float> kv in fit)
                    sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
                Plugin.Logger.LogInfo("[StepTest] " + sb);
                WingToast.Show(sb.ToString());
            }
            catch (IOException e)
            {
                Plugin.Logger.LogWarning("[StepTest] could not save: " + e.Message);
            }
        }
    }
}
