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
        /// <summary>Sandbox wingmen until they come home (review R4a): the game's restock of one is taken back.</summary>
        private static readonly HashSet<PersistentID> sandboxed = new HashSet<PersistentID>();

        /// <summary>This mission's totals (automation reads them).</summary>
        public static float Charged { get; private set; }
        public static float Refunded { get; private set; }

        /// <summary>A launch in flight between its charge and its adoption.</summary>
        internal struct Charge
        {
            public CallQuote Quote;
            public FactionHQ Hq;
            public AircraftDefinition Definition;
            /// <summary>Launched through a hangar (the game draws the airframe from the faction's stock itself).</summary>
            public bool ViaHangar;
            /// <summary>A sandbox launch: it never moves the faction's stock (<see cref="CallCost.Refund"/>).</summary>
            public bool Sandbox;
        }

        public static void Reset()
        {
            paid.Clear();
            sandboxed.Clear();
            Charged = Refunded = 0f;
        }

        /// <param name="price">What it costs (SUPPLY's quote, the surcharge included); negative: the list value.</param>
        /// <param name="held">Airframes of the type in the HANGAR store: they count as stock and one is used first (never
        /// drawn from the faction by hand).</param>
        public static CallQuote Quote(AircraftDefinition definition, FactionHQ hq, bool viaHangar, bool sandbox, float price = -1f, int held = 0)
        {
            GameManager.GetLocalPlayer(out Player player);
            CallQuote q = CallCost.Quote(price >= 0f ? price : definition != null ? definition.value : 0f, player != null ? player.Allocation : 0f,
                (hq != null && definition != null ? hq.GetUnitSupply(definition) : 0) + held, sandbox, viaHangar);
            if (held > 0) q.TakeStock = false;
            return q;
        }

        public static Charge Take(CallQuote quote, FactionHQ hq, AircraftDefinition definition, bool viaHangar, bool sandbox = false)
        {
            if (quote.Charge > 0f && GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(-quote.Charge);
                Charged += quote.Charge;
            }
            if (quote.TakeStock && hq != null) hq.ModifyUnitSupply(definition, -1);
            return new Charge { Quote = quote, Hq = hq, Definition = definition, ViaHangar = viaHangar, Sandbox = sandbox };
        }

        /// <summary>The launch never became a wingman (<see cref="CallCost.Refund"/>): the allocation back; the airframe
        /// back by hand when it never appeared, or the aircraft back to the reserve when it did (<paramref name="aircraft"/>
        /// null: it never appeared).</summary>
        public static void Refund(Charge charge, Aircraft aircraft)
        {
            bool spawned = (object)aircraft != null;
            RefundPlan plan = CallCost.Refund(spawned, spawned && (aircraft == null || aircraft.disabled), charge.ViaHangar, charge.Quote.TakeStock,
                charge.Sandbox);
            if (plan.Allocation && charge.Quote.Charge > 0f && GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(charge.Quote.Charge);
                Refunded += charge.Quote.Charge;
            }
            if (plan.StockByHand != 0 && charge.Hq != null) charge.Hq.ModifyUnitSupply(charge.Definition, plan.StockByHand);
            if (plan.ReturnAircraft) aircraft.ReturnToInventory();
        }

        /// <summary>The aircraft joined the wing: what it cost is remembered for its return.</summary>
        public static void Joined(Aircraft aircraft, Charge charge)
        {
            if (aircraft != null && charge.Quote.Charge > 0f) paid[aircraft.persistentID] = charge.Quote.Charge;
            if (aircraft != null && charge.Sandbox) sandboxed.Add(aircraft.persistentID);
        }

        /// <summary>Home in the reserve: the allocation it cost comes back (the game restocks the airframe); a sandbox one's
        /// restock is taken back, as the sandbox never moves the faction's stock. ponytail: if a player waits on the type, the
        /// game hands the restock to them and this takes one from stock instead (multiplayer only).</summary>
        public static void Returned(Aircraft aircraft)
        {
            if (aircraft == null) return;
            if (sandboxed.Remove(aircraft.persistentID) && aircraft.NetworkHQ != null && aircraft.definition != null)
                aircraft.NetworkHQ.ModifyUnitSupply(aircraft.definition, CallCost.HomeStock(sandbox: true));
            if (!paid.TryGetValue(aircraft.persistentID, out float cost)) return;
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
            if (aircraft == null) return;
            paid.Remove(aircraft.persistentID);
            sandboxed.Remove(aircraft.persistentID);
        }
    }
}
