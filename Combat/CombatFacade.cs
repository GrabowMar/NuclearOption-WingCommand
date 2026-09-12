namespace WingCommand
{
    /// <summary>Single entry point other modules use to reach Combat. Nested classes mirror the internal
    /// subsystem they forward to one-for-one (no renaming, no behavior change) so Combat's internals can
    /// be restructured without touching callers in Core/Flight/Personnel/Comms/Ui.</summary>
    internal static class CombatFacade
    {
        internal static class Weapons
        {
            public static float FireInterval(Aircraft aircraft) => WingWeapons.FireInterval(aircraft);
            public static bool EngageSpecific(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
                WingWeapons.EngageSpecific(aircraft, pilot, target, maxRange);
            public static bool EngageMassed(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
                WingWeapons.EngageMassed(aircraft, pilot, target, maxRange);
            public static bool CanStillEngage(Aircraft aircraft, Unit target) =>
                WingWeapons.CanStillEngage(aircraft, target);
            public static float BombReleaseFloor(Aircraft aircraft, Unit target) =>
                WingWeapons.BombReleaseFloor(aircraft, target);
            public static bool HasJammer(Aircraft aircraft) => WingWeapons.HasJammer(aircraft);
            public static bool EngageJammer(Aircraft aircraft, Pilot pilot, Unit target) =>
                WingWeapons.EngageJammer(aircraft, pilot, target);
            public static bool ReleaseCargo(Aircraft aircraft, Pilot pilot) => WingWeapons.ReleaseCargo(aircraft, pilot);
            public static bool InterceptMissiles(Aircraft aircraft, Pilot pilot, Aircraft protectee) =>
                WingWeapons.InterceptMissiles(aircraft, pilot, protectee);
            public static int GetGuidedAmmo(Aircraft aircraft) => WingWeapons.GetGuidedAmmo(aircraft);
            public static int RecommendedAttackers(Aircraft aircraft, Unit target) =>
                WingWeapons.RecommendedAttackers(aircraft, target);
            public static bool HasMissileDefence(Aircraft aircraft) => WingWeapons.HasMissileDefence(aircraft);
            public static void ClearTurretTargets(Aircraft aircraft) => WingWeapons.ClearTurretTargets(aircraft);
            public static Unit NearestThreatTo(Aircraft protectee, float range) =>
                WingWeapons.NearestThreatTo(protectee, range);
            public static Unit NextExpendTarget(Aircraft aircraft, GlobalPosition near, float radius, Unit exclude) =>
                WingWeapons.NextExpendTarget(aircraft, near, radius, exclude);
        }

        internal static class Roe
        {
            public static WingRoe Current => RoeRules.Current;
            public static WingRoe Next(WingRoe roe) => RoeRules.Next(roe);
            public static string Label(WingRoe roe) => RoeRules.Label(roe);
            public static string Hint(WingRoe roe) => RoeRules.Hint(roe);
            public static void EnsureFree(WingRegistry wing) => RoeRules.EnsureFree(wing);
            public static float SpacingScale(WingRoe roe) => RoeRules.SpacingScale(roe);
            public static float ExplicitOrderRange() => RoeRules.ExplicitOrderRange();
        }

        internal static class Countermeasures
        {
            public static void Initialise() => CountermeasureAccess.Initialise();
            public static bool TryFindExpendable(CountermeasureManager manager, string seekerType,
                                                 out int index, out string reason) =>
                CountermeasureAccess.TryFindExpendable(manager, seekerType, out index, out reason);
        }

        internal static class Tactical
        {
            public static void Reset() => TacticalCoordinator.Reset();
            public static void ReleaseSelection(Aircraft owner) => TacticalCoordinator.ReleaseSelection(owner);
            public static void Release(Aircraft owner) => TacticalCoordinator.Release(owner);
        }
    }
}
