namespace WingCommand
{
    /// <summary>
    /// Persistent map identity and tactical command scope. Native weapon-target selection
    /// is intentionally absent: clearing targets must change neither of these markings.
    /// </summary>
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
            // A member remains a member even if a stale weapon assignment targets it.
            OutlineKind outline = isWingMember
                ? (highlightWing ? OutlineKind.Member : OutlineKind.None)
                : (isWingTarget && highlightTargets ? OutlineKind.Target : OutlineKind.None);
            return new WingMapPresentation(outline,
                isWingMember && tacticalActive && commandSelected);
        }
    }
}
