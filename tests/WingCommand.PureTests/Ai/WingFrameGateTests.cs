using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingFrameGateTests : IDisposable
    {
        public WingFrameGateTests()
        {
            WingFidelity.Begin(WingMode.Smart);
        }

        public void Dispose() => WingFidelity.Begin(WingMode.Smart);

        [Fact]
        public void HitchSkipsPolishAndLeavesSurvivalGatesOn()
        {
            Assert.True(WingFidelity.RichChatter);
            Assert.True(WingFidelity.Deconfliction);
            Assert.True(WingFidelity.Full);

            WingFrameGate.NoteFrame(0.040f);
            Assert.True(WingFrameGate.Recovering);
            Assert.False(WingFidelity.RichChatter);
            Assert.False(WingFidelity.Deconfliction);
            Assert.True(WingFidelity.Full);
            Assert.True(WingFidelity.Manoeuvres);
        }

        [Fact]
        public void RecoveryLastsTwoQuietFrames()
        {
            WingFrameGate.NoteFrame(0.040f);
            WingFrameGate.NoteFrame(0.016f);
            Assert.True(WingFrameGate.Recovering);
            WingFrameGate.NoteFrame(0.016f);
            Assert.False(WingFrameGate.Recovering);
            Assert.True(WingFidelity.RichChatter);
        }

        [Fact]
        public void PerformanceModeStaysOffDuringAHitch()
        {
            WingFidelity.Begin(WingMode.Performance);
            WingFrameGate.NoteFrame(0.040f);
            Assert.False(WingFidelity.RichChatter);
            Assert.False(WingFidelity.Full);
        }
    }
}
