using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>One telemetry sample of one aircraft: what it did, what it was told, and what bound it. The engine
    /// recorder (20 Hz), the step test (60 Hz) and FlightSim traces share this schema.</summary>
    internal struct TelemetryRow
    {
        public float Time;
        public int Member;
        public Vec3 Pos, Vel, RefPos;
        public float BankDeg, BankCmdDeg, Nz, NzCmd, Tas, RollRate, AccelAlong;
        public float Throttle, Pitch, Roll, Sigma, SlotError;
        public byte Behaviour, Role, BankBy, NzBy, VerticalBy;
        public bool Gcas, Collision, Airbrake;
        /// <summary>Rotor speed over nominal (0 without a rotor) and the rotary nose-down tilt limit in use (1 when
        /// speed has not given way to height, and for fixed wings).</summary>
        public float RotorRpm, TiltScale;
    }

    /// <summary>Fixed ring of <see cref="Capacity"/> rows (120 s at 20 Hz), oldest first. Push never allocates.</summary>
    internal sealed class TelemetryRing
    {
        public const int Capacity = 2400;
        private readonly TelemetryRow[] items = new TelemetryRow[Capacity];
        private int next;

        public int Count { get; private set; }

        public TelemetryRow this[int index] => items[(next - Count + index + Capacity) % Capacity];

        public void Push(in TelemetryRow r)
        {
            items[next] = r;
            next = (next + 1) % Capacity;
            if (Count < Capacity) Count++;
        }

        public void Clear()
        {
            next = 0;
            Count = 0;
        }
    }

    internal static class TelemetryRows
    {
        public static TelemetryRow From(float time, int member, in AircraftState s, FormationPilot pilot, Vec3 slotPos)
        {
            TelemetryRow r = From(time, member, s, pilot.LastIntent, pilot.Pipeline.LastAttitude, pilot.LastOutput,
                pilot.Pipeline.Report, pilot.LastRejoin.Sigma, pilot.Mind.Current, slotPos, pilot.Roles.Current);
            r.TiltScale = TiltScaleOf(pilot.Pipeline);
            return r;
        }

        private static float TiltScaleOf(IFlightPipeline pipeline) =>
            pipeline is RotaryPipeline rotary ? rotary.Controller.TiltScale
            : pipeline is TiltwingPipeline tilt && tilt.Mode == TiltwingMode.Rotary ? tilt.Rotary.Controller.TiltScale
            : 1f;

        public static TelemetryRow From(float time, int member, in AircraftState s, in FlightIntent intent,
            in AttitudeCommand cmd, in ControlOutput output, in BindingReport report, float sigma, BehaviourId behaviour,
            Vec3 slotPos, Role role = Role.Slot)
        {
            float speed = s.Vel.Length;
            return new TelemetryRow
            {
                Time = time, Member = member, Pos = s.Pos, Vel = s.Vel, RefPos = intent.Ref.Pos,
                BankDeg = s.BankDeg, BankCmdDeg = cmd.BankDeg, Nz = s.Nz, NzCmd = cmd.Nz, Tas = s.Tas, RollRate = s.P,
                AccelAlong = speed > 1f ? Vec3.Dot(s.Acc, s.Vel / speed) : 0f,
                Throttle = output.Airbrake ? 0f : output.Throttle, Pitch = output.Pitch, Roll = output.Roll,
                Sigma = sigma, SlotError = (slotPos - s.Pos).Length,
                Behaviour = (byte)behaviour, Role = (byte)role, BankBy = (byte)report.BankBy, NzBy = (byte)report.NzBy,
                VerticalBy = (byte)report.VerticalBy, Gcas = report.GcasActive, Collision = report.CollisionActive,
                Airbrake = output.Airbrake, RotorRpm = s.RotorRpm, TiltScale = 1f,
            };
        }
    }

    internal static class TelemetryCsv
    {
        public const string Header =
            "time,member,x,y,z,vx,vy,vz,ref_x,ref_y,ref_z,bank,bank_cmd,nz,nz_cmd,tas,roll_rate,accel_along," +
            "throttle,pitch,roll,sigma,slot_error,behaviour,role,bank_by,nz_by,vertical_by,gcas,collision,airbrake," +
            "rotor_rpm,tilt_scale";

        public static string Write(TelemetryRing ring)
        {
            var sb = new StringBuilder(Header.Length + ring.Count * 200);
            sb.Append(Header);
            for (int i = 0; i < ring.Count; i++)
            {
                sb.Append('\n');
                AppendRow(sb, ring[i]);
            }
            return sb.ToString();
        }

        public static void AppendRow(StringBuilder sb, in TelemetryRow r)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.Append(r.Time.ToString("0.###", c)).Append(',').Append(r.Member.ToString(c));
            Vec(sb, r.Pos, c);
            Vec(sb, r.Vel, c);
            Vec(sb, r.RefPos, c);
            Num(sb, r.BankDeg, c);
            Num(sb, r.BankCmdDeg, c);
            Num(sb, r.Nz, c);
            Num(sb, r.NzCmd, c);
            Num(sb, r.Tas, c);
            Num(sb, r.RollRate, c);
            Num(sb, r.AccelAlong, c);
            Num(sb, r.Throttle, c);
            Num(sb, r.Pitch, c);
            Num(sb, r.Roll, c);
            Num(sb, r.Sigma, c);
            Num(sb, r.SlotError, c);
            sb.Append(',').Append(r.Behaviour.ToString(c)).Append(',').Append(r.Role.ToString(c)).Append(',').Append(r.BankBy.ToString(c))
              .Append(',').Append(r.NzBy.ToString(c)).Append(',').Append(r.VerticalBy.ToString(c))
              .Append(',').Append(r.Gcas ? '1' : '0').Append(',').Append(r.Collision ? '1' : '0')
              .Append(',').Append(r.Airbrake ? '1' : '0');
            Num(sb, r.RotorRpm, c);
            Num(sb, r.TiltScale, c);
        }

        private static void Vec(StringBuilder sb, Vec3 v, CultureInfo c)
        {
            Num(sb, v.X, c);
            Num(sb, v.Y, c);
            Num(sb, v.Z, c);
        }

        private static void Num(StringBuilder sb, float v, CultureInfo c) => sb.Append(',').Append(v.ToString("0.###", c));
    }
}
