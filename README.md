# WingCommand

**Tactical AI wing command for Nuclear Option.** Recruit or purchase friendly aircraft,
fly ten reactive formations, coordinate attacks, and manage your wing from the cockpit,
WMC display, or tactical map.

| | |
|---|---|
| Current release | `0.8.3` |
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

- Command a persistent wing through the native radial menu, WMC MFD page, and tactical map.
- Choose from ten formations with smooth shape transitions and element-based geometry.
- Wingmen react to turns, nearby threats, closing traffic, terrain, and missile warnings.
- After pilot death or ejection, take control of a surviving aircraft from your wing.
- Seeker-aware defensive manoeuvres use the appropriate countermeasures, then resume the
  interrupted order.
- Target reservations spread attacks across useful contacts instead of dog-piling one unit
  or repeatedly concentrating every hostile aircraft on the player.
- Rules of engagement separate where a wingman flies from what it is allowed to shoot.
- Recruit aircraft already in the mission or purchase compatible aircraft from faction
  supply.
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
   Nuclear Option/BepInEx/plugins/WingCommand.dll
   ```

4. Launch the game and check `BepInEx/LogOutput.log` for:

   ```text
   Harmony patched 12 method(s)
   WingCommand 0.8.3 loaded.
   ```

The optional `WingCommand-0.8.3.zip` mirrors the game directory and can instead be
extracted at the Nuclear Option root. Do not install both copies.

Settings are generated at:

```text
Nuclear Option/BepInEx/config/com.marci.wingcommand.cfg
```

They can also be edited in-game with
[BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
using **F1**.

## Quick start

1. Enter a mission as the host or in single-player.
2. Add aircraft using one of these methods:
   - Select friendly AI aircraft on the maximised map and **right-click**.
   - Open the radial menu and choose **Wing Command > Wing > Recruit Nearest**.
   - Open the **WMC** MFD page and recruit, add selected aircraft, or buy a wingman.
3. Open **Wing Command** from the normal radial menu and issue an order.
4. Choose a formation and rules of engagement for the situation.

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
├─ Orders
│  ├─ Rejoin
│  ├─ Engage
│  ├─ Fall Back
│  └─ Tasking
│     ├─ Return To Base
│     ├─ Orbit Here
│     ├─ Deliver Cargo
│     └─ Land Here
├─ Attack My Target
├─ Formation
├─ Rules Of Engagement
└─ Wing
   ├─ Recruit Nearest
   └─ Disband
```

The fallback radial and direct Rejoin/Engage hotkeys are available in configuration but
unbound by default.

### WMC MFD page

The **WMC** page provides the complete wing interface:

- Current wing roster, slot, order, state, and live formation error.
- Recruit, purchase, and release controls.
- Formation, tasking, ROE, and per-wing orders.
- Squadron supply catalogue with base or fast delivery.

### Tactical map

| Input | Action |
|---|---|
| Select friendly aircraft, then **right-click** | Add the selected aircraft to your wing |
| **Ctrl+1** through **Ctrl+4** | Store the current map selection |
| **1** through **4** | Recall a stored selection |

Selection groups also work with ground and naval units, preserving the game's normal move
orders.

## Orders

| Order | Behaviour |
|---|---|
| **Rejoin Formation** | Close on assigned slots and hold station on the player |
| **Attack My Target** | Distribute the selected contacts across the useful number of attackers; surplus aircraft cover the wing |
| **Engage** | Hunt autonomously within the configured leash and return when too far away |
| **Fall Back** | Break on separated headings, use countermeasures, and withdraw toward friendly territory |
| **Orbit Here** | Hold a combat air patrol around the point where the order was issued |
| **Deliver Cargo** | Use the game's supply behaviour with compatible transport helicopters |
| **Land Here** | Set compatible helicopters down near the order point |
| **Return To Base** | Hand the aircraft to the stock landing state |
| **Change Shape** | Cycle the formation geometry |
| **Disband** | Release the wing back to normal game AI |

Orders are persistent. A temporary defensive reaction, fuel state, or Engage leash recall
does not silently erase the task the player selected.

## Rules of engagement

An order controls **where the wingman flies**. ROE controls **what it may shoot**.

| ROE | Leaves formation? | Weapons policy | When the player is attacked |
|---|---|---|---|
| **Hold** | No | Incoming missiles, self-defence, and mirrored ground attacks | Attempts to intercept the missile |
| **Escort** | No | Weapons free from the assigned slot, prioritising threats to the leader | Engages the launching aircraft while covering |
| **Free** | Only for emergencies | Weapons free | May break formation and pursue the launcher |

Every wingman may temporarily leave its slot to defend itself from an immediate missile
warning, regardless of ROE.

## Reactive formation system

WingCommand provides ten formation shapes:

| Shape | Character |
|---|---|
| **Echelon Right / Left** | Swept tactical line with element staggering |
| **Line Abreast** | Broad sensor and weapons frontage |
| **Trail** | Compact column with alternating vertical separation |
| **Combat Spread** | Wide mutual-support spacing |
| **Finger Four** | Asymmetric four-ship elements that repeat for larger wings |
| **Vic** | Balanced V geometry for compact groups |
| **Diamond** | Four-ship diamonds with additional elements behind |
| **Ladder** | Progressive trail and altitude separation |
| **Wall** | Wide, shallow frontage with element depth |

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

- The first attacker receives no player-target penalty.
- Follow-on attackers increasingly prefer another worthwhile contact.
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

The WMC shop uses Nuclear Option's existing economy rather than creating a separate one:

- Price comes from the aircraft definition and is paid from player allocation.
- Declared aircraft consume the faction's mission supply.
- Rank, mission restrictions, faction aircraft limits, and compatibility are respected.
- Modded aircraft not declared by the mission can receive a small configurable allowance.
- Each existing wingman compounds the next aircraft's price.
- **Base delivery** is cheaper but requires the aircraft to fly from an airbase.
- **Fast delivery** spawns the purchased aircraft behind the player for a surcharge.

Credits and supply are consumed only after the aircraft is successfully created.

## HUD and map symbology

- Wingmen use a configurable high-contrast green marker and map caret.
- Active wing targets use amber markers.
- Selected members brighten without losing aircraft type or heading information.
- The compact roster shows order/state and live slot error, and hides while the map is open.

Slot error is useful when tuning formation flight:

| Reading | Meaning |
|---|---|
| Small and steady | The aircraft is established in its slot |
| Repeated rise and fall | Correction or throttle gain may be too aggressive |
| Continually increasing | The wingman cannot keep up or is no longer under local control |
| `-` | The wingman is not currently on a formation order |

## Configuration reference

All settings include descriptions in `com.marci.wingcommand.cfg`. These are the most useful
release defaults:

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Formation | `Shape` | `EchelonRight` | Initial formation |
| Formation | `SlotSpacing` | `120` m | Base horizontal spacing |
| Formation | `SlotStack` | `20` m | Base vertical spacing |
| Formation | `MaxWingSize` | `3` | Maximum recruited wingmen |
| Formation | `RecruitRange` | `12000` m | Recruitment search radius |
| Formation | `ReactiveThreatWidenScale` | `1.45` | Spacing multiplier under threat |
| Formation | `Aggression` | `1.0` | Overall steering and closure authority |
| Formation | `Damping` | `1.0` | Arrests corrections before overshoot |
| Formation | `CommandAngle` | `25°` | Maximum station-keeping heading correction |
| Formation | `SafePursuitBankDegrees` | `88°` | Hard-limited rejoin bank authority |
| AI | `TargetDeconfliction` | `true` | Share short target reservations |
| AI | `TargetSaturationPenalty` | `1.5` | Prefer comparable unsaturated targets |
| AI | `PlayerConcentrationPenalty` | `2.5` | Reduce repeated follow-on focus on a player |
| AI | `PanicSystem` | `true` | Enable temporary defensive reactions |
| AI | `PanicClearSeconds` | `2.5` s | Clear interval before resuming an order |
| AI | `MutualSupport` | `true` | Allow wingmen to respond to threats to one another |
| Engagement | `DefaultRoe` | `Hold` | Initial rules of engagement |
| Engagement | `MissileDefence` | `true` | Permit interception of incoming missiles |
| Engagement | `LeashRadius` | `8000` m | Maximum autonomous Engage distance |
| Engagement | `MaxWingmenPerTarget` | `2` | Explicit attacker cap per target |
| Engagement | `BingoFuel` | `0.15` | Automatic return fuel fraction |
| Engagement | `TakeoverOnDeath` | `true` | Offer a surviving wing aircraft after pilot loss |
| Shop | `WingPriceGrowth` | `1.5` | Compounding price per existing wingman |
| Shop | `FastDeliverySurcharge` | `0.25` | Fast-delivery price increase |

Optional global AI `SkillScale` and `BraveryScale` adjustments remain disabled by default.
They affect all locally simulated aircraft on both factions, not only the player's wing.

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
dist/WingCommand-0.8.3.zip
```

Attach **`WingCommand.dll` first** to a GitHub release. NOMM installs the bare plugin DLL;
the ZIP is provided for manual installation. The package script reads the version directly
from the built assembly and prints SHA-256 hashes for both assets.

## Licence

[MIT](LICENSE)
