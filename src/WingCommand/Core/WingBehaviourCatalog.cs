using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// Maps a behaviour id onto the pilot state that flies it.
    ///
    /// The one place that knows about both halves of the design. <see cref="WingArbiter"/>
    /// and every reflex deal only in strings, which is what keeps them engine-free and
    /// testable; this is where a string becomes a <c>PilotBaseState</c> and touches Unity.
    ///
    /// The mod's own five behaviours are handled directly in
    /// <see cref="WingMember.EnterBehaviour"/> because they need the member's own cached
    /// state objects. This registry exists for the other direction: a plugin that ships a
    /// new reflex almost always needs to ship the behaviour it selects too, and without a
    /// seam here that reflex could only ever pick from behaviours we happened to think of.
    /// </summary>
    public static class WingBehaviourCatalog
    {
        private static readonly Dictionary<string, Func<Aircraft, PilotBaseState>> factories =
            new Dictionary<string, Func<Aircraft, PilotBaseState>>(StringComparer.Ordinal);

        /// <summary>
        /// Register a behaviour. The factory is handed the aircraft and called once per
        /// wingman, the first time that wingman flies the behaviour; the state is cached
        /// from then on, the same lifetime the built-in states get.
        ///
        /// It takes an <c>Aircraft</c> rather than the mod's own wingman record, which stays
        /// internal. A behaviour is a way of flying an aeroplane and the aeroplane is what it
        /// needs; deciding *when* to fly it belongs to a reflex, and a reflex is handed the
        /// whole situation.
        /// </summary>
        public static void Register(string behaviourId, Func<Aircraft, PilotBaseState> factory)
        {
            if (string.IsNullOrEmpty(behaviourId))
                throw new ArgumentException("A behaviour needs an id.", nameof(behaviourId));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            factories[behaviourId] = factory;
        }

        /// <summary>Remove a behaviour. True when one was actually removed.</summary>
        public static bool Unregister(string behaviourId) => factories.Remove(behaviourId);

        /// <summary>
        /// Switch the member onto a registered behaviour. False when nothing is registered
        /// under that id, which lets the caller fall back to the standing order rather than
        /// leaving the aircraft flying whatever it was before.
        /// </summary>
        internal static bool TryEnter(WingMember member, string behaviourId)
        {
            if (member == null || string.IsNullOrEmpty(behaviourId)) return false;
            if (!factories.TryGetValue(behaviourId, out Func<Aircraft, PilotBaseState> factory))
                return false;

            try
            {
                PilotBaseState state = member.CachedBehaviour(behaviourId, factory);
                if (state == null) return false;

                member.Pilot.SwitchState(state);
                return true;
            }
            catch (Exception e)
            {
                // Same discipline as a faulting reflex: a third party's broken behaviour
                // degrades itself and hands the wingman back to its order, rather than
                // throwing out of the wing's update loop.
                Plugin.Logger.LogWarning(
                    $"[Wing] behaviour '{behaviourId}' failed to start: {e.GetType().Name} - {e.Message}");
                Unregister(behaviourId);
                return false;
            }
        }

        /// <summary>Drop every registration. Called at mission start.</summary>
        public static void Clear() => factories.Clear();
    }
}
