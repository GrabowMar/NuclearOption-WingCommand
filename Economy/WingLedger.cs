using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>What calls cost (spec M3 §5, <see cref="CallCost"/>): each wingman is quoted before it spawns and
    /// charged when it does (the local player's allocation; one airframe of the faction's stock where the game does not
    /// draw it itself), refunded when its launch is dropped, and refunded again when it comes home to the reserve (the
    /// game puts the airframe back in stock). Squadron/SandboxFreeCalls calls for free.</summary>
    internal static class WingLedger
    {
        private static readonly Dictionary<PersistentID, float> paid = new Dictionary<PersistentID, float>();

        /// <summary>This mission's totals (automation reads them).</summary>
        public static float Charged { get; private set; }
        public static float Refunded { get; private set; }

        /// <summary>A launch in flight between its charge and its adoption.</summary>
        internal struct Charge
        {
            public CallQuote Quote;
            public FactionHQ Hq;
            public AircraftDefinition Definition;
        }

        public static void Reset()
        {
            paid.Clear();
            Charged = Refunded = 0f;
        }

        public static CallQuote Quote(AircraftDefinition definition, FactionHQ hq, bool viaHangar, bool sandbox)
        {
            GameManager.GetLocalPlayer(out Player player);
            return CallCost.Quote(definition != null ? definition.value : 0f, player != null ? player.Allocation : 0f,
                hq != null && definition != null ? hq.GetUnitSupply(definition) : 0, sandbox, viaHangar);
        }

        public static Charge Take(CallQuote quote, FactionHQ hq, AircraftDefinition definition)
        {
            if (quote.Charge > 0f && GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(-quote.Charge);
                Charged += quote.Charge;
            }
            if (quote.TakeStock && hq != null) hq.ModifyUnitSupply(definition, -1);
            return new Charge { Quote = quote, Hq = hq, Definition = definition };
        }

        /// <summary>The launch never became a wingman: allocation and stock back.</summary>
        public static void Refund(Charge charge)
        {
            if (charge.Quote.Charge > 0f && GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(charge.Quote.Charge);
                Refunded += charge.Quote.Charge;
            }
            if (charge.Quote.TakeStock && charge.Hq != null) charge.Hq.ModifyUnitSupply(charge.Definition, 1);
        }

        /// <summary>The aircraft joined the wing: what it cost is remembered for its return.</summary>
        public static void Joined(Aircraft aircraft, Charge charge)
        {
            if (aircraft != null && charge.Quote.Charge > 0f) paid[aircraft.persistentID] = charge.Quote.Charge;
        }

        /// <summary>Home in the reserve: the allocation it cost comes back (the game restocks the airframe).</summary>
        public static void Returned(Aircraft aircraft)
        {
            if (aircraft == null || !paid.TryGetValue(aircraft.persistentID, out float cost)) return;
            paid.Remove(aircraft.persistentID);
            if (GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(cost);
                Refunded += cost;
            }
            Plugin.Logger.LogInfo($"[Economy] {aircraft.definition.unitName} home: {CallCost.Money(cost)} back");
        }

        /// <summary>Lost or handed to the game's AI: nothing comes back.</summary>
        public static void Forget(Aircraft aircraft)
        {
            if (aircraft != null) paid.Remove(aircraft.persistentID);
        }
    }
}
