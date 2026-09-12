# Survival perks and SAR

Implemented against the locally installed Nuclear Option `Assembly-CSharp.dll`, inspected
with ILSpy before choosing hooks. No game assembly, aircraft definition, prefab or save file
is modified. Effects execute on server/local simulation; SAR records follow server events.

## Verified game integration points

| Native code inspected | Implementation and boundary |
|---|---|
| `Pilot.ApplyDamage` subtracts four damage components and kills below zero HP. | Toughness scales positive components by 0.50 before both the death check and loss-cause recording. Only the assigned primary pilot is affected. |
| Missile seekers write `Missile.SetAimpoint`; native server steering follows it. | Ghost (merging RadarGhost, HeatGhost, Luck) and NotchExpert store random deflection outcomes using weak missile keys. Ghost applies a 35% guidance disruption against all guided missiles. NotchExpert adds a 45% disruption when beaming radar missiles. Successful rolls apply a stable horizontal 350 m guidance offset. Retargeting back does not reroll. Fuzes, warheads, targeting, damage and network ownership remain native. |
| `Aircraft.SpawnEjectingPilot` sets `PilotDismounted.parentUnit` and `pilotNumber` before server spawn. | Track the primary survivor in `OnStartServer` using the source aircraft ID. Aircraft disable may precede spawn, so living seated crew get 30 seconds to produce a confirmed survivor instead of immediate KIA. |
| `PilotDismounted.Capture` distinguishes same-HQ rescue and hostile capture; friendly recovery sets `UnitState.Returned`. | Friendly rescue releases the existing squadron record. Hostile capture makes it unavailable without declaring it dead. Repeated or late callbacks cannot release a new seat. |
| `PilotDismounted.SetPilotState`, `UnitDisabled`, and native base return. | Observe confirmed death and return, including callbacks before roster pruning. Unknown disappearance is MIA. Successful ordinary base recovery removes the SAR watch. |
| Native `CheckForCapture` searches for nearby units with capture capacity and checks grounded/stopped aircraft. `SlingloadHook` supports pilot rescue. | Player and native AI rescues already exist. WING-page dispatch reuses Land Here for an eligible idle wing helicopter; it does not bypass native pickup conditions. Water hoists remain manual. A paid local team is the non-flight alternative: 10,000,000 funds and five mission minutes. |

Confirmed living survivors receive a red map outline and a persistent `SAR · CALLSIGN` label.
The WING page offers AIR SAR for manual helicopter dispatch and LOCAL 10M for a timed local
recovery. Local recovery charges immediately, counts down for 300 mission seconds, and only
returns a survivor who has not already died or been captured. Its fee is not refunded.

Commando is an elite evasion mechanic: one 60% roll upon confirmed landing, then a
90-second mission-time delay. The survivor must still be alive, enabled, landed and unslung
at completion. Capture/death settles the record first and cancels escape. Success uses the
native returned/disabled state and removes the dismounted actor before making another seat
available. It does not resurrect pilots or spawn invulnerable rescue teams.

## Streamlined perk hooks

The pool contains 25 curated, high-impact abilities organized into distinct combat and tactical doctrines:
- **Core Survivability & Growth**: `Toughness` (halves cockpit damage), `Commando` (60% unassisted evasion on bailout), `FastLearner` (+50% XP gain), `Talented` (prodigy trait allowing pilot to roll 3 additional skills).
- **Electronic Warfare & Defensive Systems**: `Countermeasures` (halves flare burst interval and doubles ECM jamming intensity), `Ghost` (35% guidance disruption against all guided missiles), `NotchExpert` (45% radar missile deflection when beaming), `EarlyWarning` (+1.5s reaction window against incoming ordnance), `BreakTurn` (+50% roll rate and airbrake snap during terminal missile evasion).
- **Guns & BFM**: `Marksman` (+30% effective gun range), `Snapshot` (+40% off-boresight missile launch envelope), `LeadPursuit` (+25% dogfight lead turn effort), `HeadOnJoust` (+25% head-on merge launch range), `EnergyFighter` (preserves +15% corner speed floor in sustained turns).
- **Tactical & Weapons**: `QuickDraw` (0.60x weapon reaction interval), `Standoff` (1.30x release envelope expansion), `ApexHunter` (+40% radar missile range at >= 4,000 m altitude), `Bombardier` (+35% CCRP bomb envelope and 30% lower release floor), `SalvoSpecialist` (-40% burst delay between multiple bomb/rocket releases), `WildWeasel` (+50% engagement target priority for radar emitters / SAMs), `TargetMaster` (+50% target reservation lock duration), `Burnthrough` (+25% radar target scoring against jamming emitters).
- **Formation & Low-Level Flight**: `WingmanInstinct` (+30% rejoin closing rate), `CombatSpread` (+40% lateral formation dispersal under threat), `TerrainHugger` (-25 m terrain floor buffer for nap-of-the-earth ingress).

FlareEjector.Fire and RadarJammer.Fire check their parameters before firing. Countermeasures temporarily halves flare interval (0.5x) and doubles ECM intensity (2.0x); Harmony finalizers restore each original instance field even after an exception. On the WMC screen, only acquired skills are displayed.

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
  constraints, paid local recovery timing and funds, and mission reset.
- `pwsh -NoProfile -File tests/PilotLossRecord.ps1` and
  `pwsh -NoProfile -File tests/TakeoverPilotLifecycle.ps1` retain the existing loss and takeover checks.

Live-game visual and flight validation remains necessary: earn a promotion and inspect the
badges; compare equal-throttle fuel use; exercise an incoming missile against a Lucky pilot;
eject a wingman over land and water; rescue using player aircraft, native AI and DISPATCH SAR;
allow an enemy capture; verify a dead survivor never returns; and return an ordinary RTB
aircraft to base. Check host multiplayer replication and WING-page layout at supported UI
scales. The automated boundary tests do not simulate Unity physics, terrain or network RPCs.
