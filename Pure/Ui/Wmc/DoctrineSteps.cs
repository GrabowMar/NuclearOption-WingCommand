using System;

namespace WingCommand
{
    internal enum DoctrineAxis : byte { Guard, Response, Interval, Spread, Targets, Reach }

    /// <summary>The DOCT tab's per-axis steppers (spec M7b §3): one axis advances and wraps; the others stay.</summary>
    internal static class DoctrineSteps
    {
        public static WingDoctrine Cycle(WingDoctrine d, DoctrineAxis axis, int direction)
        {
            int dir = direction < 0 ? -1 : 1;
            MissileGuard guard = d.Guard;
            MissileResponse response = d.Response;
            FormationInterval interval = d.Interval;
            bool spread = d.SpreadWhenThreatened;
            TargetPolicy targets = d.Targets;
            EngagementReach reach = d.Reach;
            switch (axis)
            {
                case DoctrineAxis.Guard: guard = Step(guard, dir); break;
                case DoctrineAxis.Response: response = Step(response, dir); break;
                case DoctrineAxis.Interval: interval = Step(interval, dir); break;
                case DoctrineAxis.Spread: spread = !spread; break;
                case DoctrineAxis.Targets: targets = Step(targets, dir); break;
                default: reach = Step(reach, dir); break;
            }
            return new WingDoctrine(guard, response, interval, spread, targets, reach);
        }

        public static string Word(WingDoctrine d, DoctrineAxis axis)
        {
            switch (axis)
            {
                case DoctrineAxis.Guard: return Up(d.Guard);
                case DoctrineAxis.Response: return d.Guard == MissileGuard.Off ? WmcText.Unknown : Up(d.Response);
                case DoctrineAxis.Interval: return Up(d.Interval);
                case DoctrineAxis.Spread: return d.SpreadWhenThreatened ? "ON" : "OFF";
                case DoctrineAxis.Targets: return Up(d.Targets);
                default: return Up(d.Reach);
            }
        }

        public static string Label(DoctrineAxis axis)
        {
            switch (axis)
            {
                case DoctrineAxis.Guard: return "MISSILE GUARD";
                case DoctrineAxis.Response: return "MISSILE RESPONSE";
                case DoctrineAxis.Interval: return "INTERVAL";
                case DoctrineAxis.Spread: return "SPREAD WHEN THREATENED";
                case DoctrineAxis.Targets: return "STANDING TARGETS";
                default: return "REACH";
            }
        }

        private static string Up<T>(T value) where T : Enum => value.ToString().ToUpperInvariant();

        private static T Step<T>(T value, int dir) where T : Enum
        {
            var all = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(all, value);
            return all[((i + dir) % all.Length + all.Length) % all.Length];
        }
    }
}
