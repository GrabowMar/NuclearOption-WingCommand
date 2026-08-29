#pragma warning disable 0649

/// <summary>
/// Display hints for BepInEx ConfigurationManager, read by duck-typing on the class name,
/// so this needs no assembly reference and costs nothing when ConfigurationManager is not
/// installed. This is the trimmed copy of the template the plugin ships for the purpose —
/// only the fields actually used are kept.
///
/// It exists because the settings window shows every bound key at once, and most of these
/// are set once and never touched again. Marking those advanced leaves a short list of the
/// handful worth reaching for in a session.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Higher sorts higher within a category. Defaults to 0.</summary>
    public int? Order;

    /// <summary>Hide unless the user ticks "Advanced settings" or searches for it.</summary>
    public bool? IsAdvanced;

    /// <summary>Hide retired compatibility keys entirely from ConfigurationManager.</summary>
    public bool? Browsable;
}
