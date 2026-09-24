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

        public static string Money(float value) =>
            value >= 1_000_000f ? "$" + (value / 1_000_000f).ToString("0.0", CultureInfo.InvariantCulture) + "M"
            : "$" + value.ToString("0", CultureInfo.InvariantCulture);
    }
}
