using System;

namespace WingCommand
{
    /// <summary>
    /// Maps third-party behaviour IDs to pilot-state factories. Each member caches its states;
    /// built-in behaviours are handled directly by <see cref="WingMember.EnterBehaviour"/>.
    /// </summary>
    public static class WingBehaviourCatalog
    {
        private static readonly BehaviourFactoryRegistry<Aircraft, PilotBaseState> factories =
            new BehaviourFactoryRegistry<Aircraft, PilotBaseState>();

        /// <summary>
        /// Register a behaviour. The factory is handed the aircraft and called once per
        /// wingman, the first time that wingman flies this registration. Replacing an ID
        /// starts a new registration lifetime: existing members rebuild their state when
        /// they next resolve that behaviour, including when it is already active.
        ///
        /// It takes an <c>Aircraft</c> rather than the mod's own wingman record, which stays
        /// internal. A behaviour is a way of flying an aeroplane and the aeroplane is what it
        /// needs; deciding *when* to fly it belongs to a reflex, and a reflex is handed the
        /// whole situation.
        /// </summary>
        public static void Register(string behaviourId, Func<Aircraft, PilotBaseState> factory)
        {
            factories.Register(behaviourId, factory);
            WingAi.RestoreBehaviour(behaviourId);
        }

        /// <summary>Remove a behaviour. True when one was actually removed.</summary>
        public static bool Unregister(string behaviourId) => factories.Remove(behaviourId);

        internal static bool IsAvailable(string behaviourId) => factories.Find(behaviourId) != null;

        /// <summary>Whether this member's cached state still belongs to the live registration.</summary>
        internal static bool IsCurrent(WingMember member, string behaviourId) =>
            member != null && !string.IsNullOrEmpty(behaviourId) &&
            member.HasCurrentCachedBehaviour(behaviourId, factories.Find(behaviourId));

        /// <summary>
        /// Switch the member onto a registered behaviour. False when nothing is registered
        /// under that id, which lets the caller fall back to the standing order rather than
        /// leaving the aircraft flying whatever it was before.
        /// </summary>
        internal static bool TryEnter(WingMember member, string behaviourId)
        {
            if (member == null || string.IsNullOrEmpty(behaviourId)) return false;
            BehaviourFactoryRegistry<Aircraft, PilotBaseState>.Registration registration = factories.Find(behaviourId);
            if (registration == null)
            {
                member.AcknowledgeMissingRegisteredBehaviour(behaviourId);
                return false;
            }

            try
            {
                PilotBaseState state = member.CachedBehaviour(behaviourId, registration);
                if (state == null)
                {
                    factories.RemoveIfCurrent(behaviourId, registration);
                    member.AcknowledgeMissingRegisteredBehaviour(behaviourId);
                    return false;
                }

                // An already active state is still a successfully resolved behavior.
                // SwitchTo's false means "no transition", not "factory unavailable".
                member.SwitchToRegistered(state);
                return true;
            }
            catch (Exception e)
            {
                // Same discipline as a faulting reflex: a third party's broken behaviour
                // degrades itself and hands the wingman back to its order, rather than
                // throwing out of the wing's update loop.
                Plugin.Logger.LogWarning(
                    $"[Wing] behaviour '{behaviourId}' failed to start: {e.GetType().Name} - {e.Message}");
                factories.RemoveIfCurrent(behaviourId, registration);
                // Observe that this attempt supplied no usable state. If the factory
                // installed a successor before failing, that live registration differs
                // from this absence and triggers the next member control update.
                member.AcknowledgeMissingRegisteredBehaviour(behaviourId);
                return false;
            }
        }

        /// <summary>Drop every registration for explicit teardown; mission changes retain factories.</summary>
        public static void Clear() => factories.Clear();
    }
}
