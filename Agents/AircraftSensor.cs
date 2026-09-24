using UnityEngine;

namespace WingCommand
{
    /// <summary>Reads one aircraft into <see cref="AircraftState"/> through the Pure <see cref="AircraftSensorCore"/>.
    /// One instance per aircraft; read it once per physics tick.</summary>
    internal sealed class AircraftSensor
    {
        private readonly AircraftSensorCore core = new AircraftSensorCore();
        private Aircraft gateOf;
        private bool hasGate, alwaysOn;
        private float gateSpeed, gateAlt;
        private RotorShaft[] rotors;

        public AircraftState Read(Aircraft a, float dt)
        {
            RawAircraftSample r = Sample(a);
            if (!ReferenceEquals(a, gateOf))
            {
                // The fly-by-wire gate is per airframe; the leader's sensor is reused when the player respawns.
                gateOf = a;
                hasGate = GameAccess.TryReadFbwGate(a, out gateSpeed, out gateAlt);
                alwaysOn = a.GetControlsFilter() is HeloControlsFilter;   // no speed or height gate (native C4)
                rotors = RotorsOf(a);
            }
            r.RotorRpm = RotorRpm(rotors);
            r.FbwAlwaysOn = alwaysOn;
            r.HasFbwGate = hasGate;
            r.FbwGateMinSpeed = gateSpeed;
            r.FbwGateMinRadarAlt = gateAlt;
            return core.Read(r, dt);
        }

        /// <summary>The aircraft's rotor shafts: each registers itself in <c>Aircraft.engines</c> (the game finds them through
        /// its parts; they are not all under the aircraft's transform, so GetComponentsInChildren found none and the rotor
        /// speed read 0 — the collective governor never acted, in game 2026-09-24).</summary>
        private static RotorShaft[] RotorsOf(Aircraft a)
        {
            var found = new System.Collections.Generic.List<RotorShaft>();
            if (a.engines != null)
                foreach (IEngine e in a.engines)
                    if (e is RotorShaft r && r != null) found.Add(r);
            if (found.Count == 0) found.AddRange(a.GetComponentsInChildren<RotorShaft>(true));
            return found.ToArray();
        }

        /// <summary>The mean rotor speed over nominal of the aircraft's rotor shafts (0 without any).</summary>
        private static float RotorRpm(RotorShaft[] shafts)
        {
            if (shafts == null || shafts.Length == 0) return 0f;
            float sum = 0f;
            int n = 0;
            foreach (RotorShaft shaft in shafts)
                if (shaft != null)
                {
                    sum += shaft.GetRPMRatio();
                    n++;
                }
            return n > 0 ? sum / n : 0f;
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
