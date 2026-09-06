using System;
using Xunit;

namespace UnityEngine
{
    public static partial class Time
    {
        public static float unscaledTime;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public sealed class Transform
    {
        public Vector3 position;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
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

    internal static class Plugin
    {
        internal static readonly TestLogger Logger = new TestLogger();
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
        public void ADelayedPlaneCannotReleaseItsOccupiedSpawnLaneOnATimer()
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
    }
}
