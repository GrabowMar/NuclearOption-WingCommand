namespace WingCommand
{
    /// <summary>Why a store can or cannot fly from a station (the game's WeaponChecker rules, in its order).</summary>
    internal enum StoreVerdict : byte
    {
        Ok, Missing, NotOnStation, Disabled, EventOnly, Restricted, Blocked, NuclearNotYet, NuclearRank, NotFromShip, Warheads,
    }

    /// <summary>One store option as the game describes it.</summary>
    internal struct MountFacts
    {
        /// <summary>This build knows the store; the station can carry it.</summary>
        public bool Known, OnStation;
        public bool Disabled, EventContent, Restricted, Blocked, Nuclear, Strategic, ShipRearm;
        public int Pylons, Ammo;
    }

    /// <summary>What the mission allows now (all replicated to clients).</summary>
    internal struct MissionFacts
    {
        public bool EventContent, TacticalOpen, StrategicOpen;
        public float TacticalMinRank, StrategicMinRank;
        public int Rank;
    }

    /// <summary>What may be fitted, checked three times with one set of words (research loadout-rules §4): at edit time on LOADOUT (the
    /// build, the mission, the faction and the rank), and at quote and launch time with the field too (not from a ship, and warheads
    /// counted over the whole fit in station order, as the game's own menu counts them). A nuclear store before escalation or below the
    /// rank can still be fitted — a template is a preset and both change in a mission — but launches empty until they clear.</summary>
    internal static class StoreRules
    {
        public static StoreVerdict Check(in MountFacts m, in MissionFacts mission)
        {
            if (!m.Known) return StoreVerdict.Missing;
            if (!m.OnStation) return StoreVerdict.NotOnStation;
            if (m.Disabled) return StoreVerdict.Disabled;
            if (m.EventContent && !mission.EventContent) return StoreVerdict.EventOnly;
            if (m.Restricted) return StoreVerdict.Restricted;
            if (m.Blocked) return StoreVerdict.Blocked;
            if (m.Nuclear)
            {
                if (!mission.TacticalOpen) return StoreVerdict.NuclearNotYet;
                if (mission.Rank < mission.TacticalMinRank) return StoreVerdict.NuclearRank;
                if (m.Strategic)
                {
                    if (!mission.StrategicOpen) return StoreVerdict.NuclearNotYet;
                    if (mission.Rank < mission.StrategicMinRank) return StoreVerdict.NuclearRank;
                }
            }
            return StoreVerdict.Ok;
        }

        /// <summary><see cref="Check"/> plus the field: no ship-rearm store from a ship, and warheads taken from
        /// <paramref name="warheadsLeft"/> in station order.</summary>
        public static StoreVerdict AtField(in MountFacts m, in MissionFacts mission, bool shipField, ref int warheadsLeft)
        {
            StoreVerdict v = Check(m, mission);
            if (v != StoreVerdict.Ok) return v;
            if (m.ShipRearm && shipField) return StoreVerdict.NotFromShip;
            if (!m.Nuclear) return StoreVerdict.Ok;
            int need = m.Pylons * m.Ammo;
            if (need > warheadsLeft) return StoreVerdict.Warheads;
            warheadsLeft -= need;
            return StoreVerdict.Ok;
        }

        /// <summary>Listed in the store popup at all (the game's own menu hides these).</summary>
        public static bool Offered(StoreVerdict v) =>
            v != StoreVerdict.Disabled && v != StoreVerdict.EventOnly && v != StoreVerdict.Missing && v != StoreVerdict.NotOnStation;

        /// <summary>May be fitted on a template.</summary>
        public static bool Pickable(StoreVerdict v) =>
            v == StoreVerdict.Ok || v == StoreVerdict.NuclearNotYet || v == StoreVerdict.NuclearRank;

        /// <summary>Launches as fitted.</summary>
        public static bool Flies(StoreVerdict v) => v == StoreVerdict.Ok;
    }
}
