using System;
using Xunit;

// Link production lane ownership and live-anchor distance checks with minimal engine stubs. Shared
// vectors, transforms, and maths live in GameTypeStubs.
namespace UnityEngine
{
    public static partial class Time
    {
        public static float unscaledTime;
    }
}

namespace WingCommand
{
    internal partial class Aircraft
    {
        public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
        public float maxRadius = 10f;
    }

    internal sealed class Airbase
    {
        public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
        public string name = "Test base";
    }

    internal sealed class Hangar
    {
        public readonly UnityEngine.Transform Spawn = new UnityEngine.Transform();
        public UnityEngine.Transform GetSpawnTransform() => Spawn;
    }

    internal static class WingLaunchFields
    {
        public static string DisplayName(Airbase airbase) => airbase?.name ?? "FIELD";
    }

    internal static partial class Plugin
    {
        internal static readonly TestLogger Logger = new TestLogger();
        internal static void LogVerbose(string message) { }
        internal sealed class TestLogger
        {
            public void LogWarning(string message) { }
            public void LogInfo(string message) { }
        }
    }
}

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public class HangarDepartureLaneTests : IDisposable
    {
        public HangarDepartureLaneTests()
        {
            HangarDepartureLane.Reset();
            UnityEngine.Time.unscaledTime = 0f;
        }

        public void Dispose() => HangarDepartureLane.Reset();

        [Fact]
        public void OneDepartureAtATimePerField()
        {
            var airbase = new Airbase();
            Assert.True(HangarDepartureLane.IsFree(airbase));
            Assert.True(HangarDepartureLane.Reserve(airbase, new Hangar(), new object()));

            // Reserve per airbase so pad and runway departures cannot overlap at the same field.
            Assert.False(HangarDepartureLane.IsFree(airbase));
            Assert.False(HangarDepartureLane.Reserve(airbase, new Hangar(), new object()));

            // Other airbases retain independent capacity.
            Assert.True(HangarDepartureLane.Reserve(new Airbase(), new Hangar(), new object()));
        }

        [Fact]
        public void DelayedNativeSpawnKeepsItsLaneUntilTheAircraftIsTracked()
        {
            var airbase = new Airbase();
            var owner = new object();
            Assert.True(HangarDepartureLane.Reserve(airbase, new Hangar(), owner));

            UnityEngine.Time.unscaledTime = WingTuning.HangarDeliveryTimeout + 1f;
            HangarDepartureLane.Tick();
            Assert.False(HangarDepartureLane.IsFree(airbase));
            Assert.False(HangarDepartureLane.Reserve(airbase, new Hangar(), new object()));

            var aircraft = new Aircraft();
            HangarDepartureLane.Track(owner, aircraft);
            HangarDepartureLane.Tick();
            Assert.False(HangarDepartureLane.IsFree(airbase));

            aircraft.transform.position = new UnityEngine.Vector3(121f, 0f, 0f);
            HangarDepartureLane.Tick();
            Assert.True(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void CancellingAnotherQueuedOrderCannotReleaseAnOccupiedLane()
        {
            var airbase = new Airbase();
            var accepted = new object();
            var queued = new object();
            Assert.True(HangarDepartureLane.Reserve(airbase, new Hangar(), accepted));
            HangarDepartureLane.Track(accepted, new Aircraft());

            HangarDepartureLane.Release(queued);
            Assert.False(HangarDepartureLane.IsFree(airbase));
            HangarDepartureLane.Release(accepted);
            Assert.True(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void DestroyedTrackedAircraftReleasesItsLane()
        {
            var airbase = new Airbase();
            var owner = new object();
            Assert.True(HangarDepartureLane.Reserve(airbase, new Hangar(), owner));
            HangarDepartureLane.Track(owner, null);
            HangarDepartureLane.Tick();
            Assert.True(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void ADelayedPlaneCannotReleaseItsOccupiedLaneOnATimer()
        {
            var airbase = new Airbase();
            var owner = new object();
            var aircraft = new Aircraft();
            Assert.True(HangarDepartureLane.Reserve(airbase, new Hangar(), owner));
            HangarDepartureLane.Track(owner, aircraft);
            UnityEngine.Time.unscaledTime = WingTuning.HangarDeliveryTimeout * 2f;
            HangarDepartureLane.Tick();
            Assert.False(HangarDepartureLane.IsFree(airbase));
            Assert.False(HangarDepartureLane.Reserve(airbase, new Hangar(), new object()));

            aircraft.transform.position = new UnityEngine.Vector3(150f, 0f, 0f);
            HangarDepartureLane.Tick();
            Assert.True(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void AStuckDepartureReportsTheFieldAsJammed()
        {
            var airbase = new Airbase();
            var owner = new object();
            var aircraft = new Aircraft();
            HangarDepartureLane.Reserve(airbase, new Hangar(), owner);
            HangarDepartureLane.Track(owner, aircraft);

            Assert.False(HangarDepartureLane.IsJammed(airbase));

            UnityEngine.Time.unscaledTime = WingTuning.HangarDeliveryTimeout + 1f;
            HangarDepartureLane.Tick();
            Assert.True(HangarDepartureLane.IsJammed(airbase));

            // Clear the jam badge when the aircraft clears its anchor.
            aircraft.transform.position = new UnityEngine.Vector3(500f, 0f, 0f);
            HangarDepartureLane.Tick();
            Assert.False(HangarDepartureLane.IsJammed(airbase));
            Assert.False(HangarDepartureLane.IsJammed(null));
        }

        [Fact]
        public void MovingCarrierOrFloatingOriginDoesNotPretendThePlaneClearedThePad()
        {
            var airbase = new Airbase();
            var hangar = new Hangar();
            var aircraft = new Aircraft();
            var owner = new object();
            Assert.True(HangarDepartureLane.Reserve(airbase, hangar, owner));
            HangarDepartureLane.Track(owner, aircraft);

            hangar.Spawn.position = new UnityEngine.Vector3(-10000f, 0f, 5000f);
            aircraft.transform.position = new UnityEngine.Vector3(-9990f, 0f, 5000f);
            HangarDepartureLane.Tick();
            Assert.False(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void HandingTheLaneToTheWingMemberKeepsTheFieldHeld()
        {
            // Transfer the purchase order's field claim to the member until airborne release.
            var airbase = new Airbase();
            var order = new object();
            var member = new object();
            var aircraft = new Aircraft();

            HangarDepartureLane.Reserve(airbase, new Hangar(), order);
            HangarDepartureLane.Track(order, aircraft);
            Assert.True(HangarDepartureLane.Transfer(order, member));

            // Only the new owner may release the transferred claim.
            HangarDepartureLane.Release(order);
            Assert.False(HangarDepartureLane.IsFree(airbase));
            HangarDepartureLane.Release(member);
            Assert.True(HangarDepartureLane.IsFree(airbase));
        }

        [Fact]
        public void TransferOfAnUnheldLaneReportsFailureRatherThanInventingOne()
        {
            Assert.False(HangarDepartureLane.Transfer(new object(), new object()));
            Assert.False(HangarDepartureLane.Transfer(null, new object()));
            Assert.False(HangarDepartureLane.Transfer(new object(), null));
        }
    }
}
