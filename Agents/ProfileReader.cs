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
            return n;
        }
    }
}
