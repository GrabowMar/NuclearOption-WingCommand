namespace WingCommand
{
    internal partial class WingMember
    {
        public bool AutoRefit { get; set; }
        public bool PatrolRoute => taskQueue.Repeat;

        public bool CanPatrolRoute
        {
            get
            {
                if (!Alive || IsSurface || RefitPending || taskQueue.Count < 2) return false;
                foreach (WingDirective leg in taskQueue)
                    if (leg.Order != WingOrder.MoveToPoint || !leg.HasPoint) return false;
                foreach (WingDirective leg in taskQueue)
                    if (!leg.SameIntentAs(taskQueue[0])) return true;
                return false;
            }
        }

        public bool SetPatrolRoute(bool enabled)
        {
            if (enabled && !CanPatrolRoute) return false;
            taskQueue.SetRepeat(enabled);
            TacticalMapOverlay.Invalidate();
            return true;
        }

        private bool CanResumeAfterRefit(WingDirective directive)
        {
            switch (directive.Order)
            {
                case WingOrder.ReturnToBase:
                case WingOrder.Maneuver:
                case WingOrder.FallBack:
                    return false;
                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                case WingOrder.JamTarget:
                    return directive.Target != null && !directive.Target.disabled &&
                           directive.Target.NetworkHQ != null && directive.Target.NetworkHQ != Aircraft.NetworkHQ &&
                           WingOrderCatalog.CanApply(this, directive.Order);
                default:
                    return WingOrderCatalog.CanApply(this, directive.Order);
            }
        }

        private bool CombatStoresEmpty
        {
            get
            {
                if (Ammo > 0 || Aircraft?.weaponStations == null) return false;
                foreach (WeaponStation station in Aircraft.weaponStations)
                    if (station != null && !station.Cargo && station.FullAmmo > 0) return true;
                return false;
            }
        }
    }
}
