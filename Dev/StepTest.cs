using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WingCommand
{
    /// <summary>Flies wingman #2 through <see cref="StepSequence"/> (dev tools only, at least <see cref="MinAgl"/>
    /// above the ground). Its brain flies between the short open-loop pulses and through the throttle phases, so
    /// the aircraft recovers after each pulse. Every hand-back to the brain is bumpless. The test stops early when
    /// the member is too low or sinking fast (<see cref="StepSequence.Unsafe"/>) or leaves the wing, and never
    /// leaves the lock behind. At the end it saves the rows as CSV, fits the profile with <see cref="ProfileFit"/>
    /// and writes <c>airframes.calibrated.json</c>.</summary>
    internal static class StepTest
    {
        public static float MinAgl = 1500f;

        private static readonly TelemetryRing ring = new TelemetryRing();
        private static WingMember member;
        private static float t;
        private static StepCommand current;
        private static StepPhase lastPhase;
        private static ControlOutput lastApplied;

        public static void Start(WingService wing)
        {
            // A member that left the wing without passing through Forget must not hold the lock.
            if (member != null && (!member.Alive || member.Released || wing == null || !wing.Members.Contains(member)))
                member = null;
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
            lastPhase = StepPhase.Brain;
            lastApplied = EngineSticks.ToPure(ControlWriter.Read(m.Aircraft.GetInputs()));
            ring.Clear();
            WingToast.Show($"#2 step test: {StepSequence.Duration:0} s, recovering between pulses");
        }

        /// <summary>The member left the wing (released, dead, pruned): stop without fitting.</summary>
        public static void Forget(WingMember m)
        {
            if (member == null || m != member) return;
            member = null;
            WingToast.Show("Step test stopped: #2 left the wing");
        }

        /// <summary>Before the brain: true when the test wrote the member's controls this tick (an open-loop pulse).</summary>
        public static bool Fly(WingMember m, float dt)
        {
            if (member == null || m != member) return false;
            if (!m.Alive)
            {
                Forget(m);
                return false;
            }
            if (StepSequence.Unsafe(m.Last, MinAgl))
            {
                End(m, fit: false, why: "too low or sinking fast");
                return false;
            }
            current = StepSequence.At(t);
            if (current.Done)
            {
                End(m, fit: true, why: null);
                return false;
            }
            if (current.Phase == StepPhase.Open)
            {
                var o = new ControlOutput
                {
                    Pitch = current.Pitch, Roll = current.Roll, Throttle = lastApplied.Throttle, Airbrake = lastApplied.Airbrake,
                };
                ControlWriter.Fly(m.Aircraft, o);
                Record(m, o, dt);
                lastPhase = StepPhase.Open;
                return true;
            }
            // Back from an open pulse, or out of a throttle phase: the brain resumes from what was applied.
            if (lastPhase == StepPhase.Open || (lastPhase == StepPhase.Throttle && current.Phase == StepPhase.Brain))
                m.Brain.Track(m.Last, lastApplied, m.Profile);
            lastPhase = current.Phase;
            return false;
        }

        /// <summary>After the brain: the output to apply (the test's throttle during the throttle phases). Records
        /// the tick.</summary>
        public static ControlOutput Adjust(WingMember m, ControlOutput brain, float dt)
        {
            if (member == null || m != member) return brain;
            if (current.Phase == StepPhase.Throttle)
            {
                brain.Throttle = current.Throttle;
                brain.Airbrake = current.Throttle <= 0f;
            }
            Record(m, brain, dt);
            return brain;
        }

        private static void Record(WingMember m, in ControlOutput o, float dt)
        {
            float speed = m.Last.Vel.Length;
            ring.Push(new TelemetryRow
            {
                Time = t, Member = m.Seat, Pos = m.Last.Pos, Vel = m.Last.Vel, BankDeg = m.Last.BankDeg, Nz = m.Last.Nz,
                Tas = m.Last.Tas, RollRate = m.Last.P, AccelAlong = speed > 1f ? Vec3.Dot(m.Last.Acc, m.Last.Vel / speed) : 0f,
                Throttle = o.Throttle, Pitch = o.Pitch, Roll = o.Roll, Airbrake = o.Airbrake, Behaviour = (byte)current.Phase,
            });
            lastApplied = o;
            t += dt;
        }

        private static void End(WingMember m, bool fit, string why)
        {
            member = null;
            if (m.Alive) m.Brain.Track(m.Last, lastApplied, m.Profile);
            string unit = m.Aircraft != null ? m.Aircraft.definition.unitName : "unknown";
            try
            {
                string dir = Path.Combine(WingConfig.DataRoot, "telemetry");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "steptest-" + unit + "-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"), TelemetryCsv.Write(ring));
                if (!fit)
                {
                    Plugin.Logger.LogInfo($"[StepTest] {unit} stopped early: {why}");
                    WingToast.Show($"Step test stopped: {why}");
                    return;
                }

                Dictionary<string, float> values = ProfileFit.Fit(ring);
                if (values.Count == 0)
                {
                    WingToast.Show("Step test: nothing to fit (see the CSV)");
                    return;
                }
                string path = Path.Combine(WingConfig.DataRoot, "airframes.calibrated.json");
                File.WriteAllText(path, ProfileFit.Merge(File.Exists(path) ? File.ReadAllText(path) : null, unit, values));
                WingData.LoadProfiles(Plugin.Logger);
                WingProfiles.Clear();
                var sb = new StringBuilder("Calibrated " + unit + ":");
                foreach (KeyValuePair<string, float> kv in values)
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
