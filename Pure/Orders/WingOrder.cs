namespace WingCommand
{
    /// <summary>Every command a player can give the wing (spec WMC program §3.2): the radial, hotkeys, HOTAS, WMC, the map
    /// and a client all send one of these; the host's executor applies it.</summary>
    internal enum OrderKind : byte
    {
        FormUp, Task, Rtb, Refit, Engage, Attack, Splash, BreakOff, BogeyDope, ClearSix, LandHere, TakeOff, DeliverCargo,
        Rescue, EscortMe, EscortTarget, Release, Dismiss, Recruit, Call, SetShape, NextShape, NextFamily, SetSpacing, Stack,
        Afterburner, SetDoctrine, NextDoctrine, SkipLeg, RenameElement,
        // WMC rebuild R3: one member, confirmed (Flag), ordered by the player.
        Eject,
        // WMC rebuild R3: one doctrine setting at the scope's level (Number = DoctrineAxis, Text = the value's word).
        SetOverride,
        // WMC rebuild R3: a reaction maneuver for the scope (Number = ReactionKind).
        Maneuver,
    }

    internal enum ScopeKind : byte { Wing, Element, Members }

    /// <summary>Who issued an order (spec WMC rebuild §PLAN, §BEHAVIOUR): the player (and a client), a running plan, or a
    /// behaviour rule. The executor logs the task change with the matching reason.</summary>
    internal enum OrderSource : byte { Player, Plan, Rule }

    /// <summary>Who an order is for: the whole wing, one element (0 = A), or members by aircraft persistent id.</summary>
    internal struct WingScope
    {
        public const int MaxMembers = 8, MaxElements = 4;
        public ScopeKind Kind;
        public int Element;
        public uint[] Members;

        public static WingScope Wing => default;
        public static WingScope OfElement(int element) => new WingScope { Kind = ScopeKind.Element, Element = element };
        public static WingScope OfMembers(params uint[] ids) => new WingScope { Kind = ScopeKind.Members, Members = ids };
    }

    /// <summary>One command with its bounded arguments: a task (points, loop, seconds), target units by persistent id, one
    /// number (stack metres, call count, spacing preset), one short text (shape id, doctrine line, element name), one flag
    /// (afterburner allowed).</summary>
    // Units, Number, Text and Flag are set by the builders and a client's decoder (P2 T6, P12).
#pragma warning disable CS0649
    internal sealed class WingOrder
    {
        // MaxText fits a custom doctrine's eight-value config text (61 characters at most, review P2 I6; the wire's
        // string limit is 64).
        public const int MaxUnits = 8, MaxText = 64;
        public OrderKind Kind;
        public WingScope Scope;
        public WingTask Task;
        public uint[] Units;
        public float Number;
        public string Text;
        public bool Flag;
        /// <summary>A requisition's choices (Call; spec WMC rebuild §SUPPLY): unset keeps the radial call's.</summary>
        public CallSpec Call;
        public OrderSource Source;

        /// <summary>The reason a task change this order makes is logged with.</summary>
        public TransitionReason Reason =>
            Source == OrderSource.Plan ? TransitionReason.Plan : Source == OrderSource.Rule ? TransitionReason.Rule : TransitionReason.Commanded;

        public static WingOrder Of(OrderKind kind, WingScope scope = default) => new WingOrder { Kind = kind, Scope = scope };

        public static WingOrder Tasked(WingTask task, WingScope scope = default) =>
            new WingOrder { Kind = OrderKind.Task, Task = task, Scope = scope };
    }
#pragma warning restore CS0649
}
