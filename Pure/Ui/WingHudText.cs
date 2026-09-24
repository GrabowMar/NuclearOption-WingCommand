using System;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>Text for the wing HUD panel: member phase, slot error and the most urgent binding limit, plus the
    /// autopilot annunciator. Invariant formatting and metric units (m, km/h, m/s). Called by the HUD at ≤ 5 Hz,
    /// never on the flight hot path.</summary>
    internal static class WingHudText
    {
        public static string Phase(BehaviourId behaviour, bool fallingBehind)
        {
            // Evading a missile outranks falling behind (the rejoin planner keeps running under the defence).
            if (fallingBehind && behaviour != BehaviourId.Defend) return "BEHIND";
            switch (behaviour)
            {
                case BehaviourId.StationKeep: return "SLOT";
                case BehaviourId.HoldOverhead: return "HOLD";
                case BehaviourId.Trail: return "TRAIL";
                case BehaviourId.Defend: return "DEFEND";
                default: return "JOIN";
            }
        }

        /// <summary>GCAS, then a collision nudge, then the first axis a constraint bound.</summary>
        public static string Binding(in BindingReport r)
        {
            if (r.GcasActive) return "GCAS";
            if (r.CollisionActive) return "COLL";
            if (r.BankBy != ConstraintId.None) return "BANK " + Short(r.BankBy);
            if (r.NzBy != ConstraintId.None) return "NZ " + Short(r.NzBy);
            if (r.VerticalBy != ConstraintId.None) return "VERT " + Short(r.VerticalBy);
            if (r.SpeedBy != ConstraintId.None) return "SPD " + Short(r.SpeedBy);
            return "";
        }

        public static float BingoShowSeconds = 600f;

        /// <summary>A helicopter landing here: LAND on the way down, DOWN on the ground, LIFT on the way up (spec M4 §5).</summary>
        public static string Settle(SettlePhase phase) =>
            phase == SettlePhase.Approach || phase == SettlePhase.Descend ? "LAND"
            : phase == SettlePhase.Down ? "DOWN"
            : phase == SettlePhase.LiftOff ? "LIFT"
            : null;

        /// <summary>FIGHT while engaged, RTB/REFIT while recovering (spec M5 §6.2), else null (the formation phase).</summary>
        public static string Duty(bool engaged, bool recovering, RecoveryIntent intent) =>
            engaged ? "FIGHT" : recovering ? (intent == RecoveryIntent.Refit ? "REFIT" : "RTB") : null;

        /// <summary>"B m:ss" to bingo under <see cref="BingoShowSeconds"/>, else empty.</summary>
        public static string BingoTime(float seconds)
        {
            if (!(seconds < BingoShowSeconds)) return "";
            int s = Math.Max(0, (int)seconds);
            return string.Format(CultureInfo.InvariantCulture, "B {0}:{1:00}", s / 60, s % 60);
        }

        public static string Member(int slot, string phase, float slotErrorM, string binding, string bingo) =>
            Member(slot, phase, slotErrorM, string.IsNullOrEmpty(bingo) ? binding : string.IsNullOrEmpty(binding) ? bingo : bingo + " " + binding);

        public static string Member(int slot, string phase, float slotErrorM, string binding) =>
            string.Format(CultureInfo.InvariantCulture, "{0}  {1,-6}{2,5:0}m  {3}", slot + 2, phase, slotErrorM, binding)
                .TrimEnd();

        public static string Autopilot(in HoldSpec h, bool lateralOverride, bool verticalOverride)
        {
            var sb = new StringBuilder();
            if (h.Lateral == LateralHold.Level) Part(sb, "LVL", lateralOverride);
            else if (h.Lateral == LateralHold.Heading)
            {
                int hdg = (int)Math.Round(h.HeadingDeg) % 360;
                if (hdg < 0) hdg += 360;
                Part(sb, "HDG " + hdg.ToString("000", CultureInfo.InvariantCulture), lateralOverride);
            }
            if (h.Vertical == VerticalHold.Altitude)
                Part(sb, "ALT " + h.AltitudeM.ToString("0", CultureInfo.InvariantCulture), verticalOverride);
            else if (h.Vertical == VerticalHold.VerticalSpeed)
                Part(sb, "VS " + h.VerticalSpeedMps.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture), verticalOverride);
            if (h.Speed) Part(sb, "SPD " + (h.SpeedMps * 3.6f).ToString("0", CultureInfo.InvariantCulture), false);
            return sb.Length == 0 ? "" : "AP " + sb;
        }

        private static void Part(StringBuilder sb, string text, bool overridden)
        {
            if (sb.Length > 0) sb.Append("  ");
            sb.Append(overridden ? "(" + text + ")" : text);
        }

        private static string Short(ConstraintId id)
        {
            switch (id)
            {
                case ConstraintId.Collision: return "COLL";
                case ConstraintId.Terrain: return "TERR";
                case ConstraintId.Envelope: return "ENV";
                case ConstraintId.Gcas: return "GCAS";
                case ConstraintId.Authority: return "AUTH";
                default: return "";
            }
        }
    }
}
