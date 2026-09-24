namespace WingCommand
{
    /// <summary>What a wing event says on the radio: its class, the chatter line's name and the dedupe scope.</summary>
    internal struct RadioCall
    {
        public RadioClass Class;
        public string Name;
        public bool WingWide;
    }

    /// <summary>Spec M7 §1.3: wing events to radio calls. Every event not listed says nothing.</summary>
    internal static class RadioCalls
    {
        public static bool For(in WingEvent e, out RadioCall call)
        {
            call = default;
            switch (e.Kind)
            {
                case WingEventKind.BehaviourChanged:
                    switch (e.Reason)
                    {
                        case TransitionReason.MissileInbound: return Make(RadioClass.Emergency, "DEFENDING", false, out call);
                        case TransitionReason.MissileClear: return Make(RadioClass.Tactical, "DEFENSIVECLEAR", false, out call);
                        default: return false;
                    }
                case WingEventKind.Engaged: return Make(RadioClass.Tactical, "ENGAGING", true, out call);
                case WingEventKind.Disengaged:
                    switch (e.Reason)
                    {
                        case TransitionReason.Winchester: return Make(RadioClass.Status, "WINCHESTER", false, out call);
                        case TransitionReason.Outnumbered: return Make(RadioClass.Tactical, "FALLINGBACK", true, out call);
                        case TransitionReason.Leash:
                        case TransitionReason.NoTarget:
                        case TransitionReason.Commanded: return Make(RadioClass.Status, "REJOINING", true, out call);
                        default: return false;
                    }
                case WingEventKind.Joker: return Make(RadioClass.Status, "JOKER", false, out call);
                case WingEventKind.Bingo: return Make(RadioClass.Status, "BINGO", false, out call);
                case WingEventKind.FallingBehind: return Make(RadioClass.Status, "FALLINGBEHIND", false, out call);
                case WingEventKind.GcasActivated: return Make(RadioClass.Emergency, "PULLUP", false, out call);
                case WingEventKind.CollisionEmergency: return Make(RadioClass.Emergency, "BREAKOFF", false, out call);
                case WingEventKind.AnchorLost: return Make(RadioClass.Tactical, "LEADLOST", true, out call);
                case WingEventKind.Airborne: return Make(RadioClass.Status, "AIRBORNEREJOINING", false, out call);
                case WingEventKind.Landed: return Make(RadioClass.Chatter, "DOWN", false, out call);
                case WingEventKind.LandingFailed: return Make(RadioClass.Status, "GOAROUND", false, out call);
                case WingEventKind.TaskCompleted: return Make(RadioClass.Status, "TASKDONE", true, out call);
                case WingEventKind.TaskFailed: return Make(RadioClass.Status, "UNABLEORDER", true, out call);
                default: return false;
            }
        }

        private static bool Make(RadioClass c, string name, bool wing, out RadioCall call)
        {
            call = new RadioCall { Class = c, Name = name, WingWide = wing };
            return true;
        }
    }

    /// <summary>Reads each event of a <see cref="WingEventRing"/> once, by its running total; events overwritten before
    /// they were read are skipped.</summary>
    internal struct EventCursor
    {
        public long Seen;

        public bool Next(WingEventRing ring, out WingEvent e)
        {
            e = default;
            long oldest = ring.Total - ring.Count;
            if (Seen < oldest) Seen = oldest;
            if (Seen >= ring.Total) return false;
            e = ring[(int)(Seen - oldest)];
            Seen++;
            return true;
        }
    }
}
