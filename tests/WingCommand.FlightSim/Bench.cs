using System;
using System.Diagnostics;

namespace WingCommand.FlightSim
{
    /// <summary>Timing for the P1 budgets: the fastest of several batches is the code's cost, the slower ones are the
    /// machine's (a preempted shared CI runner pushed a single 20 000-tick window to 20.18 µs against 20). The budget stays;
    /// a real slowdown slows every batch.</summary>
    internal static class Bench
    {
        /// <summary>Microseconds per tick of the fastest batch, and the bytes allocated across all of them.</summary>
        public static double BestMicroseconds(Action<int> tick, int batches, int perBatch, out long allocated)
        {
            var watch = new Stopwatch();
            double best = double.MaxValue;
            int i = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int b = 0; b < batches; b++)
            {
                watch.Restart();
                for (int k = 0; k < perBatch; k++) tick(i++);
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds * 1000.0 / perBatch);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return best;
        }
    }
}
