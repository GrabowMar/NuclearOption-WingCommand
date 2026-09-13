using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class WingLogBufferTests
    {
        [Fact]
        public void ExportContainsOnlyOwnBoundedCodeMetadata()
        {
            var source = new SensitiveSource();
            var buffer = new WingLogBuffer(source, typeof(WingLogBufferTests).Assembly);
            var initial = buffer.Snapshot();
            buffer.Record(new SensitiveSource(), 2);
            Assert.Equal(initial, buffer.Snapshot());

            for (int i = 0; i <= WingLogBuffer.Capacity; i++) buffer.Record(source, 4);
            var snapshot = buffer.Snapshot();
            Assert.Equal(initial.Length + WingLogBuffer.Capacity, snapshot.Length);
            Assert.Contains("Older events discarded: 1", snapshot);
            Assert.Contains("level=4", snapshot[snapshot.Length - 1]);
            Assert.Contains(nameof(ExportContainsOnlyOwnBoundedCodeMetadata), snapshot[snapshot.Length - 1]);
            Assert.DoesNotContain("PRIVATE", string.Join("\n", snapshot));
            Assert.DoesNotContain("C:\\", string.Join("\n", snapshot));
        }

        [Fact]
        public void OnlyAllowlistedPayloadsAreFormatted()
        {
            var source = new object();
            var buffer = new WingLogBuffer(source, typeof(WingLogBufferTests).Assembly);
            buffer.Record(source, 2, new SensitiveSource());
            buffer.Record(source, 2, "PRIVATE C:\\Users\\Player steamID=123");
            buffer.Record(new object(), 16, new WingDiagnostic(WingDiagnosticEvent.PluginReady, 0));
            buffer.Record(source, 16, new WingDiagnostic(WingDiagnosticEvent.PatchesInstalled, 24));
            string output = string.Join("\n", buffer.Snapshot());
            Assert.Contains("Harmony patches installed: count=24", output);
            Assert.DoesNotContain("PRIVATE", output);
            Assert.DoesNotContain("Plugin ready", output);
        }

        private sealed class SensitiveSource
        {
            public override string ToString() => throw new Exception("PRIVATE player data must never be read");
        }
    }
}
