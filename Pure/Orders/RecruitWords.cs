namespace WingCommand
{
    /// <summary>What a recruit order answers (spec WMC program §5 RECRUIT): who joined, and why the rest did not (review P4 I3).</summary>
    internal static class RecruitWords
    {
        /// <summary>The ack when at least one joined; null when nobody did (the order is refused with <see cref="Refusal"/>).</summary>
        public static string Ack(int joined, int asked, string first, string reason)
        {
            if (joined <= 0) return null;
            if (joined >= asked) return joined == 1 ? first + " joins the wing" : joined + " aircraft join the wing";
            return joined + " of " + asked + " joined" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason);
        }

        public static string Refusal(string reason) =>
            // WMC rebuild C4: taking command of a flying faction aircraft is ADOPT (RECRUIT means pilots, on WING).
            string.IsNullOrEmpty(reason) ? "No friendly aircraft to adopt" : "Cannot adopt: " + reason;
    }
}
