| **Rejoin Formation** | Wingmen close on their slot and hold station |
| **Attack My Target** | Wingmen fly an attack run on what you have designated, and return to the wing once it is dead |
| **Attack My Target** | Wingmen fly an attack run on what you have designated, and return to the wing when it is dead |
# WingCommand

A BepInEx mod for **Nuclear Option** that lets you command AI aircraft: form a wing, hold
formation, and issue orders — from an in-cockpit radial menu and from the tactical map.

Built against game version `0.34.2`, Unity 2022.3.62f2 (Mono), BepInEx 5.4.23.3.

---

## What it does

### Wingmen and formation flying
Assign friendly AI aircraft to your wing. They fly a formation slot on you and hold
station, then break off and fight when you tell them to — or automatically, when you come
under missile attack.

Formation shapes: `EchelonRight`, `EchelonLeft`, `LineAbreast`, `Trail`, `CombatSpread`,
`FingerFour`, `Vic`, `Diamond`, `Ladder`, `Wall`.

### Orders
| Order | Effect |
|---|---|
| **Rejoin Formation** | Wingmen close on their slot and hold station |
| **Attack My Target** | Wingmen fly an attack run on what you have designated, and return to the wing once it is dead |
| **Engage** | Hunt freely, but on a tether — see below |
| **Fall Back** | Emergency break: scatter on separate headings with flares, run for the nearest friendly airbase, hold there |
| **Orbit Here** | Anchor to where you are *now* and fly a CAP over it while you go elsewhere |
| **Deliver Cargo** | Transport helicopters run the game's own supply behaviour |
| **Land Here** | Helicopters set down where they are, rather than flying home |
| **Return To Base** | Wingmen switch to the AI landing state |
| **Change Shape** | Cycle the formation geometry |
| **Disband** | Release everyone back to normal AI behaviour |

**Engage is tethered.** It used to hand a wingman to the stock combat AI permanently, which
made it indistinguishable from Disband except that the aircraft stayed on the roster —
ordering it meant losing the aircraft for the rest of the mission. It now means "hunt, but
stay within `LeashRadius` of me": a wingman that strays past the leash flies back, and is
turned loose again once it is inside half of it. The two thresholds are the point — a
single one would have it flip-flopping on the boundary every frame.

Orders that are going somewhere deliberate — Fall Back, Deliver Cargo, Land Here — are not
interrupted by a bingo-fuel call. A wingman already on the deck does not need telling to
land.

Cargo and landing orders only apply to the aircraft that can carry them out, and the
confirmation says how many did: an order that silently applies to nobody looks exactly like
one that failed.

### Rules of engagement

Orders and rules of engagement answer different questions, and keeping them apart is what
makes the wing predictable:

> **An order says where a wingman flies. Rules of engagement say what it shoots.**

Three rungs, wing-wide. Each has its own answer to the same event — you being shot at —
which is the test of whether three rungs earn their place:

| ROE | Leaves the slot? | What it shoots | You come under missile attack |
|---|---|---|---|
| **Hold** | never | missiles at you or itself; ground targets only while you are attacking ground | shoots the missile down |
| **Escort** | never | weapons free, preferring whatever is threatening you | shoots the aircraft that launched it, from the slot |
| **Free** | emergency only | weapons free | breaks formation and goes after the shooter |

This replaced a two-state Defensive/Aggressive posture that overlapped the order set badly.
Aggressive made a wingman leave formation to hunt, which is what the **Engage** order is
for — reached by a different code path, with different recovery — so the two were nearly
impossible to tell apart. And *Cover Me* was an **order** meaning "hold station but shoot
what is hunting the leader", which is a weapons policy wearing an order's clothes; it is
now the Escort rung.

Leaving the slot is otherwise always an explicit **Engage** or **Attack My Target** order.
The one exception is the Free rung's emergency break, which stays because it is a reaction
to a specific event rather than a standing policy.

### The formation shop

Wingmen can be bought rather than only recruited, from the **SQUADRON SUPPLY** section of
the WMC screen. Everything about the economy is the game's own, reached without patching
anything:

- **Price** is `AircraftDefinition.value` — the same field the player's own aircraft menu
  prices from.
- **Payment** comes out of `Player.Allocation`, the same pool that buys your own airframe
  and weapons.
- **Stock** is drawn from the faction's supply through `FactionHQ.AddSupplyUnit`, the exact
  call the game's own reserve flow uses. Buying a wingman therefore competes with the
  mission's AI for airframes, and a long mission can run a type dry.

The catalogue offers only what the faction has in stock, minus the types the mission
restricts and the types your rank does not reach — the same two gates the player's own
aircraft menu applies, so the shop cannot be used to fly around them.

Modded and workshop aircraft are included. A mission declares its faction stock by name,
so an aircraft it never heard of has no supply entry and would never appear; those are
taken from the game's own aircraft registry instead and given their own small per-mission
allowance rather than drawing on faction stock, since inventing supply entries on the
mission's behalf is not the mod's business. Turn it off with `IncludeUndeclaredAircraft`.

The catalogue also hides anything that could not join your formation. Rotary and fixed-wing cannot
share a formation, so flying a jet shows you only jets and flying a helicopter shows you
only helicopters. The list pages with the arrows when the faction has more types in stock
than fit on one screen.

**Price compounds with wing size.** Each wingman already on the roster multiplies the next
one's price by `WingPriceGrowth` (1.5 by default), so a 1000-credit airframe costs 1000,
1500, 2250, 3375 as the wing fills. A large wing is meant to be a serious investment.

**Two deliveries.** *Base* puts the aircraft in the circuit over the nearest friendly
airbase, and it flies to its slot under its own power — cheap, but it takes time to arrive.
*Fast* puts it behind you at your speed and altitude, on the wing within seconds, for a
further 25%.

**The balance gate.** Missions set a per-faction `AIAircraftLimit`, adjusted for player
counts. Purchases may push the faction at most `OverLimitAllowance` aircraft past it — one,
by default — after which the shop refuses until something is lost. Raising that setting is
the main way to unbalance a mission with this feature.

Nothing is spent until an aircraft actually exists: every gate runs, and the spawn returns,
before a single credit or airframe moves.

The radial's **Buy Wingman** entry is a shortcut for the common case — your own airframe
type, base delivery. The catalogue and the delivery choice live on the MFD, because a price
list is not something a wheel can show.

### WMC — the MFD screen
The main wing interface is a native MFD screen labelled **WMC**, sitting on the left bezel
alongside BDF / MAP / HUD. Each bezel column carries six buttons but only three configured
screens, so the fourth slot is free; registering an `MFDScreen` there and calling
`VirtualMFD.SetupButtons()` lights the button up and the game drives show/hide exactly as
it does for its own screens.

It shows every wingman with slot, name, order and **live slot error in metres**, plus
buttons for Add Selected, Recruit Near, Rejoin, Engage, Fall Back, Orbit Here,
Deliver Cargo, Land Here, RTB and Disband, per-member release
and formation cycling.

The panel is built from known widgets rather than cloned from HUD OPTIONS — the stock
hierarchy is not something this mod can safely dissect — borrowing only the font and the
`ThemeManager` colours so it still matches the game's look.

### Wing symbology

Your wing is marked the same way on both displays, so what you see through the canopy
agrees with what you see on the map.

- **Wingmen** — drawn in a distinct colour (default cyan `#33E5FF`, configurable) on the
  tactical map *and* on the in-cockpit HUD. Selected members are drawn brighter on the map.
- **What your wing is engaging** — units a wingman has been ordered onto, or has broken
  formation to fight, are marked in amber (`#FFB020`) on both displays. An autonomous
  target only counts while the member is actually off fighting: a weapon manager holds the
  last target it was given long after the engagement is over, and marking that would leave
  stale symbols scattered across the display.

Note that the game marks one friendly aircraft distinctly on its own — `AllyInfo` picks
the nearest ally each second and swaps its HUD marker to a different sprite. That is a
proximity indicator and has nothing to do with your wing, which is why the HUD needed its
own designation rather than leaving it to be misread.

### Map layer
- **Aircraft tasking** — select friendly AI aircraft on the maximised map and right-click
  to assign them to your wing. Vanilla ignores this gesture for aircraft, because
  `Aircraft` does not implement `ICommandable` (only ground vehicles, ships and missiles
  do), so it is free for us to use. The selection is dropped once they are recruited: a
  selected icon is drawn white by the game's own highlight, which would hide the wing
  colour on exactly the aircraft that just earned it.
- **Squad groups** — `Ctrl`+`1`..`4` stores the current map selection, `1`..`4` restores
  it. Works for ground and naval units
  too, so you can re-issue vanilla move orders to a saved group without hand-picking it
  every time.

### Debugging formation

Station-keeping is hard to judge by eye, so the slot-error column is the instrument:

| Reading | Meaning |
|---|---|
| Settles to a steady value, shown bright | Wingman is parked in the slot — controller is tuned |
| Swings up and down repeatedly | Throttle gain too hot; the wingman is hunting |
| Grows without bound | Wingman cannot keep up, or is not actually under our control |
| `-` | Not on a formation order |

The same numbers appear in the compact in-flight panel, which hides itself while the map
is up so the two never overlap.

### Debug action (optional, off by default)
One testing aid on the WMC screen, shown only when `Debug/EnableDebugActions` is on:

| Action | Effect |
|---|---|
| **Spawn Wing Of My Aircraft** | Spawns fresh copies of your own aircraft type, already in their slots, and assigns them |

It is a cheat — it creates aircraft outright — and it writes world state, so it is
**host / single-player only** and refuses otherwise. Its purpose is to remove the setup
cost of testing station-keeping: rather than flying twenty minutes to find friendly AI, put
a wing on your wingtip and read the slot-error column immediately.

A *Teleport Wing To Formation* action used to sit beside it, snapping wingmen into their
slots. It was removed: placing an aircraft with a new position and velocity in a single
step fought the game's own G-force accounting badly enough that it was never reliable, and
the spawn action covers the same testing need without the fight.

### AI tuning (optional, off by default)
Scales AI pilot `skill` and `bravery`, which the stock combat AI reads for aim error,
missile reaction time, target selection and threat avoidance. Applies to all AI aircraft
on both sides.

---

## Controls

Orders go through the **game's own radial menu**, as a nested submenu — the same technique
BOTE uses. Open the radial with your normal *Radial Menu* binding and pick **Wing Command**;
the wheel then becomes the commander menu. As in BOTE, selecting an entry closes the wheel,
so re-open it to see the submenu you switched to.

```
Radial Menu ─┬─ (stock entries: Gear, Radar, Eject, …)
             └─ Wing Command ─┬─ Orders ─┬─ Rejoin
                              │          ├─ Engage
                              │          ├─ Fall Back
                              │          ├─ Tasking ─┬─ Return To Base
                              │          │           ├─ Orbit Here
                              │          │           ├─ Deliver Cargo
                              │          │           ├─ Land Here
                              │          │           └─ Back
                              │          └─ Back
                              ├─ Attack My Target
                              ├─ Formation ─┬─ ten shapes
                              │             └─ Back
                              ├─ Rules Of Engagement
                              └─ Wing ─┬─ Recruit Nearest
                                       ├─ Disband
                                       └─ Back
```

No page carries more than six slices. A wheel is selected by direction, so every extra
entry narrows the wedge you have to hit; past about six the gesture stops being reliable,
especially on a stick. That is why the tasking orders sit on their own page rather than
being added to the orders one.

Picking any order restores the stock wheel afterwards, and it also restores itself after a
few seconds if you back out without choosing.

| Input | Action |
|---|---|
| *Radial Menu* binding | Open the wheel, then **Wing Command** |
| **Right-click** on map | Assign selected friendly AI aircraft to the wing |
| **Ctrl**+**1**..**4** | Store map selection as a squad group |
| **1**..**4** | Recall a squad group |
| **F1** | ConfigurationManager — all settings below |

Optional direct hotkeys for *Rejoin* and *Engage* are unbound by default.

### Why the native wheel

In flight the cursor is captured for mouse-look, so `Input.mousePosition` does not move —
a self-drawn wheel driven by cursor position simply does not track. The stock wheel
integrates the Rewired **Pan View / Tilt View** look axes instead. Using it means selection
works with mouse, stick and the keyboard scheme, and matches whatever the player already
set under `PlayerSettings.radialControl`.

A standalone fallback wheel is still included for the case where the native integration
cannot attach (see below). It uses the same Rewired axes. It is unbound by default —
set `Keys/FallbackRadialMenu` if you need it.

---

## Configuration

Everything is exposed through ConfigurationManager (**F1**) and written to
`BepInEx/config/com.marci.wingcommand.cfg`.

- **Formation** — geometry, nine flying knobs, three rotary knobs (below)
- **AI** — enable tweak, skill scale, bravery scale, mutual support
- **UI** — wing symbology colours, status panel, map commands
- **Keys** — radial menu key, optional quick-order hotkeys
- **Debug** — verbose logging of every order and state transition

### Tuning formation flight

The Formation section used to carry thirty-eight entries, several of which encoded one
physical quantity as two numbers that only meant anything as a ratio — so tuning either
one silently moved a quantity neither of them named. What is left names what it controls,
in the unit the controller acts in.

| Setting | Default | What it does |
|---|---|---|
| `Aggression` | 1.0 | Master scale on how hard a wingman corrects: steering, closure and throttle together. Above ~2 they start to hunt. |
| `Damping` | 1.0 | Master scale on the rate terms that arrest a correction before it arrives. This is what stops the slow left-right rocking. |
| `CommandAngle` | 25° | Largest heading correction while holding station — the real limit on how fast a lateral error closes. |
| `StationBankDegrees` | 75° | Bank authority while settled. |
| `PursuitBankDegrees` | 160° | Bank authority while rejoining. Authority eases between the two with slot error, so nothing steps. |
| `ThrottleGain` | 0.12 | Throttle change per m/s of speed error. Resting throttle is the airframe's own cruise setting. |
| `CaptureDistance` | 500 m | Slot error below which a wingman counts as on station. |
| `RejoinStagger` | 1.2 s | Per-slot delay so a Rejoin arrives in sequence, not as a scrum. |
| `LeashRadius` | &mdash; | How far a hunting wingman may stray on an Engage order before it is recalled. |
| `FallBackStandoff` | 6000 m | How far a Fall Back runs before the wing settles into its holding orbit. |
| `OrbitRadius` | 2000 m | Radius of the holding circle, for Orbit Here and the end of a Fall Back. |
| `WingPriceGrowth` | 1.5 | Compounding price multiplier per wingman already in the formation. |
| `FastDeliverySurcharge` | 0.25 | Extra fraction charged for delivery straight to your wing. |
| `OverLimitAllowance` | 1 | How far purchases may push the faction past the mission’s AI aircraft limit. |
| `IncludeUndeclaredAircraft` | true | Offer aircraft the mission did not stock, which is what makes modded airframes purchasable. |
| `UndeclaredStock` | 3 | How many of each undeclared airframe may be bought per mission. |
| `BankMatchBlend` | 0.35 | How much a settled wingman rolls to match your bank. 0 switches it off. |
| `RotaryHoverSpeed` | 25 m/s | Leader speed below which helicopters hold their slot as a point rather than flying a heading. |
| `RotaryPowerSeconds` | 20 s | Helicopter destination distance — a **power** setting, see below. |
| `RotaryCommandAngle` | 30° | Largest heading correction a helicopter will command. |

Avoidance distances (separation radius, path-cut corridor) are derived from the spacing in
use rather than configured, so changing `SlotSpacing` or `RotarySpacingScale` moves the
whole geometry together and cannot leave one threshold contradicting another.

### Two things about the game's autopilot worth knowing

Both were found by reading the decompiled autopilots, and the mod was on the wrong side of
each for a long time.

**`effort` above 1 is the agility switch.** `AutopilotPlane.AutoAim` computes
`num3 = (effort > 1 || radarAlt < 1) ? 1 : clamp01(airspeed / cornerSpeed)` and then spends
it twice — as `RotateTowards(..., 0.9 * num3²)`, the cap on how fast the commanded
direction may swing, and as `bankAllowed *= max(num3², 0.45)`. Below corner speed both
shrink quadratically, so a wingman that had slowed down was having its turn authority
halved on top of the aerodynamic penalty it already pays. Anything above 1 removes that
double-counting.

**For helicopters, distance is throttle.** `AutopilotHelo` sets collective from
`0.5 + distance*0.001 - speed*0.02`, so the distance to the destination *is* the power
command — about twenty times the speed for the terms to cancel at hover power. That is
what `RotaryPowerSeconds` is for. Steering is set separately, by rotating the commanded
heading, because when the two shared one vector, holding speed forced the destination far
enough away that every cross-track correction became about five degrees.

Also worth knowing before tuning rotary flight: `AutopilotHelo` recomputes its forward
waypoint only **once per second** and rate-limits it to 0.8 rad. That is a hard ceiling on
helicopter responsiveness that no setting can raise.

---

## Multiplayer

AI pilots are **server-authoritative**. Formation control therefore works correctly in
**single-player or when you are the host**. As a non-host client, your orders will fight
the server's own AI updates.

The squad-group feature is purely local selection state and is safe anywhere. Vanilla
ground move orders continue to go through the game's own networked
`UnitCommand.CmdSetDestination` path, which already validates faction ownership
server-side.

The mod adds no network messages of its own.

---

## How it works

The interesting part is that almost none of this needed patching.

- **`FormationFlyState`** subclasses the game's own `PilotBaseState` and is installed with
  the public `Pilot.SwitchState`, exactly as the stock AI states are. It steers through
  `Autopilot.AutoAim` — the same primitive `AIPilotCombatModes` uses — and owns only
  throttle and destination.
- **Recruiting** reads `UnitRegistry.allAircraft`, the game's live unit list.
- **Map integration** uses the public `DynamicMap.selectedIcons` / `SelectIcon` /
  `GetFactionMode` API.
- **Menu entries** are `RadialMenuAction` subclasses created at runtime. The stock
  `AllowedOnAircraft` / `TriggerAction` are not virtual, so Harmony prefixes dispatch to
  the subclass and skip the original — each prefix claims only its own type, so this
  coexists with BOTE's identical mechanism.
- **Menu icons ship as code, not files.** `IconFactory` rasterises each glyph into a
  texture at runtime with a small anti-aliased software renderer, so the mod stays a
  single DLL with no asset bundle. Glyphs are drawn white and tinted by the stock menu, so
  they inherit hover and caution colours for free. The formation icons call the real
  `FormationSolver` to place their marks, so the picker draws the actual geometry it
  selects and cannot drift out of sync with the flight code.
- **Wing symbology** postfixes `MapIcon.UpdateColor` and the private
  `HUDUnitMarker.UpdateColor` — the one place each surface assigns a colour. Because the
  stock call sites only fire on selection, theme and faction changes, the marker layer
  repaints affected units whenever membership or the engaged set changes, and reasserts
  HUD colours four times a second (a HUD marker fades in from the warning colour for its
  first second of life and is repainted whenever its track goes stale).
- **Harmony patches are listed at startup.** A patch class with no *class-level*
  `[HarmonyPatch]` is skipped in complete silence — `PatchClassProcessor` returns before it
  reads a single method attribute. Map tinting shipped that way and did nothing at all,
  with no error anywhere to say so. `Plugin.ReportPatches` now logs every patched method
  and warns about any expected one that is missing.

### Compatibility notes

- **Reflection instead of a publicizer.** The usual way to reach `actionsMain`,
  `SetupMain()` and the private `RadialMenuAction` fields is an assembly publicizer, but
  this machine's application-control policy blocks that MSBuild task from loading. The
  members are resolved once through Harmony's `AccessTools` instead. If a game update
  renames one, the mod logs a warning and disables native radial integration rather than
  throwing every frame.
- **BOTE coexistence.** BOTE swaps the wheel contents for its own submenus using the same
  technique. The injector records the main wheel on first sight and only re-adds
  *Wing Command* to wheels that still share entries with it, so it does not leak into
  another mod's submenu.
- **A stock bug this mod repairs.** `AIPilotCombatModes` subscribes its missile-alert
  handler in the *constructor* but unsubscribes it in `LeaveState`; its other three
  handlers are correctly paired with `EnterState`. Vanilla rarely leaves the combat state
  so this is mostly latent, but this mod switches pilots in and out constantly — which
  would leave wingmen permanently blind to missile warnings. The `EnterState` postfix
  removes and re-adds the handler, normalising it to exactly one subscription.

---

## Building

Requires the .NET 8 SDK.

```bash
dotnet build -c Release
```

Then deploy:

```bash
pwsh build/copy-to-game.ps1
```

Output lands in `BepInEx/plugins/WingCommand/` alongside a `meta.json` in the same format
the Nuclear Option mod manager uses.
