using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>What calling one wingman costs (spec M3 §5).</summary>
    internal struct CallQuote
    {
        public bool Allowed;
        public float Charge;
        public bool TakeStock;
        public string Reason;
    }

    /// <summary>How a launch that never joined the wing is settled (review M3c I5).</summary>
    internal struct RefundPlan
    {
        public bool Allocation, ReturnAircraft;
        public int StockByHand;
    }

    /// <summary>A call costs the airframe's list value from the player's allocation and one airframe of the faction's
    /// stock (a hangar spawn draws the stock itself, as the game does; a service-point spawn has it taken by hand). No
    /// stock, or too little allocation, refuses the call with the reason; the sandbox calls for free.</summary>
    internal static class CallCost
    {
        public static CallQuote Quote(float price, float allocation, int stock, bool sandbox, bool viaHangar)
        {
            if (sandbox) return new CallQuote { Allowed = true };
            if (stock <= 0) return new CallQuote { Reason = "none of that type in the faction's stock" };
            if (allocation < price)
                return new CallQuote { Reason = $"needs {Money(price)}, the allocation holds {Money(allocation)}" };
            return new CallQuote { Allowed = true, Charge = price, TakeStock = !viaHangar };
        }

        /// <summary>Taking command of a faction aircraft already flying costs <paramref name="rate"/> (0–1) of its list
        /// value, once per aircraft (<paramref name="paid"/>: it was bought before); no stock changes hands. Too little
        /// allocation refuses with the reason; the sandbox recruits for free.</summary>
        public static CallQuote Recruit(float value, float rate, float allocation, bool sandbox, bool paid)
        {
            float price = sandbox || paid ? 0f : value * Math.Max(0f, Math.Min(1f, rate));
            if (allocation < price)
                return new CallQuote { Reason = $"needs {Money(price)}, the allocation holds {Money(allocation)}" };
            return new CallQuote { Allowed = true, Charge = price };
        }

        /// <summary>A launch that never joined the wing: the allocation always comes back. An aircraft that never appeared
        /// gives back the airframe drawn for it (the game draws it for a hangar, the call for a service point); one that
        /// appeared intact goes back to the reserve, where the game restocks it; one destroyed first is gone.</summary>
        public static RefundPlan Refund(bool spawned, bool destroyed, bool viaHangar, bool tookStock) => new RefundPlan
        {
            Allocation = true,
            StockByHand = !spawned && (viaHangar || tookStock) ? 1 : 0,
            ReturnAircraft = spawned && !destroyed,
        };

        /// <summary>Credits, as the panel reads them (values are the game's millions).</summary>
        public static string Money(float value) => Credits.Text(value);
    }
}
