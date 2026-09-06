namespace WingCommand
{
    /// <summary>
    /// The extra job a wingman is working while it holds its formation slot.
    /// </summary>
    public enum SlotTask
    {
        /// <summary>Just fly the slot.</summary>
        None,

        /// <summary>Run the jammer pod against the designated unit.</summary>
        Jam,

        /// <summary>Legacy ID retained for binary compatibility; Splash now flies an attack run.</summary>
        Splash,
    }
}
