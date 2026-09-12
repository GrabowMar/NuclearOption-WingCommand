using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using WingCommand;

// Reduced-order experiments, not Unity physics. Production guidance drives a lagged
// coordinated-turn plant. Sweep lag/speed/geometry; keep a separate validation set.
internal static class Program
{
    record Tune(float Preview, float Floor, float Bank, float Rise, float Heading, float Station = 45, float Command = 40, float HistoricalInterceptBank = 0);
    record Result(Tune Tune, double Score, double Capture, double Tail, double Worst, double Tracking);
    static float Clamp(float v, float lo, float hi) => Math.Clamp(v, lo, hi);
    static float Blend(float v) { v = Clamp(v, 0, 1); return v * v * (3 - 2 * v); }
    static readonly List<string> Rows = new() { "phase,preview_s,floor_m,bank_deg,rise_deg_s,heading_deg,score,capture_s,tail_error_m,worst_tail_m,tracking_error_m" };

    static void Main(string[] args)
    {
        var original = new Tune(3.5f, 650, 58, 30, 55, 40, 32, 60);
        var previous = new Tune(3, 550, 75, 45, 65);
        var selected = new Tune(WingTuning.FormationLookAheadSeconds, WingTuning.FormationMinLookAhead,
            WingTuning.PursuitBank, WingTuning.FormationBankRiseRate, WingTuning.FormationRejoinCommandAngle,
            WingTuning.StationBank, WingTuning.CommandAngle);
        var best = previous;
        foreach (var seed in new[] { original, previous }) Log("baseline-train", Evaluate(seed, false));
        // Two coordinate passes: bounded candidates avoid rewarding unrealistic near-inverted turns.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (int axis in Enumerable.Range(0, 5))
            {
                float[] values = axis switch {
                    0 => new[] { 2.5f, 3f, 3.5f, 4f },
                    1 => new[] { 450f, 550f, 650f, 750f },
                    2 => new[] { 58f, 65f, 70f, 75f },
                    3 => new[] { 30f, 45f, 60f, 75f },
                    _ => new[] { 45f, 50f, 55f, 65f } };
                var choices = values.Select(v => axis switch {
                    0 => best with { Preview = v }, 1 => best with { Floor = v },
                    2 => best with { Bank = v }, 3 => best with { Rise = v },
                    _ => best with { Heading = v } }).Select(t => Evaluate(t, false)).ToArray();
                foreach (var r in choices) Log("train", r);
                best = choices.OrderBy(r => r.Score).First().Tune;
            }
        }
        foreach (var t in new[] { original, previous, best, new Tune(3, 550, 75, 75, 55), new Tune(2.5f, 450, 70, 60, 55), new Tune(3, 450, 75, 75, 55), new Tune(3, 450, 65, 60, 55), selected })
        {
            var r = Evaluate(t, true); Log("validation", r);
            Console.WriteLine($"VALIDATION {t}: score={r.Score:F2}, capture={r.Capture:F1}s, tail={r.Tail:F1}m, worst={r.Worst:F1}m, tracking={r.Tracking:F1}m");
        }
        VerticalSweep(args[0]);
        File.WriteAllLines(Path.Combine(args[0], "horizontal.csv"), Rows);
        Console.WriteLine($"PRODUCTION {selected}");
        Console.WriteLine($"HORIZONTAL-ONLY WINNER {best}; final selection also considers vertical tracking and bank margin.");
    }

    static void Log(string phase, Result r) => Rows.Add(FormattableString.Invariant(
        $"{phase},{r.Tune.Preview},{r.Tune.Floor},{r.Tune.Bank},{r.Tune.Rise},{r.Tune.Heading},{r.Score},{r.Capture},{r.Tail},{r.Worst},{r.Tracking}"));

    static Result Evaluate(Tune t, bool validation)
    {
        WingTuning.PursuitBank = t.Bank;
        WingTuning.StationBank = t.Station; WingTuning.CommandAngle = t.Command;
        WingTuning.FormationBankRiseRate = t.Rise;
        double captureSum = 0, tailSum = 0, worst = 0, trackingSum = 0; int count = 0;
        var starts = validation
            ? new[] { (-3100f, -2400f, 35f), (1700f, 2600f, -130f), (500f, -700f, 110f) }
            : new[] { (-1500f, -3500f, 0f), (2200f, 1800f, 0f), (0f, 3000f, 180f) };
        foreach (float lag in validation ? new[] { 0.6f, 1.5f } : new[] { 0.4f, 0.8f, 1.2f })
        foreach (float leaderSpeed in validation ? new[] { 85f, 175f } : new[] { 68f, 120f, 220f })
        foreach (int maneuver in new[] { 0, 1 })
        foreach (var start in starts)
        {
            const float dt = 0.1f;
            float x = start.Item1, z = start.Item2, heading = start.Item3 * MathF.PI / 180;
            float speed = leaderSpeed + 27, bank = 0, bankMemory = 8, leaderZ = 0, leaderX = 0, leaderHeading = 0;
            float capture = 600, tail = 0, tracking = 0;
            var recovery = new FormationRecovery();
            for (int step = 0; step < 6000; step++)
            {
                var velocity = new Vector2(MathF.Sin(heading), MathF.Cos(heading)) * speed;
                float time = step * dt;
                float turnRate = maneuver == 1 && time > 100 && time < 500
                    ? (validation ? 0.018f : 0.025f) * MathF.Sin((time - 100) / (validation ? 13 : 18)) : 0;
                leaderHeading += turnRate * dt;
                var leaderForward = new Vector2(MathF.Sin(leaderHeading), MathF.Cos(leaderHeading));
                var leaderVelocity = leaderForward * leaderSpeed;
                var gap = new Vector2(leaderX - x, leaderZ - z); float distance = gap.Length();
                if (distance < 300 && capture == 600) capture = step * dt;
                float blend = Blend(distance / WingTuning.CaptureDistance);
                float along = Vector2.Dot(gap, leaderForward);
                float closing = Vector2.Dot(velocity - leaderVelocity, leaderForward);
                recovery.UpdateMode(leaderSpeed, 60, along, 120, dt, false,
                    FormationRecovery.CanYieldAhead(distance, gap.X * leaderForward.Y - gap.Y * leaderForward.X, MathF.Cos(heading - leaderHeading), leaderSpeed, 60, 120,
                        recovery.Mode == FormationRecoveryMode.Overshoot));
                float station = leaderSpeed + FormationControlRules.RejoinClosure(along, closing,
                    2, 1, 1, 0.45f, 3, 90, 0.75f);
                float approach = FormationTracking.ApproachSpeed(gap.X, gap.Y, velocity.X, velocity.Y,
                    leaderVelocity.X, leaderVelocity.Y, 2, 1, 1, 0.75f);
                float desired = station + (approach - station) * blend;
                var plan = FormationIntercept.Solve(gap, leaderVelocity, Vector2.Zero,
                    leaderVelocity, speed, 340, turnRate);
                if (recovery.Blend < 0.01f)
                    desired = FormationClosure.PursuitSpeed(gap, velocity, plan, desired, 340, 2, 0.75f, 120);
                desired += (Math.Max(60, leaderSpeed - 10) - desired) * recovery.Blend;
                speed += Clamp((Clamp(desired, 60, 340) - speed) / 1.5f, -2, 3) * dt;
                float preview = Math.Max(t.Floor, speed * t.Preview);
                var command = FormationGuidance.Horizontal(gap, velocity, leaderVelocity, leaderForward,
                    plan.Gap, plan.ArrivalVelocity, distance, preview, speed, blend, 1, 1);
                Vector2 aim = command.Aim;
                if (recovery.Blend > 0)
                {
                    float lane = FormationRecovery.LaneCorrection(x, x < 0 ? -240 : 240, preview) * preview;
                    aim = Vector2.Lerp(aim, new(lane, preview), recovery.Blend);
                }
                FormationControlRules.SafeRejoinDirection(velocity.X, 0, velocity.Y, aim.X, 0, aim.Y,
                    WingTuning.CommandAngle + (t.Heading - WingTuning.CommandAngle) * blend,
                    25, 22, 600, out float sx, out _, out float sz);
                float error = FormationTracking.WrapDegrees((MathF.Atan2(sx, sz) - heading) * 180 / MathF.PI);
                bool intercept = distance > 450 || blend > 0.35f;
                float leaderBank = MathF.Atan(leaderSpeed * turnRate / 9.81f) * 180 / MathF.PI;
                float demand = intercept ? (t.HistoricalInterceptBank > 0
                    ? Clamp(Math.Abs(error) * 2, 8, t.HistoricalInterceptBank) : FormationGuidance.InterceptBank(error))
                    : Clamp(Math.Abs(leaderBank) + Math.Abs(error) * 3, 8,
                        Math.Max(WingTuning.StationBank + (t.Bank - WingTuning.StationBank) * blend, Math.Abs(leaderBank) * 1.7f + 8));
                demand = Math.Min(demand, FormationGuidance.AirborneBankLimit(600, speed, 50));
                if (!intercept) demand += (Math.Min(demand, 25) - demand) * recovery.Blend;
                bankMemory = Math.Min(demand, bankMemory + FormationGuidance.BankRiseRate(0) * dt);
                // Native altitude compensation cancels; level-flight native extra multiplier is 1.2.
                // Cap at 85 degrees: this surrogate cannot represent inverted lift.
                float demandedBank = Math.Sign(error) * Math.Min(85, bankMemory * 1.2f);
                bank += (demandedBank - bank) * (1 - MathF.Exp(-dt / lag));
                heading += 9.81f * MathF.Tan(bank * MathF.PI / 180) / speed * dt;
                x += MathF.Sin(heading) * speed * dt; z += MathF.Cos(heading) * speed * dt;
                leaderZ += leaderVelocity.Y * dt; leaderX += leaderVelocity.X * dt;
                if (time >= 100 && time < 500) tracking += distance / 4000;
                if (step >= 5700) tail = Math.Max(tail, distance);
                if (!float.IsFinite(distance)) throw new Exception("Non-finite trajectory");
            }
            captureSum += capture; trackingSum += tracking; tailSum += tail; worst = Math.Max(worst, tail); count++;
        }
        // Penalise failure to settle as well as first capture; metres and seconds are explicit weights.
        return new(t, captureSum / count + tailSum / count * 0.5 + worst * 0.1 + trackingSum / count * 0.1,
            captureSum / count, tailSum / count, worst, trackingSum / count);
    }

    static void VerticalSweep(string output)
    {
        var rows = new List<string> { "preview_s,floor_m,pitch_up_deg,pitch_down_deg,mean_absolute_error_m,worst_final_error_m" };
        foreach (float preview in new[] { 2.5f, 3f, 3.5f })
        foreach (float floor in new[] { 450f, 550f, 650f })
        foreach (var limits in new[] { (18f, 15f), (22f, 18f), (25f, 22f), (30f, 25f) })
        {
            double sum = 0, worst = 0; int count = 0;
            foreach (float speed in new[] { 68f, 120f, 220f })
            foreach (float lag in new[] { 0.5f, 1f, 2f })
            foreach (float initial in new[] { -300f, 300f })
            {
                float y = initial, vy = 0, target = 0;
                float baseline = Math.Max(floor, speed * preview);
                for (int step = 0; step < 1800; step++)
                {
                    float time = step * 0.1f;
                    float targetVy = time >= 40 && time < 70 ? 15 : time >= 90 && time < 120 ? -15 : 0;
                    float gap = target - y, blend = Blend(Math.Abs(gap) / 300);
                    float cap = 20 + (baseline * MathF.Tan(40 * MathF.PI / 180) - 20) * blend;
                    float correction = FormationControlRules.KinematicVerticalCorrection(gap, vy - targetVy, cap, 1, 4, 1, 1);
                    float rise = FormationControlRules.VerticalAimRise(baseline, speed, baseline, targetVy, correction);
                    FormationControlRules.SafeRejoinDirection(0, vy, speed, 0, rise, baseline, 55,
                        7 + (limits.Item1 - 7) * blend, 6 + (limits.Item2 - 6) * blend, 1000,
                        out _, out float sy, out float sz);
                    float desiredVy = speed * sy / Math.Max(0.01f, sz);
                    vy += Clamp((desiredVy - vy) / lag, -6, 6) * 0.1f;
                    y += vy * 0.1f; target += targetVy * 0.1f;
                    sum += Math.Abs(target - y); count++;
                    if (step >= 1700) worst = Math.Max(worst, Math.Abs(target - y));
                }
            }
            rows.Add(FormattableString.Invariant($"{preview},{floor},{limits.Item1},{limits.Item2},{sum / count},{worst}"));
        }
        File.WriteAllLines(Path.Combine(output, "vertical.csv"), rows);
    }
}
