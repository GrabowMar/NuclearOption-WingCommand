using System;

namespace WingCommand
{
#pragma warning disable CS0649 // set by the M6b transport (and the tests)
    /// <summary>What the host knows when a client's command arrives (spec M6 §2.3).</summary>
    internal struct CommandContext
    {
        /// <summary>The sender has a wing on the host.</summary>
        public bool OwnsWing;
        /// <summary>Waypoints must lie within ±this on both map axes.</summary>
        public float MapHalfSize;
        /// <summary>A unit id the host accepts as a target (alive, known to the sender's faction).</summary>
        public Func<uint, bool> Knows;
    }
#pragma warning restore CS0649

    /// <summary>One sender's validation state: the last accepted sequence number and its token bucket.</summary>
    internal sealed class SenderState
    {
        public uint LastSeq;
        public float Tokens = CommandValidator.Burst;
        public float At;
    }

    /// <summary>Spec M6 §2.3: the host checks every client command before it touches a wing — the sender owns a wing, the
    /// command is known, its sequence number is above the last (replays and reordering on the unreliable channel), the
    /// sender's token bucket (burst <see cref="Burst"/>, <see cref="RefillPerSecond"/> a second) has a token, every
    /// waypoint is finite, on the map and under <see cref="MaxAltitude"/> (altitude may be left out: NaN), every
    /// argument is finite, and every unit id is one the host knows. Every command past the ownership check costs a token,
    /// refused or not (review M6a I3: refusals are not free to send); a refusal does not advance the sequence.</summary>
    internal static class CommandValidator
    {
        public static float Burst = 20f, RefillPerSecond = 10f, MaxAltitude = 30000f;

        public static bool Check(in WcCommand c, in CommandContext ctx, SenderState s, float now, out string reason)
        {
            s.Tokens = Math.Min(Burst, s.Tokens + Math.Max(0f, now - s.At) * RefillPerSecond);
            s.At = now;
            if (!ctx.OwnsWing)
            {
                reason = "no wing to command";
                return false;
            }
            if (s.Tokens < 1f)
            {
                reason = "commands too fast";
                return false;
            }
            s.Tokens -= 1f;
            reason = c.Kind == CommandKind.None || c.Kind > CommandKind.Doctrine ? "unknown command"
                : c.Seq <= s.LastSeq ? "out-of-order sequence"
                : !WaypointsOnMap(c, ctx.MapHalfSize) ? "waypoint off the map"
                : !ArgsFinite(c) ? "argument not a number"
                : !UnitsKnown(c, ctx) ? "unknown unit"
                : null;
            if (reason != null) return false;
            s.LastSeq = c.Seq;
            return true;
        }

        private static bool WaypointsOnMap(in WcCommand c, float half)
        {
            if (c.Waypoints == null) return true;
            foreach (WcWaypoint w in c.Waypoints)
            {
                if (!Finite(w.X) || !Finite(w.Z) || Math.Abs(w.X) > half || Math.Abs(w.Z) > half) return false;
                if (!float.IsNaN(w.Alt) && (!Finite(w.Alt) || Math.Abs(w.Alt) > MaxAltitude)) return false;
            }
            return true;
        }

        private static bool ArgsFinite(in WcCommand c)
        {
            if (c.Args == null) return true;
            foreach (float a in c.Args)
                if (!Finite(a)) return false;
            return true;
        }

        private static bool UnitsKnown(in WcCommand c, in CommandContext ctx)
        {
            if (c.Units == null) return true;
            foreach (uint id in c.Units)
                if (ctx.Knows == null || !ctx.Knows(id)) return false;
            return true;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
