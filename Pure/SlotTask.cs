namespace WingCommand
{
    /// <summary>Additional job performed while holding formation.</summary>
    public enum SlotTask
    {
        /// <summary>Station keeping without an additional job.</summary>
        None,

        /// <summary>Operate the jammer pod at the designation.</summary>
        Jam,

        /// <summary>Retained binary-compatible value; current Splash orders use attack runs.</summary>
        Splash,
    }
}
