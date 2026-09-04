<div align="center">

# ✈ WING COMMAND
### Tactical AI wing control for Nuclear Option

[![Release](https://img.shields.io/badge/release-0.9.2-blue?style=for-the-badge)](https://github.com/GrabowMar/NuclearOption-WingCommand/releases)
[![Game](https://img.shields.io/badge/Nuclear%20Option-0.34.2-orange?style=for-the-badge)](https://store.steampowered.com/app/2247020/Nuclear_Option/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23%2B-lightgrey?style=for-the-badge)](https://github.com/BepInEx/BepInEx/releases)
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

**Stop babysitting one wingman at a time. Command the whole squadron like a flight lead.**

[Install](#-install) • [Quick start](#-quick-start) • [Orders](#-orders) • [Supply](#-squadron-supply) • [Config](#️-configuration) • [FAQ](#-faq)

</div>

---

> [!NOTE]
> Nuclear Option's AI is host-controlled. WingCommand works fully in **single-player** and
> for the **host** of a multiplayer match. Joining someone else's server, you can't
> reliably command AI — a game limitation, not a bug.

> [!IMPORTANT]
> Active development: balance and controls shift between releases, and the squadron shop
> can make missions much easier. Pace yourself.

## What it does

Vanilla gives you one semi-autonomous wingman. WingCommand turns your wing into a squadron:

- 🎯 **Tactical control** — select wingmen on the map, build a scope, issue scoped orders
- 🔧 **Loadout editor** — per-pylon templates from the airframe's own stores, saved across missions
- 🛩️ **Six formations** — Finger Four, Combat Spread, Trail… with smooth transitions
- 🛡️ **Self-preservation** — wingmen react to missiles, terrain and threats on their own
- 💰 **Squadron economy** — buy airframes, hold a reserve, requisition replacements mid-mission
- 🎖️ **Persistent pilots** — callsigns, ranks and records that grow, plus a radio that answers
- 💀 **Takeover** — lose your jet, jump into a surviving wingman and keep fighting
- 🚫 **Deconfliction** — the wing spreads fire across targets instead of dogpiling one

## 📸 Screenshots

<table align="center">
  <tr>
    <td align="center" width="50%"><img src="docs/images/tactical-map.png" width="100%" alt="Tactical map with wing selected"><br><sub>Tactical map — selecting and tasking a wing</sub></td>
    <td align="center" width="50%"><img src="docs/images/wmc-supply.png" width="100%" alt="WMC Supply tab"><br><sub>WMC Supply — buying and reserving aircraft</sub></td>
  </tr>
  <tr>
    <td align="center" width="50%"><img src="docs/images/wmc-loadout.png" width="100%" alt="WMC Loadout tab"><br><sub>WMC Loadout — building a template pylon by pylon</sub></td>
    <td align="center" width="50%"><img src="docs/images/radial-menu.png" width="100%" alt="Wing Command radial menu"><br><sub>The Wing Command radial menu</sub></td>
  </tr>
</table>

<div align="center"><sub>Better shots or a GIF? Swap the files in <a href="docs/images/"><code>docs/images/</code></a>.</sub></div>

## 📦 Install

**NOMM (recommended):** install [NOMM](https://github.com/Combat787/NOMM), search **WingCommand**, install, launch.

**Manual:**

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option folder and launch the game once.
2. Take `WingCommand.dll` from the [latest release](https://github.com/GrabowMar/NuclearOption-WingCommand/releases) and place it at:

   ```text
   Nuclear Option/BepInEx/plugins/WingCommand/WingCommand.dll
   ```

3. Launch and check `BepInEx/LogOutput.log` for `WingCommand 0.9.2 loaded.` — if it's missing, see [Troubleshooting](#-troubleshooting).

> [!WARNING]
> Keep the DLL only in `plugins/WingCommand/`. A stray copy in `plugins/` can load the wrong build.

Settings live in `BepInEx/config/com.marci.wingcommand.cfg`. Edit them in-game with
[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (**F1**).

## 🚀 Quick start

1. **Load a mission** as host or single-player.
2. **Get a wing.** **WMC → Supply**: select friendly AI on the map and press **Assign Selected** (once for the price, again to confirm), or requisition fresh airframes from the catalogue.
3. **Take control.** **WMC → Tactical**: click a wing icon or roster row to select one, Shift-click to add, **Select All** for everyone.
4. **Order.** Try **Hold Here** — the cursor arms, click the map.
5. **Kit them out.** Build a template on **WMC → Loadout**, pick it in the **FIT** row on **Supply** before requisitioning.
6. **Or the radial.** Open the normal radial menu → **Wing Command** for instant whole-wing orders.

Fixed-wing and rotary can't share a formation; the shop and recruit lists filter incompatible types automatically.

## 🕹️ Command interfaces

**Radial** — fast, always **whole-wing**. Recruitment/release/requisition are deliberately absent.

```text
Wing Command
├─ Combat: Attack My Target · Engage · Splash 'Em · Disengage
├─ Flight & Tasking: Form Up · Hold Position · Return To Base
│  └─ Special Tasking: Jam Target · Deliver Cargo · Land Here
├─ Formation: Echelon Right · Line Abreast · Trail · Combat Spread · Finger Four · Vic
├─ Rules Of Engagement: Defend · Escort · Free
└─ Manoeuvres
   ├─ Combat: Break Left/Right · Split-S · Immelmann
   └─ Aerobatics: Barrel Roll · Aileron Roll · Loop · Wing Waggle
```

**WMC screen** — precise, **scoped** orders. Four tabs:

- **Tactical** — roster, command selection, ROE, preferred weapon, formations, scoped orders, map-order status.
- **Supply** — funds, wing size, squadron capacity, the faction catalogue at list price, the FIT each requisition carries, and a three-airframe reserve.
- **Loadout** — per-pylon editor for named templates: the airframe's own hardpoints, stores and exclusion rules.
- **Wing** — one wingman's fuel, ammo, cargo, hull and current order, plus its pilot's callsign, rank and record.

**Tactical map** (WMC → Tactical open):

| Do this | Get this |
|---|---|
| Click a wing icon | Select just that wingman |
| Shift-click another icon / roster row | Add or remove it |
| **Select All** | Command the whole wing |
| Press Hold / Land / Deliver Cargo, then click the map | Order that spot |
| Right-click the map with wingmen selected | Send them there (`MOVE` marker) |
| Shift-right-click | Queue another waypoint |
| Right-click again / Escape | Cancel the armed order |

Hostile, friendly non-wing, ground and naval icons behave exactly as in stock. Closing WMC or
switching tabs stops intercepting wing-icon clicks. Map moves are temporary routes — at the
final point every wingman returns to formation.

## 📋 Orders

| Order | What it does |
|---|---|
| **Form Up** | Close on assigned slots and hold station on you |
| **Attack My Target** | Hit your locked target. Radial sends everyone; scoped WMC distributes contacts and may hold surplus back as cover |
| **Splash 'Em** | Every selected wingman pours its whole loadout into one target until it's dead or they're dry. WMC only — not a quick call |
| **Engage** | Hunt within the configured leash, return if they stray |
| **Disengage** | Break on separated headings, countermeasure, egress, then form up |
| **Hold Here** | CAP a point while still applying ROE |
| **Deliver Cargo** | Fly cargo to a chosen point, drop it, report, rejoin |
| **Land Here** | Set compatible helicopters down near the point |
| **Return To Base** | Fly the stock pattern home, then hand the airframe back |
| **Formation dial** | Swap between the six shapes on the fly |

Orders stick. A wingman ducking a missile, hitting bingo, or recalled by the leash resumes
the exact standing order afterward.

**RTB completes.** Down and shut down at a friendly base, the airframe leaves the world and
enters the three-slot wing reserve — a purchased one stays owned and relaunches free, an
assigned mission airframe becomes a held slot. Set `Engagement/RtbReturnsToReserve = false`
to leave recovered aircraft parked.

**Splash 'Em vs Attack.** Attack is measured — spread designations, a useful-attacker cap,
surplus held as cover, seconds between launches. Splash 'Em drops all of that: one target,
no cap, sustained volley through missiles → rockets → gun. It keeps weapon/target matching
and shot envelopes, so it empties a loadout on something worth emptying it on. Bingo and
Winchester still recall it.

## 🎯 Rules of engagement

Orders decide **where** a wingman flies; ROE decides **what it may shoot**.

| ROE | Weapons policy | Under fire |
|---|---|---|
| **Defend** (`Hold` in config) | Incoming missiles and mirrored ground attacks | Tries to intercept the missile |
| **Escort** | Air threats around the formation | Prioritises the threat to you |
| **Free** | Any valid opportunity target in range | Fires while holding the current task |

No ROE leaves formation. Every wingman still ducks its slot briefly to dodge an inbound
missile regardless of ROE — self-preservation always wins, and the interrupt resumes the
standing order.

## 🛩️ Formations

| Shape | Best for |
|---|---|
| **Echelon Right** | General-purpose swept line, elements staggered |
| **Line Abreast** | Broad sensor and weapons frontage |
| **Trail** | Compact column, alternating vertical separation |
| **Combat Spread** | Wide mutual support, hard to bracket |
| **Finger Four** | Asymmetric four-ship elements, repeated for larger wings |
| **Vic** | Balanced V for compact groups |

Echelon Left, Diamond, Ladder and Wall still parse from old config files but aren't in the
selector. Slots are leader-local and transition gradually. In hard turns, lateral spacing
compresses and trail depth grows so wingmen aren't given impossible speed demands.
Separation predicts the closest approach over four seconds, adds vertical deconfliction near
terrain, and bounds corrections. Threat spacing widens the formation only during a missile
warning or with hostiles near, then settles back. You don't babysit spacing.

## 🔫 Preferred weapon

Which of a wingman's own stores it reaches for first. Set per selection on **WMC → Tactical**;
shown in the roster and HUD strip.

| Setting | Effect |
|---|---|
| **AUTO** | Most effective ready station for the target |
| **A-A** | Prefers aircraft and anti-air stores |
| **A-G** | Prefers surface contacts and anti-surface stores |
| **GUNS** | Prefers close-in stores, saving standoff weapons |

Always a bias, never a restriction: an empty, unready or out-of-range preferred store falls
back to the usual choice, and nothing here makes a wingman hold fire when it has an alternative.

## 🧰 Loadouts

**WMC → Loadout** builds named templates pylon by pylon; **WMC → Supply** picks which one the
next requisition flies with.

- Pick the airframe, press **+** for an empty template.
- **PYLONS** is the airframe's own hardpoints under its own names. Click one to choose a
  store, from the same list the game's aircraft menu offers. WingCommand adds no weapons.
- **Empty is a choice** — leave a station clean for the weight.
- A left/right pair is one row and moves together (the game won't let them differ).
- A pylon ruled out by the rest of your fit reads `BLOCKED` and goes inert — the airframe's
  own exclusion rule, asked of the game.
- Name it in **NAME** (flight controls are held off while typing). Saved to config, survives
  restarts, up to eight per airframe.

**Flying one:** the **FIT** row on Supply picks the standard fit or a saved template.

- Equipment is fitted at aircraft creation, so one already airborne can't be refitted — the
  **Wing** tab shows what each carries.
- An active mission aircraft assigned from the map flies **as found** and can't be refitted.
- RTB keeps the fit: a recovered airframe re-launches with what it came home with; a
  template chosen afterward applies to the next *new* airframe, and Supply says which you'll get.
- Deleting a template doesn't disturb anything flying it; a purchase order pointing at a
  deleted template falls back to the standard fit.
- Loadouts don't change price. A requisition is list price.

**Cargo:** a transport carries whatever cargo pod is on its cargo pylon. **Deliver Cargo
takes a drop point** — press it, click the map, same as Hold/Land; helicopters set the load
down there, fixed-wing run in and release over it. Press it again while armed to give up the
point and use the game's own supply route. Either way the run finishes: the wingman calls
the delivery when the cargo leaves and rejoins when empty; one that can't drop says so and
brings it back.

## 🛒 Squadron supply

Built on Nuclear Option's existing economy, not a separate one:

- **One aircraft, one price** — list value, nothing compounds with wing size.
- Reassigning an active AI costs a flat **25%** of list value (`Shop/RecruitmentCostPercent`);
  releasing and reassigning the same persistent aircraft doesn't charge twice.
- Price is paid from your allocation; declared aircraft consume faction mission supply. Rank,
  mission restrictions and fixed-wing/rotary compatibility are respected.
- Requisitioned aircraft launch from a friendly airbase and fly out under their own power —
  from a stocking hangar via the game's own airbase spawn (door, taxi, takeoff). Supply's
  **LAUNCH FROM** list (5 fields per page, checkboxes) chooses which bases may spawn.
  **ONLY NEAREST** queues at the closest checked field that stocks the airframe.
  **ANY** launches immediately from the closest checked field with a free hangar, and waits
  unpinned if every allowed pad is busy. They show as departing immediately, commandable
  once the stock takeoff has finished.
- **Wing Reserve** holds up to three specific airframes across all types. `HOLD` pulls one
  faction airframe out of AI-accessible stock; `RELEASE` returns it. It doesn't create supply.
- **Releasing a wingman sends it home** (`REL` on the Wing roster) — it flies the stock
  pattern back, stops counting against the squadron limit immediately, and its airframe is
  credited back on landing.
- A paid requisition is marked owned and returns to the reserve on RTB to relaunch free.
  Recovered active assignments return as ordinary held reserve, charged normally next time.
- Every purchase and assignment previews its fee. Credits and supply move only after
  recruitment or spawn succeeds.

**Squadron capacity.** Missions cap airborne faction AI, and the cap shrinks per friendly
player — single-player often leaves zero room. Supply shows it as `SQUADRON active / limit`.
**OVER LIMIT** permits requisitioning past the cap at **3× list price**, needs **rank 3**,
and allows **3 over-limit airframes airborne at once** (`Shop/ExceedSquadronLimit*`). It's
permission, not a mode: with room to spare it changes nothing, and the surcharge only hits a
purchase that actually exceeds the limit. The allowance counts *your* still-flying over-limit
purchases, not how far the faction as a whole is over.

## 🧠 AI coordination and self-preservation

**Deconfliction.** Locally-simulated AI shares short-lived target reservations. Stock
opportunity/threat/weapon/range/bravery logic still decides whether a contact is worth
attacking; commitments nudge the next pilot toward a comparable unsaturated target. Explicit
wing attacks estimate useful attackers and hold the rest as cover; missile defence assigns
one interceptor per inbound. No target becomes artificially immune.

**When you land.** A slot is measured from the leader, so a leader on the runway puts every
slot on the runway. Wingmen holding formation recognise you're on the deck (low, gear down)
and orbit the field instead, rejoining once you're airborne. An explicit order is untouched.

**Defensive panic.** On its own missile warning a wingman calls the threat and goes
defensive: terrain-aware beam/notch + chaff + jamming for radar, away/beam + flares for IR,
a conservative mix for unknown, different steering for fixed-wing vs rotary. Once the warning
stays clear for the configured interval it calls clear and resumes the queued order —
including one issued while defensive. Shown as `DEFENSIVE` / `DEF`.

## 🎖️ Pilots and radio

**WMC → Wing** shows each wingman's callsign, name, background, rank and record.

- Pilots belong to the squadron. One who lands, or whose aircraft you release, goes back on
  the list with their record and flies your next requisition. A killed pilot doesn't return.
- Experience comes from kills, completed sorties and engagements survived; rank rises through
  Rookie → Wingman → Veteran → Ace → Legend.
- Rank has a small real effect — at the top, ~12% more weapon reach and off-boresight and
  ~12% faster shot cycling. `Pilot/RankEffect = 0` keeps the record, removes the mechanic.
- Backbone only: a pregenerated pool with portraits and an assignment screen is a later
  feature. Three pilots are hand-written, the rest generated.

**Radio.** Calls use frameless subtitles at top centre, not the game-message feed — speaker
as `M. "COBALT" ADEYEMI`, a smaller line for flight position and aircraft. A command to
several aircraft gets one element acknowledgement from its lead; skipped aircraft don't
answer as if they complied. Urgent missile/damage/loss calls jump the queue. Each pilot
carries a persona (professional, aggressive, calm, dry) that picks between lines for the same
event — a small seam for later mission dialogue. Each transmission opens with the game's
radio click (`Comms/RadioChatterSound`). Airborne crews rarely trade jokes and rumours when
the radio is quiet (`Comms/CrewBanter`). The mod no longer writes ordinary notices into the
game's message boxes.

## 💀 Takeover

Killed or ejected with wingmen still flying, they hold in a safe orbit and a takeover window
opens (number keys work). Pick one and you spawn a fresh copy of that aircraft — same
airframe, loadout, fuel, livery and motion — and take the stick, with the AI source removed
to avoid reusing its AI/network state. Normal respawn and defeat flows still work. Single-player
and host; `Engagement/TakeoverOnDeath`.

## 🗺️ HUD and map

- Wingmen use a high-contrast green marker and map caret; active wing targets are amber;
  selected members brighten without losing type or heading.
- The compact roster shows order/state and live **slot error**, and hides while the map is open.
- On the maximised map, a line runs from every tasked wingman to its point. A Shift-queued
  route draws as a chain (current leg bright, queue dimmed, a dot per pending point); an
  attack draws to its target in amber.

Slot error: small and steady = established; rising and falling = correction/throttle gain
too aggressive; continually climbing = can't keep up or not under local control; `-` = not
on a formation order.

## 💡 Tips

- **Buy first, select second** — Tactical selection means nothing until you own AI pilots via Supply.
- **One template per job** — an A-A sweep fit and a strike fit, named, one dropdown apart.
- **Empty pylons are free performance** — the summary line shows the weight coming off.
- **Shift-click for surgical strikes** — send two to flank while the rest hold on you.
- **Save Splash 'Em for something worth it** — great on a ship, terrible on a jeep.
- **Widen before the merge** — Combat Spread or Line Abreast beat a tight Trail for not getting bracketed.
- **Reserve your favourite airframe** so a lucky kill doesn't cost another purchase.
- **Check `SQUADRON active/limit`** before panic-buying — an empty shop usually just means the AI cap is full.

## ❓ FAQ

**Multiplayer?** Only as **host** — Nuclear Option's AI is host-controlled.

**Vanilla wingman controls?** Still there as advanced settings, unbound by default.

**Works with BOTE?** Yes, designed to coexist with its radial submenus.

**Do templates cost extra?** No. A requisition is list price whatever you hang on it.

**Where are templates saved?** `com.marci.wingcommand.cfg`, under `Loadout/SavedTemplates`.
Clearing that value deletes them all.

**Why is a pylon `BLOCKED`?** Something else you've fitted rules it out — the airframe's own
exclusion rule. Clear the blocking store and it comes back.

**Helicopters and jets in one formation?** No — they fly too differently. The shop and
recruit lists hide incompatible types.

**Bought an aircraft but the squadron's at its limit — scammed?** No. Missions cap airborne
AI and the cap shrinks with more players. Push past it with **OVER LIMIT** at rank 3.

**Charged twice for the same aircraft?** No — an RTB landing returns a purchased aircraft to
your reserve to relaunch free.

**Shaky formation — a bug?** Usually not. Check slot error: small oscillation is normal;
climbing means the leader is asking for more than the airframe can give.

**Newest game update?** Check the badges above for the targeted version. If the radial hook
breaks after an update, a fallback keybind is under advanced settings until the mod is patched.

## 🔧 Troubleshooting

<details>
<summary><strong>Wing Command doesn't show up in the radial menu</strong></summary>

- Confirm `WingCommand.dll` is inside `BepInEx/plugins/WingCommand/`, with no older duplicates elsewhere.
- Check `LogOutput.log` for the version + Harmony patch lines from the install steps.
- If a game update broke the native radial hook, bind the fallback radial key in advanced settings.
</details>

<details>
<summary><strong>Aircraft won't follow orders</strong></summary>

- Confirm you're **host** or single-player.
- Fixed-wing and rotary can't share a formation.
- **Deliver Cargo** needs a carried load; **Land Here** is compatible-helicopter only.
- An aircraft already landing, destroyed, or not locally simulated can't take new orders.
</details>

<details>
<summary><strong>Formation flying looks unstable</strong></summary>

- Reset `Aggression`, `Damping` and `ThrottleGain` to defaults.
- Make sure you're not outrunning the wingman's performance envelope.
- Turn on `Debug/VerboseLogging` and check slot error before filing a bug.
</details>

<details>
<summary><strong>The Loadout tab only offers the standard fit</strong></summary>

- Some airframes publish no readable hardpoint data; the tab says so and that aircraft flies its own fit.
- If *every* airframe reads that way, a game update likely moved the weapon-station members —
  check `LogOutput.log` for a `[Loadout]` warning. The mod degrades to standard fits on
  purpose rather than fitting the wrong weapons.
</details>

Found a bug? [Open an issue](https://github.com/GrabowMar/NuclearOption-WingCommand/issues)
with your game version, the aircraft/order/formation involved, and the relevant `LogOutput.log` lines.

## ⚙️ Configuration

The ordinary ConfigurationManager view is limited to the settings below. Controller gains,
integration toggles, compatibility features, colours and diagnostics are under **Advanced**;
retired keys are hidden but still parse old files.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Formation | `Shape` | `EchelonRight` | Initial formation |
| Formation | `MaxWingSize` | `3` | Maximum recruited wingmen |
| Engagement | `DefaultRoe` | `Hold` | Initial ROE |
| Engagement | `AutoReturnOnEmpty` | `true` | Auto RTB on Winchester or bingo |
| Engagement | `BingoFuel` | `0.15` | Auto-return fuel fraction |
| Engagement | `RtbReturnsToReserve` | `true` | Recovered airframes return to wing reserve |
| Engagement | `TakeoverOnDeath` | `true` | Offer a surviving wing aircraft after pilot loss |
| Loadout | `SavedTemplates` | `""` | Your saved per-pylon templates |
| Shop | `RecruitmentCostPercent` | `0.25` | Active-AI reassignment fee, fraction of list value |
| Shop | `ExceedSquadronLimitCost` | `3` | Price multiplier past the AI limit |
| Shop | `ExceedSquadronLimitRank` | `3` | Rank required to exceed the limit |
| Shop | `ExceedSquadronLimitAllowance` | `3` | Over-limit airframes airborne at once |
| Pilot | `PilotProgression` | `true` | Pilots keep a record, rank and small skill effect |
| Pilot | `RankEffect` | `1` | How much rank changes shooting; `0` = record only |
| Pilot | `XpPerRank` | `120` | Experience step between ranks |
| Comms | `RadioChatter` | `true` | Wing order and state reports |
| Comms | `RadioChatterSound` | `true` | Open each transmission with the radio click |
| Comms | `CrewBanter` | `true` | Rare jokes and rumours between airborne pilots |
| UI | `ShowWingHud` | `true` | Compact roster docked beside the tactical map |
| Debug | `FreePlanePurchases` | `false` | Free requisitions; **probably breaks the mod** |
| Debug | `DisableWingSizeLimit` | `false` | Ignore `MaxWingSize`; **probably breaks the mod** |

The Debug cheats are F1-only, off by default, and unsupported. Global AI `SkillScale` /
`BraveryScale`, player-specific target protection, `WingPriceGrowth`, `RecruitRange`,
`AdditionalWingReservePerType` and the fast-delivery keys are retired and ignored.

## 🔩 Implementation

WingCommand adds no custom network messages. It drives aircraft through the game's existing
pilot states and autopilot, uses the stock economy and supply calls, and limits Harmony
patches to UI dispatch, marker colour, missile-warning repair and AI target deconfliction.
Loadout options come from the airframe's own `WeaponManager.hardpointSets`; pylon exclusion
is the game's own `HardpointSet.BlockedByOtherHardpoint`. This mod defines no weapons.
Private game members for the native radial integration are resolved through reflection; a
game update that renames one is logged, with the fallback interface left available.

Design notes for the *why* behind these decisions are in [docs/](docs/). Much of the mod is
developed with AI coding assistance under maintainer direction, review and live flight
testing. Contributions and test reports welcome.

## 🏗️ Building

Requires the .NET 8 SDK, BepInEx 5, and a local Nuclear Option install at the Steam path in the project.

```powershell
dotnet build src/WingCommand/WingCommand.csproj -c Release
```

Release assets:

```bash
nomod package --mod wingcommand
```

→ `dist/WingCommand.dll` and `dist/WingCommand-0.9.2.zip`. Attach **`WingCommand.dll` first**
to a GitHub release — NOMM installs the bare DLL; the ZIP is for manual installs. The package
script reads the version from the built assembly and prints SHA-256 hashes.

## Licence

[MIT](LICENSE)

---

<div align="center">

**[⬆ Back to top](#-wing-command)** • Made for the Nuclear Option community

</div>
