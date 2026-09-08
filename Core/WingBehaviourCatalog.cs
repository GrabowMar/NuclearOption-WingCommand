using System;

namespace WingCommand
{
 /// <summary>Registers extension behaviour factories. Members cache states; WingMember.EnterBehaviour
 /// handles built-ins directly.</summary>
    public static class WingBehaviourCatalog
    {
        private static readonly BehaviourFactoryRegistry<Aircraft, PilotBaseState> factories =
            new BehaviourFactoryRegistry<Aircraft, PilotBaseState>();

     /// <summary>Register a factory called once per member per registration lifetime. Replacing an ID
     /// rebuilds cached states on next resolution, even if active. Factories receive Aircraft; reflexes
     /// decide when behaviours run.</summary>
        public static void Register(string behaviourId, Func<Aircraft, PilotBaseState> factory)
        {
            factories.Register(behaviourId, factory);
            WingAi.RestoreBehaviour(behaviourId);
        }

     /// <summary>Remove the ID; return true if registered.</summary>
        public static bool Unregister(string behaviourId) => factories.Remove(behaviourId);

        internal static bool IsAvailable(string behaviourId) => factories.Find(behaviourId) != null;

     /// <summary>Whether the cached state matches the current registration lifetime.</summary>
        internal static bool IsCurrent(WingMember member, string behaviourId) =>
            member != null && !string.IsNullOrEmpty(behaviourId) &&
            member.HasCurrentCachedBehaviour(behaviourId, factories.Find(behaviourId));

     /// <summary>Enter a registered behaviour. Return false when unavailable so the caller can resume
     /// the standing order.</summary>
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

                // An already-active state resolves successfully even when SwitchTo needs no transition.
                member.SwitchToRegistered(state);
                return true;
            }
            catch (Exception e)
            {
                // Isolate extension failures and return control to the standing order.
                Plugin.Logger.LogWarning(
                    $"[Wing] behaviour '{behaviourId}' failed to start: {e.GetType().Name} - {e.Message}");
                factories.RemoveIfCurrent(behaviourId, registration);
                // Record the failed resolution. A successor installed by the failed factory must
                // trigger another control update.
                member.AcknowledgeMissingRegisteredBehaviour(behaviourId);
                return false;
            }
        }

     /// <summary>Clear registrations during explicit teardown; retain them across missions.</summary>
        public static void Clear() => factories.Clear();
    }
}
