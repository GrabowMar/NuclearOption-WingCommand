namespace WingCommand
{
    /// <summary>Describes how a selected station should begin an engagement. Turrets own their
    /// physical aiming and release gate, while fixed stations need an explicit fire command.</summary>
    internal enum StationFireAction
    {
        None,
        ArmNativeTurret,
        DirectFire,
    }

    /// <summary>Engine-free dispatch rule shared by runtime weapon control and regression tests.</summary>
    internal static class StationFirePolicy
    {
        /// <summary>Keep a turret designation long enough for native aim, lock, and line-of-sight
        /// checks even if the station is temporarily cooling down. Fixed stations can only fire when
        /// ready.</summary>
        public static StationFireAction Decide(bool hasLiveTarget, bool hasTurret, bool stationReady)
        {
            if (!hasLiveTarget) return StationFireAction.None;
            if (hasTurret) return StationFireAction.ArmNativeTurret;
            return stationReady ? StationFireAction.DirectFire : StationFireAction.None;
        }
    }
}
