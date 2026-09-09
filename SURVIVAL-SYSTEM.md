# Survival perks and SAR

Implemented against the locally installed Nuclear Option `Assembly-CSharp.dll`, inspected
with ILSpy before choosing hooks. No game assembly, aircraft definition, prefab or save file
is modified. Effects execute on server/local simulation; SAR records follow server events.

## Verified game integration points

| Native code inspected | Implementation and boundary |
|---|---|
| `FuelTank.UseFuel(float rate)` subtracts engine consumption and updates mass; `LeakFuel` is separate. | Scale positive engine use by 0.7 for Fuel Discipline before native bookkeeping. No refuelling or leak cancellation. |
| `Pilot.ApplyDamage` subtracts four damage components and kills below zero HP. | Toughness scales positive components by 0.65 before both the death check and loss-cause recording. Only the assigned primary pilot is affected. |
| `Pilot.TakeGForceDamage` converts squared G above 400 to damage. | G Tolerance removes 75% of the excess above 400. Does not change flight physics or aircraft structural limits. |
| Missile seekers write `Missile.SetAimpoint`; native server steering follows it. | Luck stores one random outcome per missile and pilot using weak missile keys. A successful roll applies a stable horizontal 350 m guidance offset. Retargeting back does not reroll. Fuzes, warheads, targeting, damage and network ownership remain native. |
| `Aircraft.SpawnEjectingPilot` sets `PilotDismounted.parentUnit` and `pilotNumber` before server spawn. | Track the primary survivor in `OnStartServer` using the source aircraft ID. Aircraft disable may precede spawn, so living seated crew get 30 seconds to produce a confirmed survivor instead of immediate KIA. |
| `PilotDismounted.Capture` distinguishes same-HQ rescue and hostile capture; friendly recovery sets `UnitState.Returned`. | Friendly rescue releases the existing squadron record. Hostile capture makes it unavailable without declaring it dead. Repeated or late callbacks cannot release a new seat. |
| `PilotDismounted.SetPilotState`, `UnitDisabled`, and native base return. | Observe confirmed death and return, including callbacks before roster pruning. Unknown disappearance is MIA. Successful ordinary base recovery removes the SAR watch. |
| Native `CheckForCapture` searches for nearby units with capture capacity and checks grounded/stopped aircraft. `SlingloadHook` supports pilot rescue. | Player and native AI rescues already exist. WING-page dispatch reuses Land Here for an eligible idle wing helicopter; it does not bypass native pickup conditions. Water hoists remain manual. |

Commando is an abstract escape mechanic: one 50% roll upon confirmed landing, then a
120-second mission-time delay. Pathfinder alone provides a 35% chance and 60-second delay; together they provide 67.5% and 60 seconds. The survivor must still be alive, enabled, landed and unslung
at completion. Capture/death settles the record first and cancels escape. Success uses the
native returned/disabled state and removes the dismounted actor before making another seat
available. It does not resurrect pilots or spawn invulnerable rescue teams.

## Expanded perk hooks

The pool contains 24 perks; the complete values and stacking rules are in the README. Conditional fuel bonuses use native missile warnings and radar altitude. Specialist seated-damage resistance and Second Chance run in the same prefix before fatal-cause recording; the latter consumes one charge and preserves 1 HP rather than reversing a native death. Its charge resets on a fresh seat assignment or completed base sortie. Fast Learner scales positive XP awards with integer-overflow protection. Quick Draw and Standoff feed the existing wing weapon cadence and envelope calculations.

ChaffEjector.Fire and FlareEjector.Fire check ejectionInterval before scheduling their asynchronous launches. Reflex perks temporarily halve that interval; native launch, ammo consumption and rearm bookkeeping remain intact. RadarJammer.Fire derives current intensity from jammingIntensity and power drawn, retains the applied intensity, then subtracts it during expiry. ECM Specialist temporarily scales jammingIntensity by 1.75; native power use and intensity cleanup continue normally. Harmony finalizers restore each original instance field even after an exception; skipped prefixes do not restore an uninitialized value.

Survivalist scales positive pierce/blast/fire inputs to PilotDismounted.TakeDamage by 0.4, before native armour and lethality checks. It only applies to the tracked primary survivor on the server. Pathfinder uses the existing one-roll SAR escape lifecycle, including capture, death and hoist guards.

## Deliberately excluded

- Guaranteed missile immunity, warhead deletion, global missile accuracy changes, or damage
  cancellation on nearby units. Luck is guidance error and can still fail to save the pilot.
- Aircraft armour buffs, reduced fire/leak damage, and forced survival of failed ejections.
- Automatic hoist AI or new infantry pathfinding. Native rescue and existing helicopter
  landing are usable; new hoist navigation needs dedicated flight testing.
- Campaign persistence. The current roster resets when leaving a mission; perks follow that
  existing lifetime.

## Validation

- `dotnet build WingCommand.csproj` compiles against the installed Unity/game/BepInEx assemblies.
- `dotnet test tests/WingCommand.PureTests/WingCommand.PureTests.csproj` covers rank thresholds,
  unique random grants, multi-rank progression, XP overflow and effect calculations alongside
  existing flight/combat tests.
- `dotnet test tests/WingCommand.SurvivalTests/WingCommand.SurvivalTests.csproj` links the actual
  roster, SAR and perk hooks with a minimal native-game boundary. Exercises delayed ejection,
  selection/assignment exclusion, rescue, capture, death, repeated callbacks, Commando timing
  and roll boundaries, missile retargeting, authority/settings gates, damage ordering, dispatch
  constraints and mission reset.
- `pwsh -NoProfile -File tests/PilotLossRecord.ps1` and
  `pwsh -NoProfile -File tests/TakeoverPilotLifecycle.ps1` retain the existing loss and takeover checks.

Live-game visual and flight validation remains necessary: earn a promotion and inspect the
badges; compare equal-throttle fuel use; exercise an incoming missile against a Lucky pilot;
eject a wingman over land and water; rescue using player aircraft, native AI and DISPATCH SAR;
allow an enemy capture; verify a dead survivor never returns; and return an ordinary RTB
aircraft to base. Check host multiplayer replication and WING-page layout at supported UI
scales. The automated boundary tests do not simulate Unity physics, terrain or network RPCs.
