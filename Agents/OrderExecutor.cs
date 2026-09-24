using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Spec WMC program §3.2: applies a <see cref="WingOrder"/> on the host and answers in words — the only place an
    /// order is refused. A task goes to the element its scope resolves to (detaching the selected members when needed);
    /// an immediate order acts on the members its scope names. <see cref="OrderResult.Reason"/> and
    /// <see cref="OrderResult.Ack"/> are the full text to show; an empty ack shows nothing (the radio said it).</summary>
    internal static class OrderExecutor
    {
        private static readonly List<Unit> units = new List<Unit>();

        public static OrderResult Execute(WingOrder o)
        {
            string invalid = OrderValidator.Check(o);
            if (invalid != null) return OrderResult.Refused("Wing cannot: " + invalid);
            WingService w = WingService.Instance;
            if (w?.Selection == null) return OrderResult.Refused("Wing Command is not ready");
            if (o.Kind == OrderKind.Task) return Task(w, o);
            // Review P2 I5: an order that reaches nobody is refused, and nobody moves.
            string nobody = w.Roster.Check(o.Scope);
            if (nobody != null) return OrderResult.Refused("Wing cannot: " + nobody);
            switch (o.Kind)
            {
                case OrderKind.FormUp: return FormUp(w, o.Scope, o.Reason);
                case OrderKind.SkipLeg:
                {
                    int e = ElementFor(w, o.Scope);
                    return w.SkipLeg(e) ? OrderResult.Acked("Next leg", e) : OrderResult.Refused("No route to skip");
                }
                case OrderKind.RenameElement:
                {
                    int e = ElementFor(w, o.Scope);
                    string why = w.Roster.Rename(e, o.Text);
                    return why == null ? OrderResult.Acked($"Element {ElementRoster.Letter(e)} is {w.Roster.Name(e)}", e)
                        : OrderResult.Refused("Cannot rename: " + why);
                }
            }
            return Immediate(w, o, Who(w, o.Scope));
        }

        private static OrderResult Task(WingService w, WingOrder o)
        {
            // A LAND or CARGO point needs a helicopter among those it goes to; refused before anyone detaches.
            if (Settles(o.Task, out bool cargo) && !w.CanSettle(Who(w, o.Scope), cargo, out string why))
                return OrderResult.Refused("Cannot land there: " + why);
            ScopeTarget t = w.Roster.Resolve(o.Scope);
            if (t.Element < 0) return OrderResult.Refused("Wing cannot: " + t.Reason);
            if (t.Everyone) MergeAll(w, o.Reason);
            OrderResult r = w.OrderElement(t.Element, o.Task, o.Reason);
            if (!r.Accepted)
            {
                // A detach only happens to carry an order: a refused one puts everyone back (review P2 m9).
                if (t.Detached) w.Roster.UndoDetach();
                return OrderResult.Refused("Wing cannot: " + r.Reason);
            }
            string who = t.Element == 0 ? "Wing" : w.Roster.Name(t.Element);
            return OrderResult.Acked($"{who}: {TaskWords(o.Task)}", t.Element);
        }

        private static bool Settles(WingTask t, out bool cargo)
        {
            cargo = false;
            bool any = false;
            if (t?.Points == null) return false;
            foreach (Waypoint p in t.Points)
            {
                if (p.Action == ArrivalAction.Cargo) cargo = any = true;
                else if (p.Action == ArrivalAction.Land) any = true;
            }
            return any;
        }

        private static string TaskWords(WingTask t)
        {
            if (t == null) return "forming up";
            switch (t.Kind)
            {
                case TaskKind.Move: return t.Scout ? "scouting" : "moving";
                case TaskKind.Route: return "flying the route";
                case TaskKind.Patrol: return "patrolling";
                case TaskKind.Orbit: return "orbiting";
                case TaskKind.Hold: return "holding";
                default: return "forming up";
            }
        }

        private static OrderResult FormUp(WingService w, in WingScope scope, TransitionReason reason)
        {
            if (scope.Kind == ScopeKind.Wing)
            {
                MergeAll(w, reason);
                w.FormUp(reason);
                return OrderResult.Acked("Form up", 0);
            }
            Func<WingMember, bool> who = Who(w, scope);
            // The members first: merging moves them to A, after which the element filter matches nobody (review P2 I4).
            var chosen = new List<WingMember>();
            foreach (WingMember m in w.Members)
                if (who(m)) chosen.Add(m);
            // ponytail: forming up some members brings their whole elements back to A.
            for (int e = 1; e < ElementRoster.MaxElements; e++)
                foreach (WingMember m in chosen)
                    if (w.ElementOf(m) == e)
                    {
                        w.MergeElement(e, reason);
                        break;
                    }
            if (scope.Kind == ScopeKind.Element && scope.Element == 0 && w.Planner.Active) w.OrderElement(0, WingTask.Form(), reason);
            Func<WingMember, bool> these = chosen.Contains;
            w.Disengage(these);
            w.TakeOff(these);
            foreach (WingMember m in chosen) m.Brain.FormUp(w.MissionTime, w.Events);
            return OrderResult.Acked(chosen.Count == 1 ? "Rejoining" : $"{chosen.Count} rejoining", 0);
        }

        private static OrderResult Immediate(WingService w, WingOrder o, Func<WingMember, bool> who)
        {
            switch (o.Kind)
            {
                case OrderKind.Rtb: return Recover(w, RecoveryIntent.Rtb, "returning to base", who);
                case OrderKind.Maneuver:
                {
                    var kind = (ReactionKind)(int)o.Number;
                    int n = w.React(kind, who, out string why);
                    return n > 0 ? OrderResult.Acked($"{n} {ReactionManeuver.Words(kind)}" + (why != null ? $" ({why})" : ""))
                        : OrderResult.Refused($"{ReactionManeuver.Label(kind)}: {why}");
                }
                case OrderKind.Eject:
                {
                    WingMember m = null;
                    foreach (WingMember x in w.Members)
                        if (who(x))
                        {
                            m = x;
                            break;
                        }
                    if (m == null) return OrderResult.Refused("Nobody to eject");
                    string name = (object)m.Aircraft != null ? WingPilotRoster.Of(m.Aircraft)?.Callsign : null;
                    if (string.IsNullOrEmpty(name)) name = "#" + m.Number;
                    string why = w.Eject(m);
                    return why == null ? OrderResult.Acked(name + " ejecting") : OrderResult.Refused($"Cannot eject {name}: {why}");
                }
                case OrderKind.Refit: return Recover(w, RecoveryIntent.Refit, "going to refit", who);
                case OrderKind.Engage:
                {
                    if (!w.MayEngage(out int hostiles, out int members))
                        return OrderResult.Refused($"Outnumbered {hostiles} to {members} - Engage again to fight anyway");
                    int n = w.Engage(who);
                    return n > 0 ? OrderResult.Acked($"{n} engaging") : OrderResult.Refused("Nobody can engage");
                }
                case OrderKind.Attack:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("No enemy target selected");
                    int n = w.Attack(units, who);
                    string plural = units.Count == 1 ? "" : "s";
                    return n > 0 ? OrderResult.Acked($"{n} attacking {units.Count} target{plural}") : OrderResult.Refused("Nobody can attack");
                }
                case OrderKind.Splash:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("Splash: select a target first");
                    int fired = w.Splash(units[0], out int capable, who);
                    if (fired > 0) WingRadioAudio.Play(WingRadioAudio.Earcon.Splash);
                    return fired > 0 ? OrderResult.Acked($"Splash: {fired} firing on {units[0].unitName}")
                        : OrderResult.Refused(capable > 0 ? "Splash: launchers not ready" : "Splash: nobody in range");
                }
                case OrderKind.BreakOff:
                {
                    int n = w.Disengage(who);
                    return n > 0 ? OrderResult.Acked($"{n} disengaging") : OrderResult.Refused("Nobody engaged");
                }
                case OrderKind.BogeyDope:
                {
                    if (w.Player == null) return OrderResult.Refused("Not flying");
                    // The radio says it; when it will not (radio off, or the line dropped), the answer is shown (review M7a I4).
                    return OrderResult.Acked(WingCommands.BogeyDopeCall(w, out string answer) ? "" : answer);
                }
                case OrderKind.ClearSix:
                {
                    if (w.Player == null) return OrderResult.Refused("Not flying");
                    int found = w.ClearMySix(out int engaged);
                    return found == 0 ? OrderResult.Acked("Your six is clear")
                        : engaged > 0 ? OrderResult.Acked($"{engaged} clearing your six ({found} bandits)") : OrderResult.Refused("Nobody can engage");
                }
                case OrderKind.LandHere:
                {
                    int n = w.LandHere(out string refusal, who);
                    return n > 0 ? OrderResult.Acked($"{n} landing here") : OrderResult.Refused("Cannot land here: " + refusal);
                }
                case OrderKind.TakeOff:
                {
                    int n = w.TakeOff(who);
                    return n > 0 ? OrderResult.Acked($"{n} lifting off") : OrderResult.Refused("Nobody is down");
                }
                case OrderKind.DeliverCargo:
                {
                    int n = w.DeliverCargo(out string refusal, who);
                    return n > 0 ? OrderResult.Acked($"Deliver cargo: {n} landing") : OrderResult.Refused("Deliver cargo: " + refusal);
                }
                case OrderKind.Rescue:
                {
                    // ponytail: the rescue picks the best-placed helicopter of the wing whatever the scope.
                    WingMember m = w.Rescue(out string result);
                    return m != null ? OrderResult.Acked("Rescue: " + result) : OrderResult.Refused("Rescue: " + result);
                }
                case OrderKind.EscortMe:
                    if (!w.Escorting) return OrderResult.Refused("Already on you");
                    w.SetEscort(null);
                    return OrderResult.Acked("Escorting you");
                case OrderKind.EscortTarget:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("No friendly to escort");
                    w.SetEscort(units[0]);
                    return OrderResult.Acked("Escorting " + units[0].unitName);
                }
                case OrderKind.Release:
                {
                    if (who == null) return OrderResult.Refused("Select who to release");
                    int n = 0;
                    foreach (WingMember m in w.Members)
                        if (!m.Released && who(m))
                        {
                            w.Release(m, "released by order");
                            n++;
                        }
                    return n > 0 ? OrderResult.Acked(n == 1 ? "Wingman released" : $"{n} wingmen released") : OrderResult.Refused("Nobody to release");
                }
                case OrderKind.Dismiss:
                    if (w.Members.Count == 0) return OrderResult.Refused("No wingmen to dismiss");
                    w.Dismiss();
                    return OrderResult.Acked("Wing dismissed");
                case OrderKind.Recruit:
                {
                    // Spec WMC program §5: every friendly selected on the map, in one order; one answer.
                    Units(o.Units);
                    int joined = 0, asked = 0;
                    string first = null, refusal = null;
                    foreach (Unit u in units)
                    {
                        if (!(u is Aircraft target)) continue;
                        asked++;
                        if (WingRecruitment.TryRecruit(w, target, out _, out string reason))
                        {
                            if (joined++ == 0) first = target.unitName;
                        }
                        else if (refusal == null) refusal = reason;
                    }
                    // Review P4 I3: the answer says who joined and why the rest did not.
                    return joined == 0 ? OrderResult.Refused(RecruitWords.Refusal(refusal))
                        : OrderResult.Acked(RecruitWords.Ack(joined, asked, first, refusal));
                }
                case OrderKind.Call: return Call(w, (int)o.Number);
                case OrderKind.SetShape:
                    return w.SetShape(o.Text, ElementFor(w, o.Scope)) ? OrderResult.Acked("") : OrderResult.Refused("No such shape: " + o.Text);
                case OrderKind.NextShape:
                    w.NextShape();
                    return OrderResult.Acked("");
                case OrderKind.NextFamily:
                    w.NextFamily();
                    return OrderResult.Acked("");
                case OrderKind.SetSpacing:
                    w.SetSpacing((SpacingPreset)(int)o.Number);
                    return OrderResult.Acked("");
                case OrderKind.Stack:
                    w.SetStack(o.Number);
                    return OrderResult.Acked(string.IsNullOrEmpty(o.Text) ? "Stack set" : o.Text);
                case OrderKind.Afterburner:
                    w.SetAfterburner(o.Flag);
                    return OrderResult.Acked(o.Flag ? "Gate: afterburner allowed" : "Buster: military power, no afterburner");
                case OrderKind.SetOverride:
                {
                    var axis = (DoctrineAxis)(int)o.Number;
                    WingDoctrine.TryAxisValue(axis, o.Text, out byte v);
                    return SetOverride(w, o.Scope, axis, v, who);
                }
                case OrderKind.SetDoctrine:
                {
                    if (!WingDoctrine.TryParse(o.Text, out WingDoctrine d)) return OrderResult.Refused("Unknown doctrine: " + o.Text);
                    if (o.Scope.Kind == ScopeKind.Members)
                    {
                        // Spec WMC rebuild R3: the named aircraft only, not the first one's whole element.
                        int set = 0;
                        foreach (WingMember m in w.Members)
                            if (!m.Released && who(m) && w.SetMemberDoctrine(m, d)) set++;
                        return set > 0 ? OrderResult.Acked($"{Aircraft(set)}: doctrine {d.PatternName}")
                            : OrderResult.Refused("Too many aircraft with their own doctrine");
                    }
                    int e = ElementFor(w, o.Scope);
                    w.SetDoctrine(d, e);
                    return OrderResult.Acked((e > 0 ? w.Roster.Name(e) + ": doctrine " : "Doctrine ") + d.PatternName);
                }
                case OrderKind.NextDoctrine:
                {
                    int e = ElementFor(w, o.Scope);
                    WingDoctrine d = e > 0 ? w.DoctrineOf(e).NextPattern() : w.NextDoctrine();
                    if (e > 0) w.SetDoctrine(d, e);
                    string what = d.Targets == TargetPolicy.Hold ? "holding fire"
                        : d.Targets == TargetPolicy.Cover ? "covering you"
                        : "targets of opportunity";
                    return OrderResult.Acked($"Doctrine {d.PatternName}: {what}");
                }
                default:
                    return OrderResult.Refused("Not supported yet");
            }
        }

        /// <summary>One doctrine setting at the scope's level (spec WMC rebuild R3): the wing, an element, or each named
        /// aircraft. The answer names those in scope that keep their own value, and those without a radar.</summary>
        private static OrderResult SetOverride(WingService w, in WingScope scope, DoctrineAxis axis, byte v, Func<WingMember, bool> who)
        {
            if (axis == DoctrineAxis.Weapons)
            {
                // Review Focus R3a 5: an aircraft without the weapon would get an empty station list and fly home "out of
                // ammo", so the order is refused naming it.
                string cannot = null;
                foreach (WingMember m in w.Members)
                {
                    if (m.Released || !m.Alive || (who != null && !who(m))) continue;
                    bool gun = false, missile = false;
                    Carries(m.Aircraft, ref gun, ref missile);
                    string why = WeaponsFilter.Refusal((WeaponsPolicy)v, gun, missile);
                    if (why != null) cannot = (cannot == null ? "" : cannot + ", ") + "#" + m.Number + " " + why;
                }
                if (cannot != null) return OrderResult.Refused("Wing cannot: " + cannot);
            }
            string level;
            if (scope.Kind == ScopeKind.Members)
            {
                if (!MemberDoctrines.PerAircraft(axis)) return OrderResult.Refused("Only targets, reach, weapons and radar can differ per aircraft");
                int set = 0;
                foreach (WingMember m in w.Members)
                    if (!m.Released && who(m) && w.SetMemberAxis(m, axis, v)) set++;
                if (set == 0) return OrderResult.Refused("Too many aircraft with their own doctrine");
                level = Aircraft(set);
            }
            else
            {
                int e = scope.Kind == ScopeKind.Element ? scope.Element : -1;
                w.SetAxis(e, axis, v);
                level = e < 0 ? "Wing" : w.Roster.Name(e);
            }
            // The game's AI keeps its current attack until its next choice: engaged members choose again now.
            if (axis == DoctrineAxis.Weapons || axis == DoctrineAxis.Radar) w.Rechoose(who);
            string ack = $"{level}: {AxisWord(axis)} {ValueWord(axis, v)}";
            foreach (WingMember m in w.Members)
            {
                if (m.Released || (who != null && !who(m))) continue;
                if (scope.Kind != ScopeKind.Members && w.KeepsOwn(m, axis)) ack += $", #{m.Number} keeps its own";
                else if (axis == DoctrineAxis.Radar && m.Aircraft != null && !(m.Aircraft.radar is Radar)) ack += $", #{m.Number} has no radar";
            }
            return OrderResult.Acked(ack);
        }

        private static void Carries(Aircraft a, ref bool gun, ref bool missile)
        {
            if (a == null || a.weaponStations == null) return;
            foreach (WeaponStation s in a.weaponStations)
            {
                if (s == null || s.Cargo || s.WeaponInfo == null || s.Ammo <= 0) continue;
                WeaponInfo i = s.WeaponInfo;
                StoreClass c = StoreClasses.Of(i.gun, i.jammer, i.missile, i.bomb || i.glideBomb, i.effectiveness.antiAir, i.effectiveness.antiSurface);
                gun |= c == StoreClass.Gun;
                missile |= c == StoreClass.AirMissile || c == StoreClass.StrikeMissile;
            }
        }

        private static string Aircraft(int n) => n == 1 ? "1 aircraft" : n + " aircraft";

        private static string AxisWord(DoctrineAxis axis)
        {
            switch (axis)
            {
                case DoctrineAxis.Guard: return "missile guard";
                case DoctrineAxis.Response: return "missile response";
                case DoctrineAxis.Interval: return "interval";
                case DoctrineAxis.Spread: return "spread";
                case DoctrineAxis.Targets: return "targets";
                case DoctrineAxis.Reach: return "reach";
                case DoctrineAxis.Weapons: return "weapons";
                default: return "radar";
            }
        }

        private static string ValueWord(DoctrineAxis axis, byte v)
        {
            if (axis == DoctrineAxis.Targets && v == (byte)TargetPolicy.Hold) return "HOLD FIRE";
            if (axis == DoctrineAxis.Weapons && v == (byte)WeaponsPolicy.NoAirToGround) return "NO A-G";
            if (axis == DoctrineAxis.Spread) return v != 0 ? "SPREAD" : "NO SPREAD";
            return (WingDoctrine.ValueName(axis, v) ?? "?").ToUpperInvariant();
        }

        private static OrderResult Recover(WingService w, RecoveryIntent intent, string what, Func<WingMember, bool> who)
        {
            int n = w.RecoverAll(intent, who);
            return n > 0 ? OrderResult.Acked($"{n} wingm{(n == 1 ? "an" : "en")} {what}")
                : OrderResult.Refused("No wingman can go: no friendly field, or already going");
        }

        /// <summary>Wingmen always launch from a field (the picked one, else the nearest friendly one).</summary>
        private static OrderResult Call(WingService w, int n)
        {
            if (SpawnService.Instance == null) return OrderResult.Refused("Wing Command is not ready");
            Aircraft caller = w.Player;
            if (caller == null) return OrderResult.Refused("Not flying");
            AircraftDefinition type = WingCommands.CallAirframe() ?? caller.definition;
            Airbase field = w.FieldFor(caller, type);
            if (field == null) return OrderResult.Refused("No friendly field to launch from");
            SpawnService.Instance.LaunchFromField(field, type, n);
            return OrderResult.Acked("");
        }

        /// <summary>Who an immediate order is for: everyone (null), an element's members, or the named aircraft.</summary>
        private static Func<WingMember, bool> Who(WingService w, WingScope scope)
        {
            switch (scope.Kind)
            {
                case ScopeKind.Element:
                    int e = scope.Element;
                    return m => w.ElementOf(m) == e;
                case ScopeKind.Members:
                    uint[] named = scope.Members;
                    return m => (object)m.Aircraft != null && Array.IndexOf(named, m.Aircraft.persistentID.Id) >= 0;
                default:
                    return null;
            }
        }

        /// <summary>The element a scope points at without detaching anyone (the first named member's element).</summary>
        private static int ElementFor(WingService w, in WingScope scope)
        {
            if (scope.Kind == ScopeKind.Element) return scope.Element;
            if (scope.Kind == ScopeKind.Members && scope.Members != null && scope.Members.Length > 0)
                return w.Roster.ElementOf(scope.Members[0]);
            return 0;
        }

        private static void MergeAll(WingService w, TransitionReason reason)
        {
            for (int e = 1; e < ElementRoster.MaxElements; e++)
                if (w.Roster.InUse(e)) w.MergeElement(e, reason);
        }

        private static void Units(uint[] from)
        {
            units.Clear();
            if (from == null) return;
            foreach (uint id in from)
                if (new PersistentID { Id = id }.TryGetUnit(out Unit u) && u != null && !u.disabled) units.Add(u);
        }
    }
}
