using UnityEngine;

namespace WingCommand
{
    /// <summary>One AI aircraft under the player's command, plus the slot it holds.</summary>
    internal class WingMember
    {
        public readonly Aircraft Aircraft;
        public readonly Pilot Pilot;
        public int Slot;

        /// <summary>Distance to the assigned slot, in metres. Diagnostic only.</summary>
        public float SlotError;

        public WingOrder Order { get; private set; } = WingOrder.Formation;

        private readonly FormationFlyState formationState;
        private WingRegistry owner;

        public WingMember(WingRegistry owner, Aircraft aircraft, Pilot pilot, int slot)
        {
            this.owner = owner;
            Aircraft = aircraft;
            Pilot = pilot;
            Slot = slot;
            formationState = new FormationFlyState(this);
        }

        public Aircraft Leader => owner?.Leader;

        /// <summary>The rest of the wing, for separation steering.</summary>
        public System.Collections.Generic.IReadOnlyList<WingMember> Siblings =>
            owner != null ? owner.Members : null;

        public bool Alive =>
            Aircraft != null && !Aircraft.disabled &&
            Pilot != null && !Pilot.dead && !Pilot.ejected;

        public string Name => Aircraft != null ? Aircraft.unitName : "(gone)";

        public void Apply(WingOrder order)
        {
            Order = order;
            switch (order)
            {
                case WingOrder.Formation:
                    formationState.BoostRejoin();
                    Pilot.SwitchState(formationState);
                    break;

                case WingOrder.Engage:
                    SwitchToCombat();
                    break;

                case WingOrder.ReturnToBase:
                    SwitchToLanding();
                    break;
            }

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} -> {order}");
        }

        /// <summary>
        /// Give control back to the stock combat AI. Used both for an explicit Engage
        /// order and for automatic breaks (leader lost, mutual support).
        /// </summary>
        public void ReleaseToCombat(string reason)
        {
            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} releasing to combat AI: {reason}");

            Order = WingOrder.Engage;
            OnLeash = false;
            SwitchToCombat();
        }

        /// <summary>True while this member is off the wing on a leashed engagement.</summary>
        public bool OnLeash { get; private set; }

        /// <summary>
        /// Break formation to fight, but stay tethered. Unlike a plain Engage order this
        /// is temporary: <see cref="CheckLeash"/> pulls the member back once the fight
        /// takes it too far from the leader. Follows the Falcon BMS model, where an attack
        /// order means acquire, fire, then rejoin — not leave for good.
        /// </summary>
        public void BreakToEngage(string reason)
        {
            if (OnLeash || Order != WingOrder.Formation) return;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} breaking to engage: {reason}");

            OnLeash = true;
            Order = WingOrder.Engage;
            SwitchToCombat();
        }

        /// <summary>
        /// Called each frame while off the wing. Returns the member to formation once it
        /// strays past the leash radius, so an Aggressive wing never simply disperses.
        /// </summary>
        public void CheckLeash()
        {
            if (!OnLeash || !Alive) return;

            Aircraft leader = Leader;
            if (leader == null)
            {
                OnLeash = false;
                return;
            }

            float leash = Plugin.Config2.LeashRadius.Value;
            if (FastMath.SquareDistance(Aircraft.GlobalPosition(), leader.GlobalPosition()) < leash * leash)
                return;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} past leash - rejoining");

            OnLeash = false;
            Apply(WingOrder.Formation);
        }

        private void SwitchToCombat()
        {
            if (Pilot == null) return;

            if (Pilot.AICombatState != null)
                Pilot.SwitchState(Pilot.AICombatState);
            else if (Pilot.AIHeloCombatState != null)
                Pilot.SwitchState(Pilot.AIHeloCombatState);
            else
                Plugin.Logger.LogWarning($"[Wing] {Name} has no combat state to return to.");
        }

        private void SwitchToLanding()
        {
            if (Pilot == null) return;

            if (Pilot.AILandingState != null)
                Pilot.SwitchState(Pilot.AILandingState);
            else if (Pilot.AIHeloLandingState != null)
                Pilot.SwitchState(Pilot.AIHeloLandingState);
            else
                SwitchToCombat();
        }
    }

    internal enum WingOrder
    {
        Formation,
        Engage,
        ReturnToBase,
    }
}
