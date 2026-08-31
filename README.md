<div align="center">

# ✈ WING COMMAND
### Tactical AI Wing Control for Nuclear Option

[![Release](https://img.shields.io/badge/release-0.9.1.1-blue?style=for-the-badge)](https://github.com/GrabowMar/NuclearOption-WingCommand/releases)
[![Game Version](https://img.shields.io/badge/Nuclear%20Option-0.34.2-orange?style=for-the-badge)](https://store.steampowered.com/app/2247020/Nuclear_Option/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23.3%2B-lightgrey?style=for-the-badge)](https://github.com/BepInEx/BepInEx/releases)
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

**Stop babysitting one wingman at a time. Command your whole squadron like a flight lead.**

[Install](#-installation) • [Quick Start](#-quick-start) • [Loadouts](#-loadouts) • [Tips & Tricks](#-tips--tricks) • [FAQ](#-faq) • [Troubleshooting](#-troubleshooting)

</div>

---

> [!TIP]
> **New here?** Skip to [Quick Start](#-quick-start) — you'll be running a wing in under two minutes.

> [!IMPORTANT]
> WingCommand is in active development. Balance and controls can shift between releases, and the squadron shop can make missions noticeably easier — pace yourself.

> [!NOTE]
> Nuclear Option's AI is controlled by whoever hosts the match. WingCommand works fully in **single-player** and for the **host** of a multiplayer session. If you join someone else's server, you won't be able to command AI reliably — that's a game limitation, not a bug.

## Why you'll want this

Vanilla Nuclear Option gives you a wingman who mostly does their own thing. WingCommand turns your wing into an actual squadron:

🎯 **Point-and-click tactical control** — click wingmen on the map, build a scope, issue orders like a real flight lead
🔧 **Build your own loadouts** — a real pylon-by-pylon editor, saved as named templates that survive restarts
🛩️ **Six battle-tested formations** — Finger Four, Combat Spread, Trail, and more, with smooth transitions between them
🛡️ **Wingmen that don't die like idiots** — they react to missiles, terrain, and threats *on their own*, no micromanagement required
💰 **A real squadron shop** — buy aircraft, hold a personal reserve, requisition replacements mid-mission
🎖️ **Pilots who stick around** — callsigns, ranks and records that grow across the mission, and a radio that answers you
💀 **Second chances** — lose your jet? Jump into a surviving wingman's cockpit and keep fighting
🚫 **No more dogpiling** — your wing spreads attacks across targets instead of five planes shooting the same guy

## 📸 Screenshots

<!--
GALLERY — replace the src paths below once real screenshots are in docs/images/.
Keep filenames matching what's referenced here, or update the paths to match yours.
Recommended size: 1280x720 screenshots, compressed PNG or JPG (GitHub doesn't limit
file size much, but keep the repo lean — a few hundred KB per image is plenty).
-->
<table align="center">
  <tr>
    <td align="center" width="50%">
      <img src="docs/images/tactical-map.png" width="100%" alt="Tactical map with wing selected"><br>
      <sub>Tactical map — selecting and tasking a wing</sub>
    </td>
    <td align="center" width="50%">
      <img src="docs/images/wmc-supply.png" width="100%" alt="WMC Supply tab"><br>
      <sub>WMC Supply — buying and reserving aircraft</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <img src="docs/images/wmc-loadout.png" width="100%" alt="WMC Loadout tab"><br>
      <sub>WMC Loadout — building a template pylon by pylon</sub>
    </td>
    <td align="center" width="50%">
      <img src="docs/images/radial-menu.png" width="100%" alt="Wing Command radial menu"><br>
      <sub>The Wing Command radial menu</sub>
    </td>
  </tr>
</table>

<div align="center">
<sub>Have better shots or a gameplay GIF? Swap the files in <code>docs/images/</code> — the paths above already point there.</sub>
</div>

## 📦 Installation

### Option A — NOMM (recommended)

The easy way. [NOMM](https://github.com/Combat787/NOMM) handles BepInEx and keeps WingCommand updated for you.

1. Install [NOMM](https://github.com/Combat787/NOMM).
2. Search for **WingCommand** and hit install.
3. Launch Nuclear Option. Done.

### Option B — Manual install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your Nuclear Option folder and launch the game once (this lets BepInEx set itself up).
2. Grab `WingCommand.dll` from the [latest release](https://github.com/GrabowMar/NuclearOption-WingCommand/releases).
3. Drop it here:

   ```text
   Nuclear Option/BepInEx/plugins/WingCommand/WingCommand.dll
   ```

4. Launch the game and peek at `BepInEx/LogOutput.log` — you should see:

   ```text
   Harmony patched ... method(s)
   WingCommand 0.9.1.1 loaded.
   ```

   No line like that? Head to [Troubleshooting](#-troubleshooting).

> [!WARNING]
> Only keep the DLL in the organized `plugins/WingCommand/` folder. A stray copy directly in `plugins/` can trick BepInEx into loading the wrong build.

The optional `WingCommand-0.9.1.1.zip` mirrors the game directory and can instead be extracted at the Nuclear Option root.

### Changing settings

Settings live in `Nuclear Option/BepInEx/config/com.marci.wingcommand.cfg`. The easiest way to tweak them is in-game with [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) — press **F1** to open it.

## 🚀 Quick Start

1. **Load into a mission** as host or single-player.
2. **Buy your wing.** Open **WMC → Supply**, select friendly AI on the stock map, then press **Assign Selected** — once to see the price, again to confirm. Or requisition fresh airframes from the faction catalogue.
3. **Take tactical control.** Switch to **WMC → Tactical**. Click a wing icon or roster row to select one pilot, Shift-click to add more, or hit **Select All** to command everyone.
4. **Give an order.** Try **Hold Here** or **Land Here** — the cursor arms, click your target spot on the map.
5. **Kit them out.** Build a loadout template on **WMC → Loadout**, then pick it in the **FIT** row on **Supply** before you requisition.
6. **Or just use the radial.** Open the normal radial menu (same button as always) and pick **Wing Command** for instant whole-wing orders — no menus needed.

That's it. You're flight lead now.

Fixed-wing and rotary-wing aircraft can't share a formation. The shop and recruitment lists filter incompatible types automatically.

## 🕹️ Command interfaces

### The radial menu (fast, whole-wing orders)

Your muscle-memory radial menu now has a **Wing Command** entry:

```text
Wing Command
├─ Form Up
├─ Attack My Target
├─ Engage
├─ Disengage
├─ Rules Of Engagement
│  ├─ Defend
│  ├─ Escort
│  └─ Free
└─ Tasking
   ├─ Fire For Effect
   ├─ Hold Position
   ├─ Deliver Cargo
   ├─ Land Here
   ├─ Return To Base
   └─ Formation
      ├─ Echelon Right
      ├─ Line Abreast
      ├─ Trail
      ├─ Combat Spread
      ├─ Finger Four
      └─ Vic
```

Radial orders always hit the *entire* wing at once — great for split-second calls in a furball. Recruitment, release, and requisition are deliberately absent. The fallback radial and direct Form Up/Engage hotkeys remain available as advanced settings, unbound by default.

### The WMC screen (precise, scoped orders)

Four focused tabs:

- **Tactical** — paged roster, independent command selection, ROE, preferred weapon, six core formations, scoped orders, capability-gated Cargo/Land controls, and map-order status.
- **Supply** — funds, wing size and squadron capacity at a glance; the faction catalogue at flat list prices; the fit each requisition will carry, chosen from your saved templates; confirmed active-AI assignment; and a concrete three-airframe reserve.
- **Loadout** — a per-pylon editor for named loadout templates: the airframe's own hardpoints, its own stores, and its own exclusion rules, saved across missions.
- **Wing** — fuel, ammunition, cargo, hull state and current order for one wingman, with its pilot's callsign, rank, record and background.

### Tactical map cheat sheet

| Do this | To get this |
|---|---|
| Click a wing icon (WMC → Tactical open) | Select just that wingman |
| Shift-click another icon/roster row | Add or remove it from your selection |
| **Select All** | Back to commanding the whole wing |
| Press **Hold Here** / **Land Here** / **Deliver Cargo**, then click the map | Order that spot |
| Right-click the map with wingmen selected | Send them there (drops a `MOVE` marker) |
| Shift-right-click | Queue up another waypoint |
| Right-click again / Escape | Cancel the armed order |

Hostile, friendly non-wing, ground and naval icons behave exactly as they do in stock. Closing WMC or switching to any other tab stops intercepting wing-icon clicks.

Map moves are temporary routes. At the final point every wingman returns to formation; Free ROE permits opportunity fire but does not invent an Engage order.

## 📋 Orders, explained

| Order | What it does |
|---|---|
| **Form Up** | Close on assigned slots and hold station on you |
| **Attack My Target** | Jump the target you're locked on. Radial sends everyone; scoped WMC attacks distribute contacts and may hold surplus wingmen back as cover |
| **Fire For Effect** | Every selected wingman expends on one target until it's dead or they're dry |
| **Engage** | Hunt on their own within the configured leash, come back if they stray too far |
| **Disengage** | Break on separated headings, use countermeasures, egress, then form up again |
| **Hold Here** | Set up a CAP around a point while still applying ROE |
| **Deliver Cargo** | Fly cargo to a chosen map point, put it down, report the delivery and rejoin |
| **Land Here** | Set compatible helicopters down near the order point |
| **Return To Base** | Fly the stock landing pattern home, then hand the airframe back |
| **Formation dial** | Swap between the six core formation shapes on the fly |

Orders stick. A wingman ducking a missile, hitting bingo fuel, or getting recalled by the Engage leash doesn't forget what you told it to do — it picks the order back up.

**Return To Base completes.** Once the wingman is down and shut down at a friendly airbase, its airframe leaves the world and enters the three-slot wing reserve. A purchased airframe stays owned there and can be launched again without paying twice; an assigned mission airframe becomes a held reserve slot. Host or single-player only; set `Engagement/RtbReturnsToReserve` to `false` to leave recovered aircraft parked instead.

## 🎯 Rules of Engagement

Orders decide **where** a wingman flies. ROE decides **what it's allowed to shoot**.

| ROE | Leaves formation? | Weapons policy | If you get shot at |
|---|---|---|---|
| **Defend** (`Hold` in config) | No | Incoming missiles and mirrored ground attacks | Tries to intercept the missile |
| **Escort** | No | Engages air threats around the protected formation | Prioritises the threat to you |
| **Free** | No | Any valid opportunity target in range | Fires while maintaining the current task |

Every wingman will duck out of its slot for a split second to dodge an inbound missile no matter what ROE is set — self-preservation always wins, and the interrupt resumes the exact standing order.

## 🛩️ Formations

| Shape | Best for |
|---|---|
| **Echelon Right** | General-purpose swept line with element staggering |
| **Line Abreast** | Broad sensor and weapons frontage |
| **Trail** | Compact column with alternating vertical separation |
| **Combat Spread** | Wide mutual support, hard to bracket |
| **Finger Four** | Asymmetric four-ship elements that repeat for larger wings |
| **Vic** | Balanced V geometry for compact groups |

Echelon Left, Diamond, Ladder and Wall still parse from old configuration files but aren't in the selector; picking any core shape returns you to the compact six-shape cycle.

Formation slots are calculated in leader-local space and transition gradually when the shape or spacing changes. During hard turns, lateral spacing compresses and trail depth increases so inside and outside wingmen aren't given impossible speed demands.

Separation predicts the closest approach over the next four seconds — it reacts before two aircraft overlap, adds vertical deconfliction near terrain, and keeps avoidance corrections bounded so emergency steering doesn't destabilise the formation. Threat spacing kicks in only when there's a missile warning or hostile aircraft nearby: the formation widens smoothly, then settles back once it's clear. You don't have to babysit spacing.

## 🔫 Preferred weapon

An order says where a wingman flies. ROE says what it may shoot. **Preferred weapon** says which of its own stores it reaches for first.

| Setting | Effect |
|---|---|
| **AUTO** | The most effective ready station for the target |
| **A-A** | Prefers hostile aircraft and anti-air stores |
| **A-G** | Prefers surface contacts and anti-surface stores |
| **GUNS** | Prefers close-in stores, saving standoff weapons |

Set it per selection on **WMC → Tactical**; it shows in the roster and the compact HUD strip beside the order. Every setting is a bias, never a restriction: a preferred store that's empty, unready or out of range falls back to the usual choice, and no setting can make a wingman hold fire when it has a valid alternative.

## 🔥 Fire For Effect

**Attack** is measured. It spreads designations across the wing, caps how many aircraft are useful against one contact, holds surplus wingmen back as cover, and leaves several seconds between launches so nobody empties themselves on a truck.

**Fire For Effect** is the other thing. Every selected wingman goes after the same designated target and keeps shooting — no concurrency cap, no long cooldown — working down through missiles, then rockets, then guns as each runs dry, until the target is dead or there's nothing left aboard that could hurt it. Then they call `expended` and rejoin.

What it does *not* drop is weapon/target matching: a station still has to be effective against that kind of target and the shot still has to be inside the weapon's own envelope, so this expends a loadout on something worth expending it on rather than throwing air-to-air missiles at a tank. Bingo fuel and Winchester still send a wingman home.

Designate a target, select your scope, and press **Fire For Effect** on **WMC → Tactical**. The roster shows `FFE`. It's deliberately not on the radial: it's a considered decision, not a quick call.

## 🧰 Loadouts

**WMC → Loadout** is a workbench: you build named templates there, pylon by pylon. **WMC → Supply** is where you choose which template the next requisition actually flies with.

### Building a template

- Pick the airframe, press **+**, and you get a template seeded from that aircraft's first factory loadout — not a stripped airframe.
- The **PYLONS** list is the airframe's own hardpoints, under the names the airframe gives them. Click a pylon to choose what hangs on it, from the same stores the game's aircraft selection menu offers for that hardpoint. WingCommand adds no weapons of its own.
- **Empty is a choice.** Leave a station clean to take the weight off.
- A left/right pair is one row and moves together, because the game won't let them differ.
- A pylon that the rest of your fit rules out reads `BLOCKED` and goes inert. That's the airframe's own exclusion rule, asked of the game rather than guessed at.
- **SEED** fills every pylon at once from a role preset — `AIR-AIR`, `AIR-GND`, `BALANCED`, `CARGO` — and **STOCK** fills it from one of the airframe's own named factory loadouts. Both are starting points to edit, not final answers.
- The line under the list totals what you've built: pylons filled, weight, and role.
- Name the template in the **NAME** field. Flight controls are held off while you type.

Templates are saved to the config file and survive restarts. Up to eight per airframe.

### Flying one

On **Supply**, the **FIT** row picks between the airframe's standard fit and any template you've saved for it. That's what the next requisition of that type launches with.

- Equipment is fitted when an aircraft is created, so a wingman already in the air can't be reconfigured. The **Wing** tab shows what each one is carrying.
- An active mission aircraft you assign from the map flies **as found**. It arrives with whatever the mission gave it and can't be refitted.
- Return To Base keeps the fit. A recovered airframe goes into the wing reserve carrying what it came home with and launches that way again — a template chosen afterwards applies to the next *new* airframe, and Supply tells you which of the two you're about to get.
- Deleting a template doesn't disturb anything already flying it; a purchase order pointing at a deleted template falls back to the standard fit.

Loadouts don't change what an airframe costs. A requisition is list price, exactly as before.

### Cargo

A transport carries whatever cargo pod you put on its cargo pylon, chosen the same way as any other store.

**Deliver Cargo takes a drop point.** Press it, then click the map, exactly as Hold Here and Land Here work; a cargo marker appears at the point and a line runs to it from every wingman on the run. Helicopters descend and set the load down there, fixed-wing transports run in and release over it. Any airframe carrying a load can be given the order — it's no longer helicopter-only.

Press **Deliver Cargo** a second time while the cursor is armed to give up the point and use the game's own supply route instead, which is what the order did before.

A cargo run finishes either way: the wingman calls the delivery when the cargo actually leaves the aircraft and rejoins when it's empty, and one that can't put its load down says so and brings it back instead of circling for the rest of the mission.

## 🛒 Squadron supply

The Supply tab uses Nuclear Option's existing economy rather than inventing a separate one:

- **One aircraft, one price.** An airframe costs its list value — the same number the player's own aircraft menu prices from. Nothing compounds with wing size.
- Reassigning an active AI costs a flat 25% of list value by default; releasing and reassigning the same persistent aircraft doesn't charge twice.
- Price is paid from your allocation; declared aircraft consume the faction's mission supply. Rank, mission restrictions and fixed-wing/rotary compatibility are all respected.
- Requisitioned aircraft launch from a friendly airbase and fly to the wing under their own power. Where a hangar on the field stocks that airframe, the game's own airbase spawn is used — the aircraft appears in the hangar, waits out any door sequence, taxis and takes off exactly as the faction's own aircraft do. It shows in your roster immediately as departing and becomes commandable once airborne. Airframes no hangar stocks are delivered into the circuit overhead instead.
- Your **Wing Reserve** holds up to three specific airframes across all types. `HOLD` moves one selected faction airframe out of AI-accessible stock; `RELEASE` returns it. The reserve doesn't create supply and doesn't multiply by aircraft type.
- A requisition you've already paid for is marked owned. When it completes Return To Base that airframe comes back to the same reserve and can be launched again without another purchase. Recovered aircraft that were only active assignments return as ordinary held reserve and are charged normally next time.
- Undeclared-aircraft stock remains an advanced compatibility option, disabled by default.
- Every purchase and assignment previews its fee. Credits and supply move only after recruitment or spawn succeeds.

### Squadron capacity

Missions cap how many AI aircraft a faction may have airborne, and that cap shrinks for each friendly player — single-player missions routinely leave a limit of zero. The Supply tab shows it permanently as `SQUADRON active / limit`, so a shop that can't sell you anything says why instead of failing on a toast that's already gone.

**OVER LIMIT** grants permission to requisition past that cap at **3× list price**, needs **rank 3**, and allows at most **3 over-limit airframes in the air at once**. It's permission rather than a mode: while the squadron has room it changes nothing, and the surcharge only applies to a purchase that actually exceeds the limit.

The allowance counts *your* over-limit purchases that are still flying, not how far the faction as a whole exceeds its cap — missions script in AI regardless of the limit, and charging you for that would lock the shop on exactly the missions this exists to rescue. It frees up as those aircraft are lost or recovered. The three numbers are `Shop/ExceedSquadronLimitCost`, `ExceedSquadronLimitRank` and `ExceedSquadronLimitAllowance`.

## 🧠 AI coordination and self-preservation

### Target deconfliction

Locally simulated AI shares short-lived target reservations. Stock opportunity, threat, weapon, range and bravery calculations still decide whether a contact is worth attacking, while existing commitments nudge the next pilot toward a comparable unsaturated target.

- Follow-on attackers increasingly prefer another worthwhile unsaturated contact.
- Explicit wing attacks estimate how many aircraft are actually useful and hold the rest as cover.
- Missile defence assigns one interceptor to an inbound missile instead of wasting the whole wing's weapons on it.

This reduces dog-piling without making you or any other target artificially immune.

### Defensive panic system

When a wingman gets its own missile warning it calls the threat and enters a temporary defensive state:

- Radar-guided threats trigger a terrain-aware beam/notch, speed, chaff, and jamming where available.
- Infrared threats trigger an away/beam manoeuvre, reduced power, and flares.
- Unknown seekers get a conservative mixed response.
- Fixed-wing and rotary-wing aircraft use different steering logic.

Once the warning stays clear for the configured interval the wingman announces it's clear and resumes the queued order — including one issued while it was defensive. WMC and the compact roster show `DEFENSIVE` / `DEF` during the interrupt.

## 🎖️ Pilots

Each wingman is flown by someone. **WMC → Wing** shows their callsign, name, background, rank and record alongside the aircraft's fuel, ammunition, cargo and hull state.

- Pilots belong to the squadron. One who lands, or whose aircraft you release, goes back on the list with their record and flies the next airframe you requisition.
- A pilot who is killed does not come back.
- Experience comes from kills, completed sorties and engagements survived, and rank rises through Rookie, Wingman, Veteran, Ace and Legend.
- Rank has a small real effect: at the top it's worth roughly 12% more weapon reach and off-boresight tolerance and about 12% faster shot cycling. Set `Pilot/RankEffect` to `0` to keep the record and remove the mechanics.

This is a backbone. A pregenerated wingman pool with portraits and an assignment screen is a later feature; three pilots are written by hand and the rest are generated.

### Squadron radio

Radio calls use dedicated frameless subtitles at the top centre rather than the general game-message feed. The speaker is identified as `M. "COBALT" ADEYEMI` with the transmission beneath, and urgent missile calls jump ahead of routine traffic. A command sent to several aircraft produces a short staggered roll call from the pilots that actually accepted it; aircraft that were skipped don't answer as though they complied.

Each pilot carries a radio persona independent of rank and combat skill. COBALT is professional, HATCHET is aggressive, MERIDIAN is calm; generated pilots rotate through those voices and a dry one. The persona picks between several lines for the same event, and is intentionally a small data-facing seam for later mission and plot dialogue.

## 💀 When your jet goes down

If you're killed or eject while wingmen are still flying, they hold in a safe orbit and a takeover window pops up. Pick one (number keys work) and you'll spawn in a fresh copy of that aircraft — same airframe, loadout, fuel, livery and motion — and take the stick, with the AI source removed. This avoids reusing its AI/network state. You can also just use the normal respawn or defeat flow. The panel opens over the tactical map; available in single-player and to the host.

## 🗺️ HUD and map symbology

- Wingmen use a configurable high-contrast green marker and map caret.
- Active wing targets use amber markers.
- Selected members brighten without losing aircraft type or heading information.
- The compact roster shows order/state and live slot error, and hides while the map is open.
- On the maximised map, a line runs from every tasked wingman to the point it's flying to. A Shift-queued route draws as a chain — the leg being flown at full strength, the queue behind it dimmed, a dot at each pending point. An attack draws to its target in the same amber the target marker uses.

Slot error is the number to watch when tuning formation flight:

| Reading | Meaning |
|---|---|
| Small and steady | The aircraft is established in its slot |
| Repeated rise and fall | Correction or throttle gain may be too aggressive |
| Continually increasing | The wingman can't keep up, or is no longer under local control |
| `-` | The wingman isn't currently on a formation order |

## 💡 Tips & Tricks

- **Buy first, select second.** You need to own AI pilots via Supply before Tactical selection means anything.
- **Build one template per job.** An A-A sweep fit and a strike fit for the same airframe, named, and you're one dropdown away from either.
- **Empty pylons are free performance.** Strip the stations you won't use — the summary line under the pylon list shows the weight coming off.
- **Use Shift-click for surgical strikes.** Send two wingmen to flank while the rest hold formation on you.
- **Save Fire For Effect for something worth it.** It's designed to empty a wing's ordnance into one target; that's a great way to kill a ship and a terrible way to kill a jeep.
- **Watch the slot error readout** if formation flying looks janky — small and steady is healthy, continually climbing usually means a performance mismatch, not a bug.
- **Free ROE is a leash, not a suggestion.** Wingmen on Free will fire at opportunity targets — great in a furball, noisy if you need a quiet approach.
- **Widen out before the merge.** Combat Spread or Line Abreast make you much harder to bracket than a tight Trail.
- **Reserve your favourite airframe.** Hold it in the Wing Reserve so a lucky kill doesn't cost you another purchase — and bring it home with RTB, which keeps both the airframe and its fit.
- **Check `SQUADRON active/limit` before panic-buying.** An empty shop usually just means the mission's AI cap is full, not that something's broken.
- **Fixed-wing and rotary can't mix formations.** The shop and recruitment screens auto-filter this, so if an aircraft isn't listed, that's why.

## ❓ FAQ

**Does this work in multiplayer?**
Yes, if you're the **host**. Nuclear Option's AI is controlled by the host, so joining someone else's game means you can't reliably command AI wingmen — that's how the base game works, not a WingCommand limitation.

**Can I still use the vanilla wingman controls?**
Yes. The stock radial and Form Up/Engage hotkeys still exist as advanced settings (unbound by default) if you want them alongside WingCommand.

**Does WingCommand work with BOTE?**
Yes, it's designed to coexist with BOTE's radial submenus.

**Do loadout templates cost extra?**
No. A requisition is list price whatever you hang on it.

**Where are my templates saved?**
In `com.marci.wingcommand.cfg`, under `Loadout/SavedTemplates`. They survive missions and restarts. Clearing that value deletes them all.

**Why is a pylon greyed out and marked BLOCKED?**
Something else you've fitted rules it out — a conformal tank over a station, that sort of thing. That's the airframe's own exclusion rule; clear the store that's blocking it and the pylon comes back.

**Why can't I put helicopters and jets in the same formation?**
They fly too differently to formate together — the shop and recruitment lists automatically hide incompatible types so you won't accidentally try.

**I bought an aircraft, but it says the squadron is at its limit — did I get scammed?**
No — missions cap total AI aircraft in the air, and that cap shrinks with more players in the match (some single-player missions start at zero room). The Supply tab shows the live `active/limit` count so you always know why. You can push past it with **OVER LIMIT** once you hit the rank requirement.

**Do I get charged twice for the same aircraft?**
No. A **Return To Base** landing sends a purchased aircraft back to your reserve, and launching it again later is free.

**My wingmen look shaky in formation — is that a bug?**
Usually not — check the slot error readout on the WMC panel first. Small oscillation is normal; if it keeps climbing, the leader may be asking for more performance than the wingman's airframe can give.

**Is this compatible with the newest Nuclear Option update?**
Check the badges at the top of this page for the game version WingCommand currently targets. If the radial menu integration breaks after a game update, a fallback keybind is available under advanced settings until the mod is patched.

## 🔧 Troubleshooting

<details>
<summary><strong>Wing Command doesn't show up in the radial menu</strong></summary>

- Make sure `WingCommand.dll` is actually inside `BepInEx/plugins/`.
- Check for duplicate or older WingCommand DLLs sitting in other plugin folders.
- Open `LogOutput.log` and confirm you see the version + Harmony patch lines from the [install steps](#option-b--manual-install).
- If a recent game update broke the native radial hook, bind the fallback radial key in advanced settings.

</details>

<details>
<summary><strong>My aircraft won't follow orders</strong></summary>

- Confirm you're the **host** or in single-player — non-host clients can't reliably command AI.
- Fixed-wing and rotary-wing can't share a formation.
- **Deliver Cargo** needs a carried load; **Land Here** applies only to compatible helicopters.
- An aircraft that's already landing, destroyed, or no longer simulated locally can't take new orders.

</details>

<details>
<summary><strong>Formation flying looks unstable</strong></summary>

- Reset `Aggression`, `Damping`, and `ThrottleGain` to their defaults if you've been tweaking them.
- Make sure you (the leader) aren't outrunning the wingman's performance envelope.
- Turn on `Debug/VerboseLogging` and check the slot error readout before filing a bug report.

</details>

<details>
<summary><strong>The Loadout tab only offers the standard fit</strong></summary>

- Some airframes genuinely publish no readable hardpoint data; the tab says so, and that aircraft flies its own fit.
- If *every* airframe reads that way, a game update has probably moved the weapon-station members. Check `LogOutput.log` for a `[Loadout]` warning — the mod degrades to standard fits on purpose rather than fitting the wrong weapons.

</details>

Found a bug or have balance feedback? [Open a GitHub issue](https://github.com/GrabowMar/NuclearOption-WingCommand/issues) and include your game version, the aircraft/order/formation involved, and the relevant bit of `LogOutput.log`.

## ⚙️ Configuration reference

The ordinary ConfigurationManager view is intentionally limited to the release-facing settings below. Controller gains, integration toggles, compatibility features, colours and diagnostics live under **Advanced settings**; retired keys are hidden but still parse old configuration files.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Formation | `Shape` | `EchelonRight` | Initial formation |
| Formation | `MaxWingSize` | `3` | Maximum recruited wingmen |
| Engagement | `DefaultRoe` | `Hold` | Initial rules of engagement |
| Engagement | `AutoReturnOnEmpty` | `true` | Automatic RTB on Winchester or bingo |
| Engagement | `BingoFuel` | `0.15` | Automatic return fuel fraction |
| Engagement | `RtbReturnsToReserve` | `true` | Recovered wing airframes return to wing reserve |
| Engagement | `TakeoverOnDeath` | `true` | Offer a surviving wing aircraft after pilot loss |
| Loadout | `SavedTemplates` | `""` | Your saved per-pylon loadout templates |
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

The two Debug cheats are exposed only as F1 ConfigurationManager options. They're deliberately off by default, unsupported for balance and large-wing layout, and can break mission scripting or the mod itself.

Global AI `SkillScale`/`BraveryScale`, player-specific target protection, `WingPriceGrowth`, `RecruitRange`, `AdditionalWingReservePerType`, and the fast-delivery keys are retired and ignored, including when present in an older config.

## 🔩 Compatibility and implementation

WingCommand adds no custom network messages. It drives aircraft through the game's existing pilot states and autopilot interfaces, uses the stock economy and supply calls, and limits Harmony patches to UI dispatch, marker colour, missile-warning repair, and AI target deconfliction.

Loadout options come from the airframe's own `WeaponManager.hardpointSets`, and pylon exclusion is answered by the game's own `HardpointSet.BlockedByOtherHardpoint`. This mod defines no weapons and reimplements none of those rules.

The mod is designed to coexist with BOTE's radial submenus. Private game members required by the native radial integration are resolved through Harmony reflection; if a game update renames one, the mod logs the problem and leaves the fallback interface available.

Much of WingCommand has been developed with AI coding assistance under maintainer direction, review, and live flight testing. Contributions and detailed test reports are welcome.

## 🏗️ Building

Requires the .NET 8 SDK, BepInEx 5, and a local Nuclear Option installation at the default Steam path referenced by the project.

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
dist/WingCommand-0.9.1.1.zip
```

Attach **`WingCommand.dll` first** to a GitHub release. NOMM installs the bare plugin DLL; the ZIP is provided for manual installation. The package script reads the version directly from the built assembly and prints SHA-256 hashes for both assets.

## Licence

[MIT](LICENSE)

---

<div align="center">

**[⬆ Back to top](#-wing-command)**

Made for the Nuclear Option community • [MIT Licensed](LICENSE)

</div>
