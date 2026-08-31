# WingCommand Loadout & Wing Info Overhaul

**Target:** `0.9.x`, after the tactical layer overhaul
**Baseline:** WingCommand `0.9.0`, Nuclear Option `0.34.2`
**Status:** implemented
**Release posture:** additive. The formation controller, defensive manoeuvres, target
deconfliction, takeover and economy invariants are not touched.

This plan follows the discipline of `TACTICAL_OVERHAUL_PLAN.md`: independently buildable
phases, native systems reused rather than reimplemented, host-authoritative only, no
big-bang UI rewrite.

> `docs/ROADMAP_PLAN.md` is referenced by the brief for this work but does not exist in the
> repository. Where a decision below needs the recruitment-cost and purchase-multiplier
> tuning it describes, it is taken against `TACTICAL_OVERHAUL_PLAN.md` §9.1/§9.2, which
> carries the same content.

## 1. Outcome

Four things the player could not do before:

- Requisition a wingman **configured for a job** rather than with whatever the faction's AI
  would have launched it with, per airframe, not per type.
- Tell a wingman **which of its own weapons to reach for first**, separately from whether it
  is allowed to shoot at all.
- See **who is flying**, how experienced they are, and what state their aircraft is in.
- Choose **what a transport is carrying** before it launches, and find out whether the
  delivery actually happened.

The WMC page grows from two tabs to four: `TACTICAL`, `SUPPLY`, `LOADOUT`, `WING`.

## 2. Product principles

Inherited from the tactical overhaul, plus two of this feature's own:

1. **Reuse the airframe's own stores.** Every loadout option comes from
   `WeaponManager.hardpointSets[i].weaponOptions` — the list the game's own aircraft
   selection menu fills its per-hardpoint dropdowns from. This mod defines no weapons.
2. **A preference is a bias, never a veto.** A preferred weapon that is empty, unready or
   out of range must fall through to the existing effectiveness ranking. Nothing the player
   can select may make a wingman hold fire when it has a valid alternative.
3. **Say what cannot be changed.** Equipment is fitted when an airframe is created. Any
   surface that implies otherwise is a lie, so the Loadout tab configures the *next*
   requisition and reports the current flight as read-only, with the reason on screen.

## 3. Decisions taken before implementation

### 3.1 Loadout pricing: presets do not change the price

**A loadout preset costs nothing extra.** A requisition is list price, exactly as it was.

The alternative — pricing heavier fits above lighter ones, as the base game prices a
configured aircraft — was rejected for this pass:

- `WingShop` deliberately moved to *one aircraft, one price* precisely because a compounding
  term meant "the number on a row was never the number you paid". Re-introducing a
  multiplier that depends on a choice made on a *different tab* would undo that, and the
  Supply row would have to print a formula again to explain itself.
- The recruitment-cost and over-limit multipliers (`RecruitmentCostPercent`,
  `ExceedSquadronLimitCost`) are still unturned. Adding a third multiplicative term against
  the same allocation pool would make all three untunable independently, which is exactly
  the double-counting the brief warns about.
- The presets are drawn from stores the airframe is already allowed to carry. Nothing here
  grants access to equipment the faction did not have.

If loadout pricing is wanted later, the hook is `WingShop.CurrentPriceOf`, and it should be
tuned in the same pass as the two existing multipliers rather than before them.

### 3.2 Pilots belong to the squadron, not to airframes

A recovered wingman's **loadout** stays with the airframe; its **pilot** goes back on the
squadron list. These are deliberately different, because the two are recovered differently:

- Return To Base destroys the aircraft and leaves an anonymous count in
  `WingSupplyReserve`, so a `persistentID` cannot carry anything across. The loadout is
  parked per type, in recovery order, and collected by the requisition that spends that
  slot (`WingLoadoutBook`).
- A pilot who lands is a person who can fly the next airframe. Binding their record to the
  destroyed aircraft would reset it at exactly the moment the mod is encouraging the player
  to bring an aircraft home.

A pilot who is **killed** is not recycled. An experience system where losses cost nothing is
a scoreboard, not a squadron.

### 3.3 Rank has a small, real effect

Rank is not cosmetic, and it is not a rebalance. At `Pilot/RankEffect = 1`, a Legend gets
roughly 12% more weapon reach and off-boresight tolerance and cycles a shot about 12%
faster than a Rookie. Both scales are one-sided: rank can only widen the envelope and only
shorten the interval, so a low-ranked pilot shoots exactly as this mod has always made
wingmen shoot. `RankEffect = 0` keeps the record and removes the mechanics entirely.

### 3.4 Active-AI assignments cannot be reconfigured

An aircraft the player assigns from the map arrives with whatever the mission gave it, and
the game fits equipment at creation. So:

- The Loadout tab reports such an aircraft as `AS FOUND` and never as a preset.
- It cannot be reconfigured at all — not "before its next relaunch", because there is no
  relaunch: a recovered assignment becomes an ordinary held reserve slot with no loadout
  parked against it, and requisitioning it afterwards is a new airframe that uses the
  planned fit like any other.

Both statements are on screen, on the Loadout tab and on the Wing tab, rather than left to
be inferred.

## 4. Phases

Each phase builds and is independently reviewable.

| # | Phase | Files |
|---|---|---|
| 1 | Data model: per-`persistentID` loadout, squadron pilot records | `WingLoadout.cs`, `WingLoadoutBook.cs`, `WingPilots.cs`, `WingKillCredit.cs` |
| 2 | Spawn and purchase paths apply the loadout | `WingShop.cs`, `WingShopDelivery.cs`, `WingRecovery.cs`, `WingSupplyReserve.cs`, `WingRegistry.cs` |
| 3 | Weapon preference in AI weapon selection | `WingWeaponPreference.cs`, `WingWeapons.cs`, `WingMember.cs` |
| 4 | `LOADOUT` tab | `WmcScreen.cs` |
| 5 | `WING` tab | `WmcScreen.cs` |
| 6 | Cargo completion, and the four-tab layout budget | `WingMember.cs`, `WingComms.cs`, `WmcScreen.cs`, `WingHud.cs` |
| 7 | Cargo drop points, and the Fire For Effect order | `CargoRunState.cs`, `AttackRunState.cs`, `WingWeapons.cs`, `WingOrderCatalog.cs`, `MapCommandLayer.cs`, `TacticalMapOverlay.cs` |

## 5. Loadout system

### 5.1 Where the options come from

`WingLoadoutCatalog` reads the airframe's prefab once per `AircraftDefinition`:

```text
AircraftDefinition.unitPrefab
  -> WeaponManager.hardpointSets[]
       -> HardpointSet.weaponOptions : List<WeaponMount>
```

Each `WeaponMount` is classified by the stations it carries, using the same
`WeaponInfo.effectiveness` (`antiAir` / `antiSurface` / `antiMissile`) and
`targetRequirements.maxRange` figures `WingWeapons.BestStationFor` already ranks live
stations with. `WeaponStation.Cargo` identifies a cargo mount.

The route from a mount prefab to its station is private, so it is resolved reflectively,
once per mount type, in the spirit of `GameAccess`: a game update that moves it degrades to
"standard fit only" with a line in the log, not to a wrong fit or an exception.

### 5.2 Presets

Five, chosen at requisition time:

| Preset | Rule over the airframe's own options |
|---|---|
| `STANDARD` | A null loadout — the airframe's own AI fit, what the faction's aircraft launch with |
| `AIR-AIR` | Highest `antiAir` on every set, longest reach breaking a tie |
| `AIR-GND` | Highest `antiSurface` on every set |
| `BALANCED` | Alternating emphasis across sets, so the aircraft is capable of both rather than mediocre at each |
| `CARGO` | The selected cargo mount on cargo-capable sets; the best self-defence store elsewhere |

A preset that arms nothing on a given airframe is not offered. A preset that *would* arm
nothing at build time falls back to the standard fit rather than launching an empty
wingman — the failure mode that once "left a whole spawned wing with no ammunition and sent
all of them straight home Winchester".

A per-station editor is an explicit non-goal.

### 5.3 Where a choice lives

| State | Key | Lives in |
|---|---|---|
| What this aircraft is carrying | `Aircraft.persistentID` | `WingLoadoutBook.aboard` |
| What the next requisition of a type will carry | `AircraftDefinition` | `WingLoadoutBook.planned` |
| What a recovered airframe came home with | `AircraftDefinition`, FIFO | `WingLoadoutBook.reserved` |

The plan is keyed by type because at the moment the player chooses it the aircraft does not
exist. It is read once, at delivery, and copied onto that one airframe — so a choice made
for one VT-7 never follows the next one.

### 5.4 Commit-after-spawn

Unchanged and load-bearing. `WingShop.Buy` resolves the choice, builds the `Loadout`, and
hands it to whichever delivery route runs. Nothing is recorded against an aircraft until
`WingShop.NoteDelivery` is called with a real one — which for a hangar delivery is when the
faction's `onRegisterUnit` fires, several seconds later. A loadout consumes nothing: it is
not a resource, and a failed build silently becomes the standard fit rather than failing
the purchase.

## 6. Preferred weapon

`WingWeaponPreference`, per member, set for the current command scope from the Tactical tab
and shown in one place only — read-only on the Wing tab, and as a column in the roster and
the docked HUD strip.

| Value | Effect |
|---|---|
| `AUTO` | Unchanged: most effective ready station for the target |
| `A-A` | Weights air contacts ×1.75 and surface ×0.6 in target selection |
| `A-G` | The mirror |
| `GUNS` | Weights an already-valid station by reach: ×2 at gun range, tapering to ×1 by 10 km |

Every value is multiplicative and applied *after* the existing target-class, range,
effectiveness and reservation gating. The damped side is never zero. Missile defence is
excluded from the station bias entirely — there is no sense in which preferring the gun for
trucks should change which interceptor answers an inbound missile.

`GUNS` reads "the gun end of the loadout" from `targetRequirements.maxRange`, so it needs no
list of weapon names and works on any airframe, stock or modded.

## 7. Wing Info and pilots

`WingPilotRoster` holds the squadron's people. Three pilots are pregenerated with a name,
callsign and one-line background; further names are generated as needed.

**A future roster system replaces `WingPilotRoster.Provide` and nothing else.** No other
file constructs a `WingPilot` or invents a callsign, so an Ace Combat-style assignment flow
with portraits and skills can hand over fully-formed records without touching assignment,
XP, rank or any panel. Building that pool, its portraits and its selection UI is explicitly
out of scope here.

XP, all tunable, all in the `Pilot` config section:

| Event | Default | Source |
|---|---|---|
| Kill | 25 | `WingKillCredit`: a contact the wingman fired on that stops existing within 25 s |
| Sortie | 40 | Recovery at base, or a completed cargo delivery |
| Engagement survived | 10 | A missile warning that cleared |

Kill attribution is an inference — the game does not expose the killer of an arbitrary unit
to a plugin — and it errs towards generosity. It cannot double-count: a target is credited
once, and every other claim on it is dropped.

Rank thresholds grow triangularly from one number (`XpPerRank`), so the whole curve moves
together: Wingman at one step, Veteran at three, Ace at six, Legend at ten.

## 8. Cargo

### 8.1 What was actually wrong

`WingMember.Apply` switched the pilot to `Pilot.AIHeloTransportState` and stopped. The stock
state configures itself — nearest airbase, nearest known ground enemy, landing zone search —
and that part is genuinely a complete supply-run behaviour for the cost of a state switch.

The gap was not the state; it was that **nothing observed the outcome**. The order never
completed. A helicopter that delivered and one that never found anywhere to put its cargo
were indistinguishable from the roster, both sat on `CARGO` indefinitely, and neither ever
rejoined.

> This was found by reading the code, not by flying it. The stock state's internals are not
> visible from a plugin build, and no claim is made here about what it does internally; the
> fix deliberately measures the aircraft rather than trusting the state either way.

### 8.2 The fix

`WingMember.CheckCargoRun`, on the existing once-a-second housekeeping pass, watches the
cargo station's own ammunition — the same field `CanDeliverCargo` gates on, and the only
ground truth available:

- Cargo falls → `"cargo away, delivery complete"`, and a sortie credited.
- Cargo reaches zero → the order completes and the wingman rejoins.
- Five minutes with the cargo still aboard → `"no drop-off available, bringing the cargo
  back"`, and the airframe is given back rather than left circling for the mission.

### 8.3 Cargo type

Chosen on the Loadout tab from the airframe's own cargo mounts. `CanDeliverCargo` is
extended, not replaced: it still requires a cargo station with ammunition, which is now the
station the player chose.

### 8.4 Drop points

Deliver Cargo takes a map point, on the same arm-then-click flow as Hold and Land, with a
cargo marker and a route line. `CargoRunState` transits to the point, then a helicopter
settles and lets down onto it while a fixed-wing transport runs in and releases overhead.

Two consequences worth stating:

- **`CanDeliverCargo` no longer requires the stock helicopter transport state.** It required
  it only because the point-less route needs it, which quietly made the whole order
  rotary-only. Nothing about a cargo station is rotary-specific. The stock state is now
  checked where that route is actually taken, and a fixed-wing transport asked to deliver
  without a point is told it needs one rather than silently doing nothing.
- **The point-less route is still reachable.** Pressing Deliver Cargo again while the cursor
  is armed abandons the point and runs the stock supply behaviour, and the status line says
  so. It remains the right answer when the player does not care where the load goes.

Whether a cargo station answers `Fire` on the ground is not provable from a plugin build, so
`CargoRunState` never assumes it worked: the station's own ammunition is the only evidence
accepted, and a delivery phase that shifts nothing in 45 seconds hands the job to the stock
transport state (saying so) rather than hovering over a field for the mission. The order is
therefore never worse than it was before drop points existed.

## 8a. Fire For Effect

A second target order beside Attack, using US fire-support nomenclature for what it is: stop
adjusting, put everything on it.

Attack is deliberately measured — designations spread across the wing, a useful-attacker cap
per contact, surplus aircraft held as cover, seconds between launches. Those are the right
defaults and none of them change. Fire For Effect is the case they were never meant to
govern:

| | Attack | Fire For Effect |
|---|---|---|
| Designations | Spread across the scope | Every wingman onto one |
| Concurrency cap | `MaxWingmenPerTarget` | None |
| Interval between launches | `FireInterval`, ~5 s | 0.8 s |
| Weapon preference | Honoured | Ignored — the order already answered that question |
| Ends when | Target dead or invalid | Target dead, or nothing aboard can still hurt it |

What it keeps is the weapon/target matching and the shot envelope, so a wingman works down
through its missiles, then its rockets, then its gun as each runs dry rather than throwing
anti-air missiles at a tank. "Everything it has" means everything that can do the job — the
difference between the order being rad and the order being broken. Bingo and Winchester still
recall it, and the leash still applies.

It shares `AttackRunState`'s flying entirely; the difference is one flag and which
`WingWeapons` entry point the run calls. It is not on the radial: six slices is the plan's
cap, and this is a considered decision rather than a quick call.

## 9. Four-tab layout budget

Two tabs became four on the same bezel, so the Tactical page had to give height back before
it could take any. It is now **shorter** than it was:

| | Before | After |
|---|---:|---:|
| Tactical | 640 px | 568 px |
| Supply | ~430 px | ~450 px |
| Loadout | — | ~450 px |
| Wing | — | ~445 px |

Where it came from:

- Roster viewport 4 rows → 3, matching the default `MaxWingSize`. A permanently empty
  fourth row was the cheapest 32 px available; larger wings still page.
- `RULES OF ENGAGEMENT` and `FORMATION` headings and the standing ROE hint line collapsed
  into one `ENGAGEMENT` block with a left gutter. Three headings were labelling three rows
  that all answer the same question.
- The per-choice explanations moved to the status line at the foot of the page, where only
  the one in force is shown and it has the width to be a sentence. The map keeps first claim
  on that line: an armed point order or a pending assignment fee outranks a hint.
- The order block went from two columns to three when Fire For Effect made the set nine. A
  fifth row of pairs would have cost more height than the new tabs left; a three-by-three
  grid holds all nine in three rows and hands another 34 px back.

Each page is still sized to its own content, and only the visible page is refreshed — which
matters now that rebuilding the requisition catalogue walks the faction's supply dictionary
and would otherwise be paid five times a second while the player reads the roster.

## 10. Release gates

Re-checked against `TACTICAL_OVERHAUL_PLAN.md` §7 and §15.

- [x] No page taller than the pre-existing Tactical page, so 720p is no worse than the
      shipping build. **Needs a visual pass at 1280×720 and at UI scale above 100%.**
- [x] No per-frame string allocation: every panel refreshes on the existing 0.2 s throttle,
      and only for the visible page.
- [x] Active controls in the stock accent colour; only unavailable ones dim.
- [x] Every unavailable control has a reason on the status line — no airframe selected, no
      stock stores, not a transport, station data unreadable.
- [x] Host/single-player only. Nothing new writes world state off the server; the Wing tab
      is read-only and says when an aircraft is not locally simulated.
- [x] No new network messages, no new Harmony patches.
- [ ] Controller reachability of the two new tabs and their selectors. The widgets are the
      same `WingButton` pointer handlers the existing tabs use, so this should carry over
      unchanged — but it is a playtest item, not a code claim.

## 11. Out of scope

- A pregenerated wingman pool with portraits and an assignment screen. Backbone only.
- A per-weapon-station loadout editor.
- Multiplayer sync of loadout or pilot state.
- Any balance tuning beyond keeping loadout pricing and rank effects non-broken.
