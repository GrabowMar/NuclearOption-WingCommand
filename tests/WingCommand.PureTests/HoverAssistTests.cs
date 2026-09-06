using Xunit;

// Native hover and nozzle ownership is stubbed; the complete production transition
// is linked so a stale native hover flag cannot hide a retained downward nozzle.
namespace WingCommand
{
    internal sealed class ControlsFilter
    {
        public bool CanHover = true;
        public bool HoverEnabled;
        public bool HasAutoHover() => CanHover;
        public bool IsAutoHoverEnabled() => HoverEnabled;
        public void SetAutoHover(bool enabled) => HoverEnabled = enabled;
    }

    internal sealed class ControlInputs
    {
        public float customAxis1;
    }

    internal sealed class SwivelDuctSystem { }
    internal sealed class DuctedThrustSystem { }
    internal readonly struct GlobalPosition { }
    internal sealed class Autopilot
    {
        public void Hover(GlobalPosition destination, float altitudeHold,
            UnityEngine.Vector3 aimDirection) { }
    }

    internal partial class Aircraft
    {
        public readonly ControlInputs HoverTestInputs = new ControlInputs();
        public ControlsFilter HoverTestFilter = new ControlsFilter();
        public SwivelDuctSystem HoverTestDucts;
        public DuctedThrustSystem HoverTestVagrantDucts;
        public Autopilot autopilot = new Autopilot();
        public ControlInputs GetInputs() => HoverTestInputs;
        public ControlsFilter GetControlsFilter() => HoverTestFilter;
        public T GetComponentInChildren<T>(bool includeInactive) where T : class =>
            HoverTestDucts as T ?? HoverTestVagrantDucts as T;
    }
}

namespace WingCommand.PureTests
{
    public sealed class HoverAssistTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LeavingHoverRestoresVectoringCruiseEvenWhenNativeStateAlreadyClearedFlag(bool hoverEnabled)
        {
            var aircraft = new Aircraft { HoverTestDucts = new SwivelDuctSystem() };
            aircraft.HoverTestFilter.HoverEnabled = hoverEnabled;
            aircraft.GetInputs().customAxis1 = 0f;

            HoverAssist.Release(aircraft);

            Assert.False(aircraft.HoverTestFilter.HoverEnabled);
            Assert.Equal(1f, aircraft.GetInputs().customAxis1);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VagrantCruiseRestoresItsDuctedThrustAfterHover(bool hoverEnabled)
        {
            var aircraft = new Aircraft { HoverTestVagrantDucts = new DuctedThrustSystem() };
            aircraft.HoverTestFilter.HoverEnabled = hoverEnabled;
            aircraft.GetInputs().customAxis1 = 0.25f;

            HoverAssist.Release(aircraft);

            Assert.False(aircraft.HoverTestFilter.HoverEnabled);
            Assert.Equal(1f, aircraft.GetInputs().customAxis1);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        public void HelicopterOrTiltwingKeepsItsOwnCustomAxis(float axis)
        {
            var aircraft = new Aircraft();
            aircraft.HoverTestFilter.HoverEnabled = true;
            aircraft.GetInputs().customAxis1 = axis;

            HoverAssist.Release(aircraft);

            Assert.False(aircraft.HoverTestFilter.HoverEnabled);
            Assert.Equal(axis, aircraft.GetInputs().customAxis1);
        }

        [Fact]
        public void AircraftWithoutHoverCapabilityKeepsItsConfiguration()
        {
            var aircraft = new Aircraft { HoverTestDucts = new SwivelDuctSystem() };
            aircraft.HoverTestFilter.CanHover = false;
            aircraft.GetInputs().customAxis1 = 0.5f;

            HoverAssist.Release(aircraft);

            Assert.Equal(0.5f, aircraft.GetInputs().customAxis1);
        }
    }
}
