using UnityEngine;

namespace WingCommand
{
    /// <summary>Reads one aircraft into <see cref="AircraftState"/> through the Pure <see cref="AircraftSensorCore"/>.
    /// One instance per aircraft; read it once per physics tick.</summary>
    internal sealed class AircraftSensor
    {
        private readonly AircraftSensorCore core = new AircraftSensorCore();
        private Aircraft gateOf;
        private bool hasGate;
        private float gateSpeed, gateAlt;

        public AircraftState Read(Aircraft a, float dt)
        {
            RawAircraftSample r = Sample(a);
            if (!ReferenceEquals(a, gateOf))
            {
                // The fly-by-wire gate is per airframe; the leader's sensor is reused when the player respawns.
                gateOf = a;
                hasGate = GameAccess.TryReadFbwGate(a, out gateSpeed, out gateAlt);
            }
            r.HasFbwGate = hasGate;
            r.FbwGateMinSpeed = gateSpeed;
            r.FbwGateMinRadarAlt = gateAlt;
            return core.Read(r, dt);
        }

        public static RawAircraftSample Sample(Aircraft a)
        {
            Transform t = a.cockpit.xform;
            Rigidbody rb = a.CockpitRB();
            return new RawAircraftSample
            {
                Pos = a.GlobalPosition().ToVec3(),
                Vel = rb.velocity.ToVec3(),
                Fwd = t.forward.ToVec3(),
                Up = t.up.ToVec3(),
                Right = t.right.ToVec3(),
                AngularVelocity = t.InverseTransformDirection(rb.angularVelocity).ToVec3(),
                Wind = GameAccess.WindOf(a).ToVec3(),
                AirDensity = a.airDensity,
                RadarAlt = a.radarAlt,
                GroundSpeed = a.speed,
                Throttle = a.GetInputs().throttle,
            };
        }
    }
}
