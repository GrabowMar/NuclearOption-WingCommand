namespace WingCommand
{
    /// <summary>The native numbers an <see cref="AirframeProfile"/> is derived from, and the airframe's class from its
    /// pilot type (a VTOL is reported as fixed-wing; callers refuse it by <see cref="IsVtol"/>).</summary>
    internal static class ProfileReader
    {
        public static AirframeClass ClassOf(Aircraft a)
        {
            Pilot pilot = a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
            if (pilot == null) return AirframeClass.FixedWing;
            switch (pilot.pilotType)
            {
                case Pilot.PilotType.Helo: return AirframeClass.Rotary;
                case Pilot.PilotType.Tiltwing: return AirframeClass.Tiltwing;
                default: return AirframeClass.FixedWing;
            }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<AeroPart, int> airfoilOf = FieldOrNull();
        private static readonly UnityEngine.Vector3[] sweep = new UnityEngine.Vector3[46];

        private static HarmonyLib.AccessTools.FieldRef<AeroPart, int> FieldOrNull()
        {
            try
            {
                return HarmonyLib.AccessTools.FieldRefAccess<AeroPart, int>("airfoil");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>The 1-g stall speed its wings give at its weight now (m/s EAS; 0 when unreadable): the airflow swept from −5° to
        /// 40° across the pitch plane, each part's lift as the game computes it (CL(α)·S along the lift normal, decompiled
        /// AeroJob_Math), its vertical share summed; the best total is the wings' ΣCL·S (<see cref="LiftStall"/>). Fins and
        /// the fuselage lift sideways or not at all and add nothing.</summary>
        public static float LiftStallSpeed(Aircraft a)
        {
            if (a == null || a.partLookup == null) return 0f;
            AircraftParameters parameters = a.GetAircraftParameters();
            UnityEngine.Quaternion toBody = UnityEngine.Quaternion.Inverse(a.transform.rotation);
            float best = 0f;
            for (int k = 0; k < sweep.Length; k++)
            {
                float alpha = (k - 5) * UnityEngine.Mathf.Deg2Rad;
                // Nose up by alpha to the airflow: the air meets the aircraft from ahead and below.
                UnityEngine.Vector3 v = new UnityEngine.Vector3(0f, -UnityEngine.Mathf.Sin(alpha), UnityEngine.Mathf.Cos(alpha));
                float total = 0f;
                foreach (UnitPart part in a.partLookup)
                {
                    if (!(part is AeroPart aero) || aero.WingArea <= 0f || aero.LiftNormal == null) continue;
                    UnityEngine.Quaternion r = toBody * aero.LiftNormal.rotation;
                    UnityEngine.Vector3 local = UnityEngine.Quaternion.Inverse(r) * v;
                    float partAlpha = UnityEngine.Mathf.Atan2(local.y, local.z);
                    int foil = airfoilOf != null ? airfoilOf(aero) : -1;
                    float cl = foil >= 0 && parameters != null && parameters.airfoils != null && foil < parameters.airfoils.Length
                        && parameters.airfoils[foil]?.liftCoef != null
                        ? parameters.airfoils[foil].liftCoef.Evaluate(partAlpha)
                        : 1.8f * UnityEngine.Mathf.Sin(5f * partAlpha);
                    UnityEngine.Vector3 lift = -UnityEngine.Vector3.Cross(v, r * UnityEngine.Vector3.right).normalized;
                    total += UnityEngine.Vector3.Dot(lift, UnityEngine.Vector3.up) * cl * aero.WingArea;
                }
                if (total > best) best = total;
            }
            return LiftStall.Speed(a.GetMass(), best);
        }

        /// <summary>The class of a type, from its prefab (fixed-wing when unknown).</summary>
        public static AirframeClass ClassOf(AircraftDefinition d)
        {
            Aircraft template = d != null && d.unitPrefab != null ? d.unitPrefab.GetComponent<Aircraft>() : null;
            return template != null ? ClassOf(template) : AirframeClass.FixedWing;
        }

        public static bool IsVtol(Aircraft a) =>
            a.pilots != null && a.pilots.Length > 0 && a.pilots[0] != null && a.pilots[0].pilotType == Pilot.PilotType.VTOL;

        public static ProfileInputs Read(Aircraft a)
        {
            AircraftDefinition def = a.definition;
            AircraftParameters p = a.GetAircraftParameters();
            var n = new ProfileInputs
            {
                UnitName = def != null ? def.unitName : null,
                Class = ClassOf(a),
                GLimit = p.aircraftGLimit,
                PidReferenceAirspeed = p.PIDReferenceAirspeed,
                MaxSpeed = p.maxSpeed,
                CornerSpeed = p.cornerSpeed,
                TakeoffSpeed = p.takeoffSpeed,
                LandingSpeed = p.landingSpeed,
                CruiseThrottle = p.cruiseThrottle,
                MaxRadius = a.maxRadius,
                PublishedStallKmh = def != null && def.aircraftInfo != null ? def.aircraftInfo.stallSpeed : 0f,
            };
            if (GameAccess.TryReadFlyByWire(a, out float roll, out float g, out float corner))
            {
                n.FbwMaxRollAngularVel = roll;
                n.FbwGLimit = g;
                n.FbwCornerSpeed = corner;
            }
            if (GameAccess.TryReadHeloFlyByWire(a, out UnityEngine.Vector3 rates, out float heloG))
            {
                n.HeloPitchRate = rates.x;
                n.HeloYawRate = rates.y;
                n.HeloRollRate = rates.z;
                n.HeloGLimit = heloG;
            }
            if (GameAccess.TryReadHoverThrottle(a, out float hover)) n.HoverCollective = hover;
            if (GameAccess.TryReadLandingGear(a, out float steerLock, out float steerRate, out float wheelbase))
            {
                n.SteerLockDeg = steerLock;
                n.SteerRateDps = steerRate;
                n.WheelbaseM = wheelbase;
            }
            if (def != null) n.SpanM = def.width;
            return n;
        }
    }
}
