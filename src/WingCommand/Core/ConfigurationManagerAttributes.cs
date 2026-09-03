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
    /// <summary>Override the key name shown in ConfigurationManager.</summary>
    public string DispName;

    /// <summary>Higher sorts higher within a category. Defaults to 0.</summary>
    public int? Order;

    /// <summary>Hide unless the user ticks "Advanced settings" or searches for it.</summary>
    public bool? IsAdvanced;

    /// <summary>
    /// Draw this entry's value area yourself, in the settings window's IMGUI pass.
    ///
    /// What turns a setting into an action: a bound entry nobody ever reads, whose editor
    /// is a button. Used for the debug spawn, which is a thing you do once rather than a
    /// value you keep, and therefore belongs where the other cheats already are rather
    /// than taking permanent space on a cockpit panel.
    ///
    /// The signature has to match ConfigurationManager's own field exactly — it is bound
    /// by reflection on the name — so this is not <c>Action&lt;object&gt;</c>.
    /// </summary>
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;

    /// <summary>Suppress the reset-to-default button, which means nothing for an action.</summary>
    public bool? HideDefaultButton;

    /// <summary>
    /// Suppress the key name, giving a custom drawer the whole row. Used by the Debug
    /// banner, which is a sentence rather than a setting and has no name worth showing.
    /// </summary>
    public bool? HideSettingName;
}
