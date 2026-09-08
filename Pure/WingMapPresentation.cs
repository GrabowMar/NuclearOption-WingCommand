namespace WingCommand
{
    /// <summary>Persistent wing identity and command scope, independent of weapon targeting so clearing
    /// targets leaves both intact.</summary>
    internal readonly struct WingMapPresentation
    {
        public enum OutlineKind { None, Member, Target }

        public OutlineKind Outline { get; }
        public bool CommandBrackets { get; }

        private WingMapPresentation(OutlineKind outline, bool commandBrackets)
        {
            Outline = outline;
            CommandBrackets = commandBrackets;
        }

        public static WingMapPresentation Resolve(bool isWingMember, bool isWingTarget,
            bool highlightWing, bool highlightTargets, bool tacticalActive, bool commandSelected)
        {
            // Wing membership takes precedence over stale weapon-target selection.
            OutlineKind outline = isWingMember
                ? (highlightWing ? OutlineKind.Member : OutlineKind.None)
                : (isWingTarget && highlightTargets ? OutlineKind.Target : OutlineKind.None);
            return new WingMapPresentation(outline,
                isWingMember && tacticalActive && commandSelected);
        }
    }
}
