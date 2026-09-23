namespace WingCommand
{
    /// <summary>The native numbers an <see cref="AirframeProfile"/> is derived from.</summary>
    internal static class ProfileReader
    {
        public static ProfileInputs Read(Aircraft a)
        {
            AircraftDefinition def = a.definition;
            AircraftParameters p = a.GetAircraftParameters();
            var n = new ProfileInputs
            {
                UnitName = def != null ? def.unitName : null,
                Class = AirframeClass.FixedWing,
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
            return n;
        }
    }
}
