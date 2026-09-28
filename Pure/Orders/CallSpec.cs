namespace WingCommand
{
    /// <summary>A requisition as data (spec WMC rebuild §SUPPLY; supply-critic §1.1): which airframe (the game's jsonKey), from
    /// which field (its SavedAirbase unique name), flown by which pilot (callsign), with which fit (null: AUTO, the game's pick;
    /// <see cref="YourLoadout"/>; else a LOADOUT template id) and fuel (0: full, else the fraction). Unset fields keep the
    /// radial call's choices.</summary>
    internal struct CallSpec
    {
        public const string YourLoadout = "YOURS";

        public string Airframe, Field, Pilot, Fit;
        public float Fuel;

        public bool IsSet => Airframe != null || Field != null || Pilot != null || Fit != null || Fuel != 0f;
    }
}
