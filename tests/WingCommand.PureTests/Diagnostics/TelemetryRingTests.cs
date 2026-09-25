using System.Globalization;
using Xunit;

namespace WingCommand.PureTests
{
    public class TelemetryRingTests
    {
        [Fact]
        public void RingKeepsTheNewestRowsOldestFirst()
        {
            var ring = new TelemetryRing();
            for (int i = 0; i < TelemetryRing.Capacity + 5; i++) ring.Push(new TelemetryRow { Time = i });
            Assert.Equal(TelemetryRing.Capacity, ring.Count);
            Assert.Equal(5f, ring[0].Time);
            Assert.Equal(TelemetryRing.Capacity + 4f, ring[ring.Count - 1].Time);
            ring.Clear();
            Assert.Equal(0, ring.Count);
        }

        [Fact]
        public void RowCopiesThePilotsView()
        {
            AircraftState s = TestStates.Flying(new Vec3(1f, 2000f, 3f), new Vec3(0f, 0f, 200f));
            s.BankDeg = 12f;
            s.P = 30f;
            var intent = new FlightIntent { Ref = new RefState(new Vec3(5f, 2010f, 7f), Vec3.Zero, Vec3.Zero) };
            var cmd = new AttitudeCommand { BankDeg = 20f, Nz = 1.5f };
            var output = new ControlOutput { Pitch = 0.1f, Roll = -0.2f, Throttle = 0.6f };
            var report = new BindingReport { BankBy = ConstraintId.Terrain, GcasActive = true };
            TelemetryRow r = TelemetryRows.From(4.5f, 2, s, intent, cmd, output, report, 0.8f, BehaviourId.StationKeep,
                new Vec3(4f, 2000f, 3f));
            Assert.Equal(4.5f, r.Time);
            Assert.Equal(2, r.Member);
            Assert.Equal(new Vec3(5f, 2010f, 7f), r.RefPos);
            Assert.Equal(20f, r.BankCmdDeg);
            Assert.Equal(1.5f, r.NzCmd);
            Assert.Equal(30f, r.RollRate);
            Assert.Equal(3f, r.SlotError, 3);
            Assert.Equal(0.8f, r.Sigma);
            Assert.Equal((byte)BehaviourId.StationKeep, r.Behaviour);
            Assert.Equal((byte)ConstraintId.Terrain, r.BankBy);
            Assert.True(r.Gcas);
        }

        [Fact]
        public void RowRecordsTheMembersRoleAfterItsBehaviour()
        {
            var pilot = new FormationPilot(0, AirframeClass.Rotary);
            TelemetryRow r = TelemetryRows.From(1f, 0, TestStates.Flying(Vec3.Zero, new Vec3(0f, 0f, 50f)), pilot, Vec3.Zero);
            Assert.Equal((byte)pilot.Roles.Current, r.Role);
            Assert.Contains(",behaviour,role,", TelemetryCsv.Header);
        }

        [Fact]
        public void RowRecordsTheRotorSpeedAndTheTiltLimitLast()
        {
            // M2d: the in-game helicopter sank with its rotor drooping; the trace must show it.
            var pilot = new FormationPilot(0, AirframeClass.Rotary);
            AircraftState s = TestStates.Flying(Vec3.Zero, new Vec3(0f, 0f, 50f));
            s.RotorRpm = 0.96f;
            TelemetryRow r = TelemetryRows.From(1f, 0, s, pilot, Vec3.Zero);
            Assert.Equal(0.96f, r.RotorRpm);
            Assert.Equal(1f, r.TiltScale);
            Assert.EndsWith(",airbrake,rotor_rpm,tilt_scale", TelemetryCsv.Header);
        }

        [Fact]
        public void CsvHasTheSharedHeaderAndInvariantNumbers()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                var ring = new TelemetryRing();
                ring.Push(new TelemetryRow { Time = 1.25f, Member = 1, Tas = 200.5f, Gcas = true, RotorRpm = 0.5f, TiltScale = 1f });
                string csv = TelemetryCsv.Write(ring);
                string[] lines = csv.Split('\n');
                Assert.Equal(TelemetryCsv.Header, lines[0]);
                Assert.StartsWith("1.25,1,", lines[1]);
                Assert.Contains(",200.5,", lines[1]);
                Assert.EndsWith(",1,0,0,0.5,1", lines[1]);
                Assert.Equal(TelemetryCsv.Header.Split(',').Length, lines[1].Split(',').Length);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
