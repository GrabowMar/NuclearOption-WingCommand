# WingCommand

Lead your own flight of AI wingmen in **Nuclear Option**. Recruit or buy aircraft, put them
in formation on your wing, and order them around — from a radial menu, a cockpit MFD screen,
or the tactical map.

Built against game `0.34.2`, Unity 2022.3.62f2 (Mono), BepInEx 5.4.23.3.

> [!NOTE]
> **First time?** Jump to [Quick start](#quick-start) — you can be flying with a wing in about a minute.

---

## Quick start

1. **Install** the mod ([NOMM](#with-nomm-recommended) or [manually](#manually)).
2. Fly some mission where your side has friendly AI aircraft.
3. **Open the radial menu** and pick **Wing Command ▸ Wing ▸ Recruit Nearest** — the closest
   friendly AI joins your wing.
4. They'll form up on your wing and hold station. Use **Wing Command ▸ Orders** to tell them
   what to do.

<!-- IMAGE: formation flying a few wingmen -->

---

## What you get

### A wing that flies with you
Wingmen hold a formation slot on your aircraft and stay there through climbs, turns and
whatever you throw at them — fixed-wing and helicopters alike. Ten formation shapes:
Echelon Left/Right, Line Abreast, Trail, Combat Spread, Finger Four, Vic, Diamond, Ladder and
Wall.

### Orders
| Order | What it does |
|---|---|
| **Rejoin Formation** | Wingmen close on their slot and hold station |
| **Attack My Target** | Fly an attack run on what you've designated, then return to the wing |
| **Engage** | Hunt on their own, but stay within a leash of you |
| **Fall Back** | Scatter on separate headings with flares, run for the nearest airbase |
| **Orbit Here** | Hold a circle over where you are, while you go elsewhere |
| **Deliver Cargo** | Transports run the game's own supply behaviour |
| **Land Here** | Helicopters set down where they are |
| **Return To Base** | Wingmen land at the nearest airbase |
| **Change Shape** | Cycle the formation geometry |
| **Disband** | Release everyone back to normal AI behaviour |

**Engage is tethered.** A wingman on Engage hunts freely but turns back if it strays past
`LeashRadius` of you, and is let loose again once back inside half of it.

### Rules of engagement
An **order** says where a wingman flies. **Rules of engagement** say what it shoots. Three
levels, wing-wide:

| ROE | Leaves the slot? | What it shoots | If you're shot at |
|---|---|---|---|
| **Hold** | never | missiles at you or itself; ground targets only while you attack ground | shoots the missile down |
| **Escort** | never | weapons free, guarding you first | shoots the aircraft that fired at you |
| **Free** | emergency only | weapons free | breaks formation and goes after the shooter |

### Getting wingmen
- **Recruit** — take command of friendly AI aircraft already flying on your side.
- **Buy** — from the **WMC** MFD screen's *Squadron Supply* section. Prices come out of your
  own allocation, and stock is drawn from your faction's supply, so a long mission can run a
  type dry. Modded and workshop aircraft are included.

---

## How to command your wing

### Radial menu (in flight)
Open the game's own radial menu and pick **Wing Command**, then dig into the submenus:

```
Wing Command ─┬─ Orders (Rejoin, Engage, Fall Back, Tasking…)
              ├─ Attack My Target
              ├─ Formation (ten shapes)
              ├─ Rules Of Engagement (Hold / Escort / Free)
              └─ Wing (Recruit Nearest, Disband)
```

Selecting an entry closes the wheel — open it again to go deeper. No page has more than six
slices, so every option is easy to hit.

<!-- IMAGE: the radial menu with Wing Command open -->

### Tactical map
- **Right-click** on selected friendly AI aircraft to add them to your wing.
- **Ctrl+1..4** stores the current map selection as a group, **1..4** recalls it — handy for
  re-issuing vanilla move orders to ground units without re-selecting them.

### WMC — the cockpit MFD screen
The main interface is a native MFD screen labelled **WMC**, alongside BDF / MAP / HUD. It
lists every wingman (slot, name, order, and live distance to their slot), and has buttons for
all the orders, per-wingman release, formation cycling, and the squadron shop.

<!-- IMAGE: the WMC MFD screen -->

### On your displays
Wingmen and their targets are tinted so they stand out:
- **Wingmen** — cyan (`#33E5FF`) on the map and HUD.
- **What they're engaging** — amber (`#FFB020`) on both.

Both colours are configurable.

<!-- IMAGE: tactical map with a tinted wing -->

---

## Install

### With NOMM (recommended)
Search for **WingCommand** in [NOMM](https://github.com/Combat787/NOMM) and install it —
BepInEx is set up for you and updates arrive automatically.

### Manually
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (5.4.23.3+) into your
   Nuclear Option folder and run the game once.
2. From the [releases page](https://github.com/GrabowMar/NuclearOption-WingCommand/releases),
   take either:
   - **`WingCommand.dll`** → drop into `Nuclear Option/BepInEx/plugins/`.
   - **`WingCommand-x.y.z.zip`** → extract at the Nuclear Option root.
3. Launch. `BepInEx/LogOutput.log` should show `WingCommand 0.6.4 loaded.` and
   `Harmony patched 8 method(s)`.

Settings live in `BepInEx/config/com.marci.wingcommand.cfg` and can be edited in-game with
[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (**F1**).

> **Host or single-player only.** AI pilots are server-authoritative in Nuclear Option, so
> formation control works when you host or play alone.

---

## Things to know

- **It's a sandbox.** The shop is knowingly overpowered — buying a wing will trivialise
  missions built around the stock difficulty. Most defaults lean generous.
- **Still evolving.** Orders, rules of engagement and config keys are still moving, and
  settings may reset between versions.
- **No multiplayer.** As a non-host client your orders fight the server's own AI.

Found something wrong? Please [open an issue](https://github.com/GrabowMar/NuclearOption-WingCommand/issues).

---

## Configuration

Everything is exposed through ConfigurationManager (**F1**). The knobs you're most likely to
touch:

| Setting | Default | What it does |
|---|---|---|
| `Aggression` | 1.0 | How hard wingmen correct position. Above ~2 they start to hunt. |
| `Damping` | 1.0 | How hard corrections are arrested — raise it if they overshoot. |
| `CommandAngle` | 25° | Largest heading correction while holding station. |
| `SlotSpacing` | 120 m | Distance between formation slots. |
| `MaxWingSize` | 3 | How many wingmen you can command. |
| `DefaultRoe` | Hold | Which rules of engagement a mission starts with. |
| `LeashRadius` | 8000 m | How far an Engage wingman may roam. |
| `WingPriceGrowth` | 1.5 | Price multiplier per wingman already owned. |
| `HighlightWingOnMap` / `…OnHud` | true | Tint your wing on the map / HUD. |

The full list — including rotary-tuning and shop settings — is in the config file with
descriptions next to each entry.

---

## For developers and modders

The interesting part is how much of this needed **no patching**: `FormationFlyState`
subclasses the game's own `PilotBaseState` and slots in via the public `Pilot.SwitchState`;
recruiting reads `UnitRegistry.allAircraft`; the shop uses `AircraftDefinition.value`,
`Player.Allocation` and `FactionHQ` supply. The only Harmony patches are for the radial menu
entries and the map/HUD tinting.

A few hard-won findings worth stealing:

- **`effort` above 1 is the agility switch** in `AutopilotPlane.AutoAim` — below corner speed,
  values ≤ 1 quietly halve a wingman's turn authority twice.
- **For helicopters, distance is throttle** — `AutopilotHelo` sets collective from
  `0.5 + distance*0.001 - speed*0.02`, and recomputes its waypoint once per second.
- **A stock bug this mod repairs** — `AIPilotCombatModes` unsubscribes its missile-alert
  handler in `LeaveState` but subscribes it in the constructor, so vanilla AI goes blind to
  missile warnings the first time it leaves the combat state.

### Building

```bash
dotnet build -c Release          # requires the .NET 8 SDK
pwsh build/copy-to-game.ps1       # deploy to the game's BepInEx/plugins
```

### Releasing

```bash
dotnet build -c Release
pwsh build/package.ps1            # prints the DLL/zip hashes and manifest fields
git tag -a v0.6.4 -m "WingCommand 0.6.4" && git push --tags
```

The mod is listed on [NOMM](https://github.com/Combat787/NOMM) via
[`build/nomnom/WingCommand.json`](build/nomnom/WingCommand.json); `autoUpdateArtifacts`
picks up later releases automatically.

---

## Licence

MIT — see [LICENSE](LICENSE).
