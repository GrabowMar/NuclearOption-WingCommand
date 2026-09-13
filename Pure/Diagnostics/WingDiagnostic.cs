namespace WingCommand
{
    internal enum WingDiagnosticEvent
    {
        PluginReady,
        PatchesInstalled,
        MissionStarted,
        WingReset,
        AiSettingsChanged,
        VerboseLoggingChanged,
    }

    /// <summary>Allowlisted diagnostic payload: no strings or arbitrary objects can enter the export.</summary>
    internal sealed class WingDiagnostic
    {
        internal WingDiagnostic(WingDiagnosticEvent kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        internal WingDiagnosticEvent Kind { get; }
        internal int Value { get; }

        public override string ToString()
        {
            switch (Kind)
            {
                case WingDiagnosticEvent.PluginReady: return "Plugin ready";
                case WingDiagnosticEvent.PatchesInstalled: return $"Harmony patches installed: count={Value}";
                case WingDiagnosticEvent.MissionStarted: return $"Mission started: performanceMode={Value != 0}";
                case WingDiagnosticEvent.WingReset: return $"Wing reset: previousMemberCount={Value}";
                case WingDiagnosticEvent.AiSettingsChanged: return $"AI settings changed: sharpTurns={(Value & 1) != 0}, targetSpreading={(Value & 2) != 0}, missileWarningRepair={(Value & 4) != 0}";
                case WingDiagnosticEvent.VerboseLoggingChanged: return $"Verbose logging enabled={Value != 0}";
                default: return "Unknown mod diagnostic";
            }
        }
    }
}
