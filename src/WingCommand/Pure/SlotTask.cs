namespace WingCommand
{
    /// <summary>
    /// The extra job a wingman is working while it holds its formation slot.
    ///
    /// Both of these are orders that carry a target but are flown from the slot rather than
    /// as a break-away run, so the aircraft is in the formation state either way.
    /// <see cref="FormationFlyState"/> used to work out which of its behaviours to run by
    /// reading the standing order directly, which is how one state came to have four jobs
    /// and no way to tell them apart from its own name.
    /// </summary>
    public enum SlotTask
    {
        /// <summary>Just fly the slot.</summary>
        None,

        /// <summary>Run the jammer pod against the designated unit.</summary>
        Jam,

        /// <summary>Work every effective store into the designated unit.</summary>
        Splash,
    }
}
