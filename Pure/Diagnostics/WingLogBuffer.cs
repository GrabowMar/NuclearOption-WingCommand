using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace WingCommand
{
    /// <summary>Stores only code metadata, never log payloads or player/environment data.</summary>
    internal sealed class WingLogBuffer
    {
        internal const int Capacity = 4096;
        private readonly object source;
        private readonly Assembly assembly;
        private readonly Queue<string> entries = new Queue<string>();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private int discarded;

        internal WingLogBuffer(object source, Assembly assembly)
        {
            this.source = source;
            this.assembly = assembly;
        }

        internal void Record(object logSource, int level, object data = null)
        {
            if (!ReferenceEquals(source, logSource)) return;

            // No file names, line numbers, exception stacks, or message.ToString().
            string location = "WingCommand";
            foreach (var frame in new StackTrace(false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (type?.Assembly != assembly || type == typeof(WingLogBuffer)
                    || type.FullName == "WingCommand.WingLogExport") continue;
                if (type.FullName == "WingCommand.Plugin"
                    && (method.Name == "WriteVerbose" || method.Name == "LogVerbose"
                        || method.Name == "LogAction")) continue;
                location = type.FullName + "." + method.Name + " IL=" + frame.GetILOffset();
                break;
            }

            lock (entries)
            {
                if (entries.Count == Capacity) { entries.Dequeue(); discarded++; }
                string detail = data is WingDiagnostic diagnostic ? " | " + diagnostic : "";
                entries.Enqueue($"+{elapsed.ElapsedMilliseconds}ms level={level} {location}{detail}");
            }
        }

        internal string[] Snapshot()
        {
            lock (entries)
            {
                var result = new List<string>
                {
                    "Wing Command privacy-safe log events (current session)",
                    "Unstructured message bodies omitted: they may contain personal data.",
                    "Includes severity, elapsed time, mod code locations and allowlisted mod diagnostics.",
                    "Levels: Fatal=1 Error=2 Warning=4 Message=8 Info=16 Debug=32",
                    $"Older events discarded: {discarded}"
                };
                result.AddRange(entries);
                return result.ToArray();
            }
        }
    }
}
