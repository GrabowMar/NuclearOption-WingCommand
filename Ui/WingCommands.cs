using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum ApCommand : byte { Level, Heading, Altitude, VerticalSpeed, Speed, Off }

    /// <summary>Player commands shared by the radial and the hotkeys.</summary>
    internal static class WingCommands
    {
        public static void Autopilot(ApCommand c)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            switch (c)
            {
                case ApCommand.Level: ap.SetLateral(LateralHold.Level); break;
                case ApCommand.Heading: ap.SetLateral(LateralHold.Heading); break;
                case ApCommand.Altitude: ap.SetVertical(VerticalHold.Altitude); break;
                case ApCommand.VerticalSpeed: ap.SetVertical(VerticalHold.VerticalSpeed); break;
                case ApCommand.Speed: ap.ToggleSpeed(); break;
                default: ap.Off(); break;
            }
        }

        /// <summary>Wingmen always launch from a field (the picked one, else the nearest friendly one).</summary>
        public static void Call(int n)
        {
            if (!Ready(out WingService w) || SpawnService.Instance == null) return;
            Aircraft caller = w.Player;
            if (caller == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            AircraftDefinition type = CallAirframe() ?? caller.definition;
            Airbase field = w.FieldFor(caller, type);
            if (field == null)
            {
                WingToast.Show("No friendly field to launch from");
                return;
            }
            SpawnService.Instance.LaunchFromField(field, type, n);
        }

        public static void NextField()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            Airbase field = w.NextField(w.Player, CallAirframe());
            WingToast.Show(field != null
                ? $"Launch field: {field.name} ({(field.transform.position - w.Player.transform.position).magnitude / 1000f:0} km)"
                : "No friendly field to launch from");
        }

        public static void FormUp()
        {
            if (!Ready(out WingService w)) return;
            w.FormUp();
            WingToast.Show("Form up");
        }

        /// <summary>The wing's helicopters land around you and wait (spec M4 §5).</summary>
        public static void LandHere()
        {
            if (!Ready(out WingService w)) return;
            int n = w.LandHere(out string refusal);
            WingToast.Show(n > 0 ? $"{n} landing here" : "Cannot land here: " + refusal);
        }

        public static void TakeOff()
        {
            if (!Ready(out WingService w)) return;
            int n = w.TakeOff();
            WingToast.Show(n > 0 ? $"{n} lifting off" : "Nobody is down");
        }

        public static void NextShape()
        {
            if (Ready(out WingService w)) w.NextShape();
        }

        public static void NextFamily()
        {
            if (Ready(out WingService w)) w.NextFamily();
        }

        public static void SetSpacing(SpacingPreset preset)
        {
            if (Ready(out WingService w)) w.SetSpacing(preset);
        }

        public static void CycleSpacing()
        {
            if (Ready(out WingService w)) w.SetSpacing((SpacingPreset)(((int)w.Selection.Spacing + 1) % 4));
        }

        /// <summary>The whole wing home to the reserve (spec M3 §4).</summary>
        public static void Rtb() => Recover(RecoveryIntent.Rtb, "returning to base");

        /// <summary>The whole wing home to refuel and rearm, then back out.</summary>
        public static void Refit() => Recover(RecoveryIntent.Refit, "going to refit");

        private static void Recover(RecoveryIntent intent, string what)
        {
            if (!Ready(out WingService w)) return;
            int n = w.RecoverAll(intent);
            WingToast.Show(n > 0 ? $"{n} wingm{(n == 1 ? "an" : "en")} {what}" : "No wingman can go: no friendly field, or already going");
        }

        public static void Dismiss()
        {
            if (!Ready(out WingService w)) return;
            if (w.Members.Count == 0)
            {
                WingToast.Show("No wingmen to dismiss");
                return;
            }
            w.Dismiss();
            WingToast.Show("Wing dismissed");
        }

        /// <summary>Escort the player's selected friendly unit (aircraft, vehicle or ship), else the nearest friendly
        /// aircraft ahead within 5 km (spec M2 §6).</summary>
        public static void EscortTarget()
        {
            if (!Ready(out WingService w)) return;
            Aircraft player = w.Player;
            if (player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            Unit target = SelectedFriendly(player, w) ?? NearestFriendlyAhead(player, w);
            if (target == null)
            {
                WingToast.Show("No friendly to escort");
                return;
            }
            w.SetEscort(target);
            WingToast.Show("Escorting " + target.unitName);
        }

        /// <summary>Takes command of the selected friendly aircraft, else the nearest one ahead (spec M3 §5).</summary>
        public static void Recruit()
        {
            if (!Ready(out WingService w)) return;
            Aircraft player = w.Player;
            if (player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            Aircraft target = SelectedFriendly(player, w) as Aircraft ?? NearestFriendlyAhead(player, w) as Aircraft;
            if (target == null)
            {
                WingToast.Show("No friendly aircraft to recruit");
                return;
            }
            WingToast.Show(WingRecruitment.TryRecruit(w, target, out _, out string reason)
                ? target.unitName + " joins the wing"
                : "Cannot recruit " + target.unitName + ": " + reason);
        }

        public static float MoveAheadMetres = 10000f, PatrolHalfMetres = 10000f, OrderHeightMin = 50f, ScoutAheadMetres = 20000f;

        /// <summary>The wing orbits the point below the player (spec M4 §2.4).</summary>
        public static void OrbitHere() => Order(p => WingTask.Orbit(Point(p, 0f, 0f)), "orbiting here");

        public static void HoldHere() =>
            Order(p => WingTask.Hold(Point(p, 0f, 0f), Vec3.HeadingDeg(p.transform.forward.ToVec3())), "holding here");

        /// <summary>Spec M4 §7.1: a wing helicopter lands next to the nearest downed wing pilot for the pickup.</summary>
        public static void Rescue()
        {
            if (!Ready(out WingService w)) return;
            w.Rescue(out string result);
            WingToast.Show("Rescue: " + result);
        }

        /// <summary>Spec M4 §7.2: helicopters carrying cargo land, deploy it and rejoin.</summary>
        public static void DeliverCargo()
        {
            if (!Ready(out WingService w)) return;
            int n = w.DeliverCargo(out string refusal);
            WingToast.Show(n > 0 ? $"Deliver cargo: {n} landing" : "Deliver cargo: " + refusal);
        }

        public static void MoveAhead() => Order(p => WingTask.Move(Point(p, MoveAheadMetres, 0f)), "moving ahead");

        /// <summary>Spec M7 §2.4: Move 20 km ahead, reporting ground contacts on the way and while orbiting there.</summary>
        public static void ScoutAhead() => Order(p =>
        {
            WingTask t = WingTask.Move(Point(p, ScoutAheadMetres, 0f));
            t.Scout = true;
            return t;
        }, "scouting ahead");

        public static void PatrolHere() =>
            Order(p => WingTask.Patrol(false, Point(p, -PatrolHalfMetres, 0f), Point(p, PatrolHalfMetres, 0f)), "patrolling here");

        private static Waypoint Point(Aircraft p, float forward, float right)
        {
            Vec3 at = p.GlobalPosition().ToVec3();
            Vec3 f = p.transform.forward.ToVec3().Horizontal;
            f = f.SqrLength > 1e-4f ? f.Normalized : Vec3.Forward;
            Vec3 there = at + f * forward + new Vec3(f.Z, 0f, -f.X) * right;
            Waypoint w = Waypoint.At(there.X, there.Z);
            // The player's height only when flying: from the runway the wing keeps its own (review M4a I2d).
            w.Altitude = p.radarAlt > OrderHeightMin ? at.Y : float.NaN;
            return w;
        }

        private static void Order(Func<Aircraft, WingTask> make, string what)
        {
            if (!Ready(out WingService w)) return;
            if (w.Player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            OrderResult r = w.Order(make(w.Player));
            WingToast.Show(r.Accepted ? "Wing " + what : "Wing cannot: " + r.Reason);
        }

        public static void Engage()
        {
            if (!Ready(out WingService w)) return;
            if (!w.MayEngage(out int hostiles, out int members))
            {
                WingToast.Show($"Outnumbered {hostiles} to {members} - Engage again to fight anyway");
                return;
            }
            int n = w.Engage();
            WingToast.Show(n > 0 ? $"{n} engaging" : "Nobody can engage");
        }

        /// <summary>The player's selected enemy targets, split across the wing (spec M5, M5b).</summary>
        public static void AttackTarget()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            List<Unit> targets = SelectedEnemies(w.Player);
            if (targets.Count == 0)
            {
                WingToast.Show("No enemy target selected");
                return;
            }
            int n = w.Attack(targets);
            string plural = targets.Count == 1 ? "" : "s";
            WingToast.Show(n > 0 ? $"{n} attacking {targets.Count} target{plural}" : "Nobody can attack");
        }

        /// <summary>Spec M5 §9.2: one missile from every wingman with your target in envelope.</summary>
        public static void Splash()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            List<Unit> targets = SelectedEnemies(w.Player);
            if (targets.Count == 0)
            {
                WingToast.Show("Splash: select a target first");
                return;
            }
            int fired = w.Splash(targets[0], out int capable);
            if (fired > 0) WingRadioAudio.Play(WingRadioAudio.Earcon.Splash);
            WingToast.Show(fired > 0 ? $"Splash: {fired} firing on {targets[0].unitName}"
                : capable > 0 ? "Splash: launchers not ready" : "Splash: nobody in range");
        }

        public static void ClearMySix()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            int found = w.ClearMySix(out int engaged);
            WingToast.Show(found == 0 ? "Your six is clear" : engaged > 0 ? $"{engaged} clearing your six ({found} bandits)" : "Nobody can engage");
        }

        /// <summary>Spec M7 §1.5: the first wingman flying with the wing calls the nearest air threat's BRA from you.</summary>
        public static void BogeyDope()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            // The radio says it; when it will not (radio off, or the line dropped), the answer is a toast (review M7a I4).
            if (!BogeyDopeCall(w, out string answer)) WingToast.Show(answer);
        }

        /// <summary>The Bogey Dope answer on the radio (false with the reason when nobody can answer).</summary>
        public static bool BogeyDopeCall(WingService w, out string answer)
        {
            WingMember speaker = null;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && !m.OnGround)
                {
                    speaker = m;
                    break;
                }
            RadioDirector radio = RadioDirector.Instance;
            if (speaker == null || radio == null)
            {
                answer = "No wingmen to ask";
                return false;
            }
            answer = w.NearestAirThreat(w.Player, out Vec3 pos, out Vec3 vel, out string type)
                ? $"Bogey dope, {Bra.Format(w.Player.GlobalPosition().ToVec3(), pos, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial)}. {type}."
                : "Picture clean.";
            return radio.Answer(speaker, "BOGEYDOPE", answer);
        }

        /// <summary>Reserve → Escort → Sweep: what members shoot at while holding formation (spec M5 §8).</summary>
        public static void NextDoctrine()
        {
            if (!Ready(out WingService w)) return;
            WingDoctrine d = w.NextDoctrine();
            string what = d.Targets == TargetPolicy.Hold ? "holding fire"
                : d.Targets == TargetPolicy.Cover ? "covering you"
                : "targets of opportunity";
            WingToast.Show($"Doctrine {d.PatternName}: {what}");
        }

        public static void Disengage()
        {
            if (!Ready(out WingService w)) return;
            int n = w.Disengage();
            WingToast.Show(n > 0 ? $"{n} disengaging" : "Nobody engaged");
        }

        private static List<Unit> SelectedEnemies(Aircraft player)
        {
            var enemies = new List<Unit>();
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            if (targets == null) return enemies;
            foreach (Unit u in targets)
                if (u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != player.NetworkHQ) enemies.Add(u);
            return enemies;
        }

        public static void EscortMe()
        {
            if (!Ready(out WingService w)) return;
            if (!w.Escorting)
            {
                WingToast.Show("Already on you");
                return;
            }
            w.SetEscort(null);
            WingToast.Show("Escorting you");
        }

        private static Unit SelectedFriendly(Aircraft player, WingService w)
        {
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            if (targets == null) return null;
            foreach (Unit u in targets)
                if (u != null && !u.disabled && !ReferenceEquals(u, player) && u.NetworkHQ == player.NetworkHQ &&
                    !(u is Aircraft a && w.IsMember(a))) return u;
            return null;
        }

        private static readonly List<Aircraft> candidates = new List<Aircraft>();
        private static Vec3[] candidatePositions = new Vec3[64];

        private static Unit NearestFriendlyAhead(Aircraft player, WingService w)
        {
            candidates.Clear();
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || a.disabled || ReferenceEquals(a, player) || a.NetworkHQ != player.NetworkHQ || w.IsMember(a)) continue;
                candidates.Add(a);
            }
            if (candidatePositions.Length < candidates.Count) candidatePositions = new Vec3[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) candidatePositions[i] = candidates[i].GlobalPosition().ToVec3();
            int chosen = EscortPick.Nearest(player.GlobalPosition().ToVec3(), player.transform.forward.ToVec3(),
                candidatePositions, candidates.Count);
            return chosen >= 0 ? candidates[chosen] : null;
        }

        /// <summary>The configured airframe to call, or null for the player's own type.</summary>
        public static AircraftDefinition CallAirframe()
        {
            string name = Plugin.Settings.CallAirframe.Value;
            if (string.IsNullOrWhiteSpace(name)) return null;
            var catalogue = Encyclopedia.i != null ? Encyclopedia.i.aircraft : null;
            if (catalogue != null)
                foreach (AircraftDefinition d in catalogue)
                    if (d != null && string.Equals(d.unitName, name.Trim(), StringComparison.OrdinalIgnoreCase)) return d;
            WingToast.Show($"Unknown airframe '{name}'; calling your own type");
            return null;
        }

        private static bool Ready(out WingService w)
        {
            w = WingService.Instance;
            if (w?.Selection != null) return true;
            WingToast.Show("Wing Command is not ready");
            return false;
        }
    }
}
