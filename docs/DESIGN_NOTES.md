# Design notes

Decisions that cost an argument. Kept so they are not made again the other way.

## Command & orders

- **Scope differs by interface, on purpose.** Map/WMC obey the explicit selection; radial
  and hotkeys always mean the whole wing; automatic reflexes act on the one affected
  member. Stated in tooltips and the README.
- **`HOLD` the order and `Hold` the ROE must not share a visible word.** ROE labels are
  `DEFEND` / `ESCORT` / `FREE`; the enum values are unchanged for config compatibility.
- **Point orders store their point.** `OrbitState` and landing take a captured
  `GlobalPosition`, not the leader's position at `Apply`. Holding aircraft re-run the ROE
  weapon decision or Hold is tactically inert.
- **Disengage is temporary.** `FallBackState` egresses then calls Form Up, instead of
  orbiting forever at a remote rally.
- **Splash 'Em / Fire For Effect** is the case Attack was never meant to govern: every
  wingman onto one target, no concurrency cap, ~0.8 s between launches, weapon preference
  ignored. Shares `AttackRunState`'s flying — one flag and a different `WingWeapons` entry.
  Not on the radial (six-slice cap is deliberate).

## Loadout

- **The mod defines no weapons.** Every option comes from the airframe's own
  `WeaponManager.hardpointSets[i].weaponOptions` — the same list the game's aircraft menu
  uses. Rules the editor needs (`SymmetryWithPrev`, `BlockedByOtherHardpoint`,
  `StandardLoadouts`) are the game's.
- **Role presets were removed.** The LOADOUT tab shipped with five presets, became a
  per-pylon template editor, and the preset machinery (`WingLoadoutPreset`, `PresetsFor`,
  `CanFit`, seed buttons) was deleted once unreachable. `WingLoadoutChoice` is now a
  template id or the standard fit — a struct, so the shop, delivery, reserve, recovery and
  takeover carry it by value without inspecting it.
- **A preset costs nothing extra.** One aircraft, one price. A loadout is not a resource;
  a failed build silently falls back to the standard fit rather than failing the purchase
  or launching an empty wingman.
- **Commit-after-spawn is load-bearing.** Nothing is recorded against an aircraft until
  `WingShop.NoteDelivery` fires with a real one. The plan is keyed by `AircraftDefinition`
  (the aircraft doesn't exist yet), read once at delivery, copied onto that airframe.
- **Templates persist across missions** (`Loadout/SavedTemplates` config blob). Text input
  needs `WingKeyboardGuard` to hold Rewired off the aircraft while a field is focused.

## Pilots & rank

- **Pilots belong to the squadron, not the airframe.** A recovered wingman's loadout stays
  with the airframe (RTB leaves an anonymous reserve count); the pilot goes back on the
  list. A killed pilot is not recycled — losses have to cost something.
- **Nothing outside `WingPilotRoster` invents a callsign.** A future pool-with-portraits
  feature replaces `WingPilotRoster.Provide` and nothing else.
- **Rank is a small real effect, not cosmetic and not a rebalance.** At `Pilot/RankEffect
  = 1` a Legend gets ~12% more reach/off-boresight and ~12% faster shot cycling than a
  Rookie. One-sided: rank only widens the envelope. `RankEffect = 0` keeps the record,
  removes the mechanic.
- **Kill credit is an inference** (the game doesn't expose a unit's killer) and errs
  generous, but a target is credited once — every other claim is dropped.

## Cargo

- **The stock helo transport state was already a complete supply run.** The bug was that
  nothing observed the outcome, so a delivered aircraft and a stuck one both sat on `CARGO`
  forever. `WingMember.CheckCargoRun` watches the cargo station's own ammunition once a
  second: cargo gone → complete + sortie; 5 min aboard → bring it back.
- **Deliver Cargo takes a map point** (arm-then-click, like Hold). This decoupled cargo
  from the rotary-only assumption: `CanDeliverCargo` no longer requires the stock transport
  state. Pressing the order again while armed abandons the point and runs the stock
  behaviour — still the right answer when the drop location doesn't matter.

## Economy & balance

- **Recruiting active mission AI costs a one-time reassignment fee** from player
  allocation (`≈ value × 0.25 × WingPriceGrowth ^ wingSize`), previewed before commit,
  deducted only after `WingRegistry.Add`, never refunded, never charged twice for the same
  `persistentID`.
- **Wing holdback is additive and per-type.** `AdditionalWingReserve` (default 0) adds to
  the mission + per-player reserve; it sets the live field, never patches
  `DeployAIAircraft`, and is restored on HQ/mission reset. Copy says `PER TYPE` — `+1`
  protects one of every stocked type.
- **Removed from the standard release:** player-specific anti-concentration penalty and
  global AI skill/bravery scaling (both affect balance outside the wing), default fast
  delivery, undeclared-airframe stock, `Ctrl+1..4` map groups, per-row Buy buttons, WMC
  debug actions. Generic target-saturation coordination stays — it helps every side.
- **Release never refunds a purchased aircraft.** It stays a live faction AI asset.

## Config & UI

- **~10 visible settings, not 63.** Tuned numbers derived from the game's arithmetic or
  flight testing (bank authorities, engagement ranges, XP awards) are constants in
  `Pure/WingTuning.cs`, not settings — a player couldn't know what to set them to. Retired
  keys stay bound but hidden; the user's config file is never rewritten.
- **Six visible formations** (Echelon Right, Line Abreast, Trail, Combat Spread, Finger
  Four, Vic). Solver support and enum values for the other four stay for old config files.
- **WMC is four tabs on one bezel** (`TACTICAL` / `SUPPLY` / `LOADOUT` / `WING`). Adding
  two tabs forced the Tactical page *shorter* (568 px): roster viewport 4→3 rows, ROE and
  Formation headings merged into one `ENGAGEMENT` block, per-choice text moved to the
  status line, order grid 2→3 columns once the set hit nine. Only the visible page
  refreshes — rebuilding the requisition catalogue walks the faction supply dictionary.
- **Active controls use the stock accent colour; only unavailable ones dim.** Every
  disabled control puts its reason on the status line. The map outranks hints there.

## Not in scope (and why the seams exist)

Multiple player flights / squadron hierarchy · patrol polygons or an RTS mission planner ·
a command-points currency or upkeep · custom network messages or non-host AI control ·
multiplayer sync of loadout/pilot state · a per-station loadout editor beyond templates ·
a rewrite of formation flight, panic manoeuvres, or target deconfliction. The pilot roster,
loadout choice, and directive types are shaped so those could be added without touching
assignment, economy, or flight.
