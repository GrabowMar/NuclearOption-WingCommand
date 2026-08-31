# WingCommand

**Tactical AI wing command for Nuclear Option.** Select individual wingmen on the tactical
map, issue scoped combat orders, and manage paid aircraft assignments, requisitions, and
player reserves without turning Nuclear Option into an RTS.

| | |
|---|---|
| Current release | `0.9.0` |
| Game version | Nuclear Option `0.34.2` |
| Requirements | BepInEx `5.4.23.3` or newer |
| Supported play | Single-player and multiplayer host |
| Licence | MIT |

> [!IMPORTANT]
> WingCommand is in active development. Balance, controls, configuration, and AI behaviour
> may change between releases. The squadron shop can make missions substantially easier.

> [!NOTE]
> Nuclear Option's AI pilots are server-authoritative. WingCommand works correctly in
> single-player and for the multiplayer host; non-host clients cannot reliably control AI.

## Highlights

- Command the whole wing quickly from the native radial, or select individual wingmen in WMC.
- Requisition wingmen with a chosen loadout, kept per airframe from purchase through recovery.
- Tell a wingman which of its own weapons to reach for first, separately from its ROE.
- Read live aircraft and pilot state, and a callsign, rank and record that grow with the mission.
- Click wing icons on the tactical map to build a command scope independent of weapon targets.
- Place Hold and Land orders directly on the map and read persistent task markers at a glance.
- Choose from six distinct release-facing formations with smooth shape transitions.
- Wingmen react to turns, nearby threats, closing traffic, terrain, and missile warnings.
- After pilot death or ejection, take control of a surviving aircraft from your wing.
- Seeker-aware defensive manoeuvres use the appropriate countermeasures, then resume the
  interrupted order.
- Target reservations spread attacks across useful contacts instead of dog-piling one unit
  or repeatedly concentrating every hostile aircraft on the player.
- Rules of engagement separate where a wingman flies from what it is allowed to shoot.
- Reassign active AI for a confirmed allocation fee or requisition compatible faction stock.
- Hold up to three concrete airframes for the wing; recovered requisitions return to that
  same reserve without being charged twice.
- High-contrast wing and target symbology stays consistent between the HUD and map.

## Installation

### NOMM

Search for **WingCommand** in [NOMM](https://github.com/Combat787/NOMM) and install it.
NOMM handles BepInEx and future updates.

### Manual installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option
   directory and launch the game once.
2. Download `WingCommand.dll` from the
   [latest release](https://github.com/GrabowMar/NuclearOption-WingCommand/releases).
3. Copy it to:

   ```text
   Nuclear Option/BepInEx/plugins/WingCommand/WingCommand.dll
   ```

4. Launch the game and check `BepInEx/LogOutput.log` for:

   ```text
   Harmony patched ... method(s)
   WingCommand 0.9.0 loaded.
   ```

The optional `WingCommand-0.9.0.zip` mirrors the game directory and can instead be
extracted at the Nuclear Option root. Keep only this organized plugin path; a loose
`BepInEx/plugins/WingCommand.dll` copy can make BepInEx load the wrong build.

Settings are generated at:

```text
Nuclear Option/BepInEx/config/com.marci.wingcommand.cfg
```

They can also be edited in-game with
[BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
using **F1**.

## Quick start

1. Enter a mission as the host or in single-player.
2. Open **WMC > Supply**. Select friendly AI using the stock map selection, then press
   **Assign Selected** twice: the first press quotes the total fee and the second confirms it.
3. Switch to **WMC > Tactical**. Click a roster row or wing icon; Shift-click builds a
   multi-aircraft scope and **Select All** restores whole-wing scope.
4. Issue an order. **Hold Here** and **Land Here** arm the map cursor; click the destination.
5. Before requisitioning, open **WMC > Loadout** to choose the fit that airframe launches
   with — and, for a transport, what it is carrying.
6. Use the compact **Wing Command** radial when you need immediate whole-wing combat orders.

If your pilot is killed or ejects while wingmen survive, WingCommand holds them in safe
orbits and opens a takeover window. Selecting one spawns a fresh player-controlled copy at
its position with the same airframe, loadout, fuel, livery and motion, then removes the AI
source. This avoids reusing its AI/network state. You may instead use the normal respawn or
defeat flow. The recovery panel opens over the tactical map and supports number-key selection.
Replacement is available in single-player and to the host.

Fixed-wing and rotary-wing aircraft cannot share a formation. Recruitment and the shop
automatically filter incompatible aircraft.

## Command interfaces

### Native radial menu

Open the game's normal radial menu and select **Wing Command**. The submenu uses the same
look controls and bindings as the stock wheel.

```text
Wing Command
├─ Form Up
├─ Attack My Target
├─ Engage
├─ Disengage
├─ Rules Of Engagement
└─ Tasking
   ├─ Hold Position
   ├─ Return To Base
   └─ Formation
      ├─ Echelon Right
      ├─ Line Abreast
      ├─ Trail
      ├─ Combat Spread
      ├─ Finger Four
      └─ Vic
```

Radial and direct hotkey orders always apply to the whole wing. Recruitment, release, and
requisition are intentionally absent. The fallback radial and direct Form Up/Engage hotkeys
remain available as advanced settings and are unbound by default.

### WMC MFD page

The **WMC** page has four focused tabs:

- **Tactical:** paged roster, independent command selection, ROE, preferred weapon, six core
  formations, scoped orders, capability-gated Cargo/Land controls, and map-order status.
- **Supply:** funds, wing size and squadron capacity at a glance; the faction catalogue at
  flat list prices, including the loadout each requisition will carry; confirmed active-AI
  assignment; and a concrete three-airframe reserve.
- **Loadout:** the fit the next requisition of an airframe launches with, the cargo a
  transport carries, and what every wingman already in the air is carrying.
- **Wing:** fuel, ammunition, cargo, hull state and current order for one wingman, with its
  pilot's callsign, rank, record and background.

### Tactical map

| Input | Action |
|---|---|
| Click a wing icon while **WMC > Tactical** is open | Select only that wingman |
| Shift-click another wing icon or roster row | Add/remove it from command scope |
| **Select All** | Restore whole-wing WMC scope |
| Press **Hold Here**, **Land Here** or **Deliver Cargo**, then click map | Place the selected order |
| Right-click map with explicitly selected wingmen | Move those wingmen to the point and show a `MOVE` marker |
| Shift-right-click map | Queue another move point |
| Right-click or Escape while an order is armed | Cancel it |

Hostile, friendly non-wing, ground, and naval icon behavior remains stock. Closing WMC or
switching to any other tab stops intercepting wing-icon clicks.

Map moves are temporary routes. On the final point, Defend/Hold and Escort wingmen return
to formation; Free wingmen hand off to Engage and may resume autonomous combat.

## Orders

| Order | Behaviour |
|---|---|
| **Form Up** | Close on assigned slots and hold station on the player |
| **Attack My Target** | Radial command assigns every wingman an attack; scoped WMC attacks distribute contacts and may retain surplus cover |
| **Fire For Effect** | Every selected wingman expends on one designated target until it is dead or they are dry |
| **Engage** | Hunt autonomously within the configured leash and return when too far away |
| **Disengage** | Break on separated headings, use countermeasures, egress, then form up again |
| **Hold Here** | Hold a combat air patrol around the selected point while continuing to apply ROE |
| **Deliver Cargo** | Fly cargo to a chosen map point and put it down there, then report the delivery and rejoin |
| **Land Here** | Set compatible helicopters down near the order point |
| **Return To Base** | Fly the stock landing pattern home, then hand the airframe back to faction stock |
| **Formation dial** | Choose one of the six core formation geometries from the radial Tasking menu |

Orders are persistent. A temporary defensive reaction, fuel state, or Engage leash recall
does not silently erase the task the player selected.

**Return To Base completes.** Once the wingman is down and shut down at a friendly airbase,
its airframe leaves the world and enters the three-slot wing reserve. A purchased airframe
stays owned there and can be launched again without paying twice; an assigned mission
airframe becomes a held reserve slot. Host or single-player only; set
`Engagement/RtbReturnsToReserve` to `false` to leave recovered aircraft parked instead.

## Rules of engagement

An order controls **where the wingman flies**. ROE controls **what it may shoot**.

| ROE | Leaves formation? | Weapons policy | When the player is attacked |
|---|---|---|---|
| **Defend** (`Hold` in config) | No | Incoming missiles, self-defence, and mirrored ground attacks | Attempts to intercept the missile |
| **Escort** | No | Weapons free from the assigned slot, prioritising threats to the leader | Engages the launching aircraft while covering |
| **Free** | Only for emergencies | Weapons free | May break formation and pursue the launcher |

Every wingman may temporarily leave its slot to defend itself from an immediate missile
warning, regardless of ROE.

## Reactive formation system

WingCommand exposes six core formation shapes:

| Shape | Character |
|---|---|
| **Echelon Right** | Swept tactical line with element staggering |
| **Line Abreast** | Broad sensor and weapons frontage |
| **Trail** | Compact column with alternating vertical separation |
| **Combat Spread** | Wide mutual-support spacing |
| **Finger Four** | Asymmetric four-ship elements that repeat for larger wings |
| **Vic** | Balanced V geometry for compact groups |

Echelon Left, Diamond, Ladder, and Wall remain supported for old configuration files, but
are not shown in the normal selector. Choosing any core shape returns the selector to the
compact six-shape cycle.

Formation slots are calculated in leader-local space and transition gradually when the
shape or spacing changes. During hard turns, lateral spacing compresses and trail depth
increases so inside and outside wingmen are not given impossible speed demands.

Separation predicts the closest approach over the next four seconds. It reacts before two
aircraft overlap, adds vertical deconfliction near terrain, and keeps avoidance corrections
bounded so emergency steering does not destabilise the formation. Fixed-wing speed requests
respect the airframe's own limits, and bank commands are capped below inversion.

Threat spacing activates only when a missile warning exists or hostile aircraft are close.
The formation widens smoothly, then settles back to its selected geometry when the threat
clears.

## AI coordination and self-preservation

### Target deconfliction

Locally simulated AI shares short-lived target reservations. Stock opportunity, threat,
weapon, range, and bravery calculations still decide whether a contact is useful, while
existing commitments encourage the next pilot to select a comparable unsaturated target.

- Follow-on attackers increasingly prefer another worthwhile unsaturated contact.
- Explicit wing attacks estimate how many aircraft are useful and retain the rest as cover.
- Missile defence assigns one interceptor to an inbound missile instead of wasting the
  entire wing's weapons on it.

This reduces dog-piling without making the player or any other target artificially immune.

### Defensive panic system

When a wingman receives its own missile warning it calls the threat and enters a temporary
defensive state:

- Radar-guided threats trigger a terrain-aware beam/notch, speed, chaff, and jamming when
  available.
- Infrared threats trigger an away/beam manoeuvre, reduced power, and flares.
- Unknown seekers use a conservative mixed response.
- Fixed-wing and rotary-wing aircraft receive different steering logic.

After the warning remains clear for the configured interval, the wingman announces that it
is clear and resumes the queued order—including an order issued while it was defensive.
The WMC and compact roster show `DEFENSIVE` / `DEF` during the interrupt.

## Squadron supply

The WMC Supply tab uses Nuclear Option's existing economy rather than creating a separate one:

- **One aircraft, one price.** An airframe costs its list value, the same number the player's
  own aircraft menu prices from. Nothing compounds with wing size.
- Reassigning an active AI costs a flat 25% of that list value by default; releasing and
  reassigning the same persistent aircraft does not charge twice.
- Price is paid from player allocation; declared aircraft consume the faction's mission supply.
- Rank, mission restrictions, and fixed-wing/rotary compatibility are respected.
- Requisitioned aircraft launch from a friendly airbase and fly to the wing under their own
  power. Where a hangar on the field stocks that airframe, the game's own airbase spawn is
  used: the aircraft appears in the hangar, waits out any door sequence, taxis and takes off
  exactly as the faction's own aircraft do. It appears in your wing roster immediately as
  departing and becomes commandable once airborne. Airframes
  no hangar stocks are delivered into the circuit overhead instead.
- Undeclared-aircraft stock remains an advanced compatibility option, disabled by default.
- The WMC **Wing Reserve** holds up to three specific airframes across all types. `HOLD`
  moves one selected faction airframe out of AI-accessible stock; `RELEASE` returns it.
  The reserve does not create supply and does not multiply by aircraft type.
- A requisition that has already been paid for is marked owned. When it completes Return To
  Base, that airframe returns to the same reserve and can be launched again without another
  purchase. Recovered aircraft that were only active assignments return as ordinary held
  reserve and are charged normally when requisitioned again.

### Squadron capacity

Missions cap how many AI aircraft a faction may have airborne, and that cap shrinks for each
friendly player — single-player missions routinely leave a limit of zero. The Supply tab shows
it permanently as `SQUADRON active / limit`, so a shop that cannot sell you anything says why
instead of failing on a toast that has already gone.

**OVER LIMIT** grants permission to requisition past that cap at **3× list price**, needs
**rank 3**, and allows at most **3 over-limit airframes in the air at once**. It is permission
rather than a mode: while the squadron has room it changes nothing, and the surcharge only
applies to a purchase that actually exceeds the limit.

The allowance counts *your* over-limit purchases that are still flying, not how far the
faction as a whole exceeds its cap — missions script in AI regardless of the limit, and
charging you for that would lock the shop on exactly the missions this exists to rescue. It
frees up as those aircraft are lost or recovered. The three numbers are
`Shop/ExceedSquadronLimitCost`, `ExceedSquadronLimitRank` and `ExceedSquadronLimitAllowance`.

Every assignment previews its fee. Credits and supply move only after recruitment or spawn
succeeds.

## Loadouts

A requisition is configured before it launches, on **WMC > Loadout**.

- Options come from the airframe's own weapon stations — the same stores the game's aircraft
  selection menu offers for that hardpoint. WingCommand adds no weapons of its own.
- Five fits: `STANDARD` (the airframe's own AI loadout), `AIR-AIR`, `AIR-GND`, `BALANCED`,
  and `CARGO` for transports. A fit an airframe has no stores for is not offered.
- The choice belongs to the airframe, not the type. Configuring one VT-7 does not configure
  the next one you buy.
- Equipment is fitted when an aircraft is created, so a wingman already in the air cannot be
  reconfigured. The Loadout tab shows what each one is carrying and says so.
- An active mission aircraft you assign from the map flies **as found**. It arrives with
  whatever the mission gave it and cannot be refitted.
- Return To Base keeps the fit. A recovered airframe goes into the wing reserve carrying
  what it came home with, and launches that way again.

Loadouts do not change what an airframe costs. A requisition is list price, exactly as before.

### Cargo

For a transport, the Loadout tab also chooses **what it is carrying**, from the cargo mounts
the airframe itself offers.

**Deliver Cargo takes a drop point.** Press it, then click the map, exactly as Hold Here and
Land Here work; a cargo marker appears at the point and a line runs to it from every wingman
on the run. Helicopters descend and set the load down there, fixed-wing transports run in and
release over it. Any airframe carrying a load can be given the order — it is no longer
helicopter-only.

Press **Deliver Cargo** a second time while the cursor is armed to give up the point and use
the game's own supply route instead, which is what the order did before.

A cargo run now finishes either way: the wingman calls the delivery when the cargo actually
leaves the aircraft and rejoins when it is empty, and one that cannot put its load down says
so and brings it back instead of circling for the rest of the mission.

## Fire For Effect

**Attack** is measured. It spreads designations across the wing, caps how many aircraft are
useful against one contact, holds surplus wingmen back as cover, and leaves several seconds
between launches so nobody empties themselves on a truck.

**Fire For Effect** is the other thing. Every selected wingman goes after the same designated
target and keeps shooting — no concurrency cap, no long cooldown — working down through
missiles, then rockets, then guns as each runs dry, until the target is dead or there is
nothing left aboard that could hurt it. Then they call `expended` and rejoin.

What it does *not* drop is weapon/target matching: a station still has to be effective
against that kind of target and the shot still has to be inside the weapon's own envelope, so
this expends a loadout on something worth expending it on rather than throwing air-to-air
missiles at a tank. Bingo fuel and Winchester still send a wingman home.

Designate a target as usual, select your scope, and press **Fire For Effect** on
**WMC > Tactical**. The roster shows `FFE`. It is deliberately not on the radial: it is a
considered decision, not a quick call.

## Preferred weapon

An order says where a wingman flies. ROE says what it may shoot. **Preferred weapon** says
which of its own stores it reaches for first.

| Setting | Effect |
|---|---|
| **AUTO** | The most effective ready station for the target |
| **A-A** | Prefers hostile aircraft and anti-air stores |
| **A-G** | Prefers surface contacts and anti-surface stores |
| **GUNS** | Prefers close-in stores, saving standoff weapons |

Set it per selection on **WMC > Tactical**; it shows in the roster and the compact HUD strip
beside the order. Every setting is a bias, never a restriction: a preferred store that is
empty, unready or out of range falls back to the usual choice, and no setting can make a
wingman hold fire when it has a valid alternative.

## Pilots

Each wingman is flown by someone. **WMC > Wing** shows their callsign, name, background,
rank and record alongside the aircraft's fuel, ammunition, cargo and hull state.

- Pilots belong to the squadron. One who lands, or whose aircraft you release, goes back on
  the list with their record and flies the next airframe you requisition.
- A pilot who is killed does not come back.
- Experience comes from kills, completed sorties and engagements survived, and rank rises
  through Rookie, Wingman, Veteran, Ace and Legend.
- Rank has a small real effect: at the top it is worth roughly 12% more weapon reach and
  off-boresight tolerance and about 12% faster shot cycling. Set `Pilot/RankEffect` to `0`
  to keep the record and remove the mechanics.

This is a backbone. A pregenerated wingman pool with portraits and an assignment screen is a
later feature; three pilots are written by hand and the rest are generated.

## HUD and map symbology

- Wingmen use a configurable high-contrast green marker and map caret.
- Active wing targets use amber markers.
- Selected members brighten without losing aircraft type or heading information.
- The compact roster shows order/state and live slot error, and hides while the map is open.
- On the maximised map, a line runs from every tasked wingman to the point it is flying to.
  A Shift-queued route is drawn as a chain, with the leg being flown at full strength, the
  queue behind it dimmed, and a dot at each pending point. An attack draws to its target in
  the same amber the target marker uses.

Slot error is useful when tuning formation flight:

| Reading | Meaning |
|---|---|
| Small and steady | The aircraft is established in its slot |
| Repeated rise and fall | Correction or throttle gain may be too aggressive |
| Continually increasing | The wingman cannot keep up or is no longer under local control |
| `-` | The wingman is not currently on a formation order |

## Configuration reference

The ordinary ConfigurationManager view is intentionally limited to the release-facing
settings below. Controller gains, integration toggles, compatibility features, colors, and
diagnostics remain under **Advanced settings**; retired keys are hidden but still parse old
configuration files.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Formation | `Shape` | `EchelonRight` | Initial formation |
| Formation | `MaxWingSize` | `3` | Maximum recruited wingmen |
| Engagement | `DefaultRoe` | `Hold` | Initial rules of engagement |
| Engagement | `AutoReturnOnEmpty` | `true` | Automatic RTB on Winchester or bingo |
| Engagement | `BingoFuel` | `0.15` | Automatic return fuel fraction |
| Engagement | `RtbReturnsToReserve` | `true` | Recovered wing airframes return to wing reserve |
| Engagement | `TakeoverOnDeath` | `true` | Offer a surviving wing aircraft after pilot loss |
| Shop | `RecruitmentCostPercent` | `0.25` | Active-AI reassignment fee, as a fraction of list value |
| Shop | `ExceedSquadronLimitCost` | `3` | Price multiplier past the mission's AI aircraft limit |
| Shop | `ExceedSquadronLimitRank` | `3` | Rank required to exceed that limit |
| Shop | `ExceedSquadronLimitAllowance` | `3` | Over-limit airframes you may have flying at once |
| Pilot | `PilotProgression` | `true` | Wing pilots keep a record, a rank and a small skill effect |
| Pilot | `RankEffect` | `1` | How much rank changes shooting; `0` makes rank a record only |
| Pilot | `XpPerRank` | `120` | Experience step between ranks |
| Comms | `RadioChatter` | `true` | Wing order and state reports |
| UI | `ShowWingHud` | `true` | Compact roster docked beside the tactical map |
| Debug | `FreePlanePurchases` | `false` | Free requisitions; **probably breaks the mod** |
| Debug | `DisableWingSizeLimit` | `false` | Ignore `MaxWingSize`; **probably breaks the mod** |

The two Debug cheats are exposed only as F1 ConfigurationManager options. They are
deliberately off by default, unsupported for balance and large-wing layout, and can break
mission scripting or the mod itself.

Global AI `SkillScale`/`BraveryScale`, player-specific target protection, `WingPriceGrowth`,
`RecruitRange`, `AdditionalWingReservePerType`, and the fast-delivery keys are retired and
ignored, including when present in an older config.

## Troubleshooting

### Wing Command does not appear

- Confirm `WingCommand.dll` is inside `BepInEx/plugins/`.
- Remove duplicate or older WingCommand DLLs from other plugin subdirectories.
- Check `LogOutput.log` for the version and Harmony patch lines shown above.
- If native radial integration fails after a game update, configure the fallback radial key.

### Aircraft ignore orders

- Confirm you are playing single-player or hosting the multiplayer session.
- Fixed-wing and rotary-wing aircraft cannot join the same formation.
- Cargo and Land Here apply only to compatible helicopters.
- Aircraft that are landing, destroyed, or no longer locally simulated cannot be controlled.

### Formation feels unstable

- Start with the default `Aggression`, `Damping`, and `ThrottleGain` values.
- Check whether the leader is exceeding the wingman's performance envelope.
- Use WMC slot error and enable `Debug/VerboseLogging` before reporting a reproducible issue.

Please report bugs and balance feedback through
[GitHub Issues](https://github.com/GrabowMar/NuclearOption-WingCommand/issues). Include the
game version, aircraft type, order, formation, and relevant `LogOutput.log` section.

## Compatibility and implementation

WingCommand adds no custom network messages. It drives aircraft through the game's existing
pilot states and autopilot interfaces, uses the stock economy and supply calls, and limits
Harmony patches to UI dispatch, marker colour, missile-warning repair, and AI target
deconfliction.

The mod is designed to coexist with BOTE's radial submenus. Private game members required by
the native radial integration are resolved through Harmony reflection; if a game update
renames one, the mod logs the problem and leaves the fallback interface available.

Much of WingCommand has been developed with AI coding assistance under maintainer direction,
review, and live flight testing. Contributions and detailed test reports are welcome.

## Building

Requires the .NET 8 SDK, BepInEx 5, and a local Nuclear Option installation at the default
Steam path referenced by the project.

```powershell
dotnet build src/WingCommand/WingCommand.csproj -c Release
```

Build output:

```text
src/WingCommand/bin/Release/netstandard2.1/WingCommand.dll
```

Create release assets:

```powershell
pwsh build/package.ps1
```

This produces:

```text
dist/WingCommand.dll
dist/WingCommand-0.9.0.zip
```

Attach **`WingCommand.dll` first** to a GitHub release. NOMM installs the bare plugin DLL;
the ZIP is provided for manual installation. The package script reads the version directly
from the built assembly and prints SHA-256 hashes for both assets.

## Licence

[MIT](LICENSE)
