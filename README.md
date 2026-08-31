<div align="center">

# ✈️ WING COMMAND
### Tactical AI Wing Control for Nuclear Option

[![Release](https://img.shields.io/badge/release-0.9.0-blue?style=for-the-badge)](https://github.com/GrabowMar/NuclearOption-WingCommand/releases)
[![Game Version](https://img.shields.io/badge/Nuclear%20Option-0.34.2-orange?style=for-the-badge)](https://store.steampowered.com/app/2247020/Nuclear_Option/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23.3%2B-lightgrey?style=for-the-badge)](https://github.com/BepInEx/BepInEx/releases)
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

**Stop babysitting one wingman at a time. Command your whole squadron like a flight lead.**

[Install](#-installation) • [Quick Start](#-quick-start) • [Tips & Tricks](#-tips--tricks) • [FAQ](#-faq) • [Troubleshooting](#-troubleshooting)

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

🎯 **Point-and-click tactical control** — click wingmen on the map, drag a selection, issue orders like a real flight lead
🛩️ **Six battle-tested formations** — Finger Four, Combat Spread, Trail, and more, with smooth transitions between them
🛡️ **Wingmen that don't die like idiots** — they react to missiles, terrain, and threats *on their own*, no micromanagement required
💰 **A real squadron shop** — buy aircraft, hold a personal reserve, requisition replacements mid-mission
💀 **Second chances** — lose your jet? Jump into a surviving wingman's cockpit and keep fighting
🎯 **No more dogpiling** — your wing spreads attacks across targets instead of five planes shooting the same guy

## 📸 Screenshots

<div align="center">
<!-- Drop screenshots or a short GIF into docs/images/ and reference them here, e.g.: -->
<!-- <img src="docs/images/tactical-map.png" width="800" alt="Tactical map wing selection"> -->
<!-- <img src="docs/images/wmc-supply.png" width="800" alt="Supply tab"> -->

*Add screenshots of the tactical map, the WMC Supply tab, and a formation in flight to `docs/images/` — this section is ready for them.*

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
   WingCommand 0.9.0 loaded.
   ```

   No line like that? Head to [Troubleshooting](#-troubleshooting).

> [!WARNING]
> Only keep the DLL in the organized `plugins/WingCommand/` folder. A stray copy directly in `plugins/` can trick BepInEx into loading the wrong build.

### Changing settings

Settings live in `Nuclear Option/BepInEx/config/com.marci.wingcommand.cfg`. The easiest way to tweak them is in-game with [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) — press **F1** to open it.

## 🚀 Quick Start

1. **Load into a mission** as host or single-player.
2. **Buy your wing.** Open **WMC → Supply**, select friendly AI on the stock map, then press **Assign Selected** — once to see the price, again to confirm.
3. **Take tactical control.** Switch to **WMC → Tactical**. Click a wing icon or roster row to select one pilot, Shift-click to add more, or hit **Select All** to command everyone.
4. **Give an order.** Try **Hold Here** or **Land Here** — the cursor arms, click your target spot on the map.
5. **Or just use the radial.** Open the normal radial menu (same button as always) and pick **Wing Command** for instant whole-wing orders — no menus needed.

That's it. You're flight lead now.

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

Radial orders always hit the *entire* wing at once — great for split-second calls in a furball.

### The WMC screen (precise, scoped orders)

Two tabs do the heavy lifting:

- **Tactical** — your roster, map-based selection, ROE, formations, and orders
- **Supply** — funds, wing size, the faction shop, and your personal reserve

### Tactical map cheat sheet

| Do this | To get this |
|---|---|
| Click a wing icon (WMC → Tactical open) | Select just that wingman |
| Shift-click another icon/roster row | Add or remove it from your selection |
| **Select All** | Back to commanding the whole wing |
| Press **Hold Here** / **Land Here**, then click the map | Order that spot |
| Right-click the map with wingmen selected | Send them there (drops a `MOVE` marker) |
| Shift-right-click | Queue up another waypoint |
| Right-click again / Escape | Cancel the armed order |

## 📋 Orders, explained

| Order | What it does |
|---|---|
| **Form Up** | Get back in formation and hold station on you |
| **Attack My Target** | Jump the target you're locked on (spread across it if you send the whole wing) |
| **Engage** | Hunt on their own within a leash range, come back if they stray too far |
| **Disengage** | Break off, pop countermeasures, egress, then reform |
| **Hold Here** | Set up a CAP around a point while still following ROE |
| **Deliver Cargo** | Compatible transport helicopters run supply drops |
| **Land Here** | Compatible helicopters set down near the point |
| **Return To Base** | Fly the landing pattern home and go back into the squadron's pool |
| **Formation** | Swap between the six formation shapes on the fly |

Orders stick. A wingman ducking a missile or landing to refuel doesn't forget what you told it to do — it picks the order back up.

## 🎯 Rules of Engagement

Orders decide **where** a wingman flies. ROE decides **what it's allowed to shoot at**.

| ROE | Leaves formation? | Fires at | If you get shot at |
|---|---|---|---|
| **Defend** | No | Incoming missiles + self-defense only | Tries to intercept the missile |
| **Escort** | No | Anything, but prioritizes protecting you | Engages whoever shot at you, from position |
| **Free** | For emergencies only | Anything | May break formation and go hunt the shooter |

Every wingman will duck out of formation for a split second to dodge an inbound missile no matter what ROE is set — self-preservation always wins.

## 🛩️ Formations

| Shape | Best for |
|---|---|
| **Echelon Right** | General-purpose swept line |
| **Line Abreast** | Wide sensor/weapons coverage |
| **Trail** | Tight column through narrow corridors |
| **Combat Spread** | Wide mutual support, hard to bracket |
| **Finger Four** | Classic four-ship, scales to bigger wings |
| **Vic** | Tight, balanced V for small groups |

Formations reshape themselves automatically during hard turns and widen out when a missile warning or nearby threat pops up, then tuck back in once it's clear. You don't have to babysit spacing.

## 🛒 Squadron shop

- Aircraft cost the same list price you'd pay from the normal aircraft menu — no markup for having a bigger wing.
- Reassigning an already-active AI pilot costs a flat fee instead of a full purchase.
- Your **Wing Reserve** holds up to three specific airframes. Land one with **Return To Base** and it comes home to the reserve — launch it again later for free, no double-charging.
- Missions cap how many AI aircraft can be airborne at once, and that cap shrinks with more players. The Supply tab always shows you `SQUADRON active / limit` so you know exactly why the shop might be empty.
- Need more than the cap allows? **OVER LIMIT** purchases let you push past it at a price premium, once you hit the required rank — capped at a handful of extra airframes in the air at a time.
- Every purchase shows its price before you confirm. Nothing is charged silently.

## 💀 When your jet goes down

If you're killed or eject while wingmen are still flying, they'll hold in a safe orbit and a takeover window pops up. Pick one (number keys work) and you'll spawn in a fresh copy of that aircraft — same loadout, fuel, and paint — and take the stick. You can also just respawn normally instead. Available in single-player and to the host.

## 💡 Tips & Tricks

- **Buy first, select second.** You need to own AI pilots via Supply before Tactical selection means anything.
- **Use Shift-click for surgical strikes.** Send two wingmen to flank while the rest hold formation on you.
- **Watch the slot error readout** if formation flying looks janky — small and steady is healthy, climbing steadily means the wingman can't keep up (usually a performance mismatch, not a bug).
- **Free ROE is a leash, not a suggestion.** Wingmen on Free will break off to chase a shooter — great for a furball, risky if you need them covering your approach.
- **Widen out before the merge.** Combat Spread or Line Abreast make you much harder to bracket than a tight Trail.
- **Reserve your favorite airframe.** Hold it in the Wing Reserve so a lucky kill doesn't cost you another purchase.
- **Check `SQUADRON active/limit` before panic-buying.** An empty shop usually just means the mission's AI cap is full, not that something's broken.
- **Fixed-wing and rotary can't mix formations.** The shop and recruitment screens auto-filter this for you, so if an aircraft isn't listed, that's why.

## ❓ FAQ

**Does this work in multiplayer?**
Yes, if you're the **host**. Nuclear Option's AI is controlled by the host, so joining someone else's game means you can't reliably command AI wingmen — that's how the base game works, not a WingCommand limitation.

**Can I still use the vanilla wingman controls?**
Yes. The stock radial and Form Up/Engage hotkeys still exist as advanced settings (unbound by default) if you want them alongside WingCommand.

**Does WingCommand work with BOTE?**
Yes, it's designed to coexist with BOTE's radial submenus.

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

## 🛠️ Troubleshooting

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
- **Deliver Cargo** and **Land Here** only work on compatible helicopters.
- An aircraft that's already landing, destroyed, or no longer being simulated locally can't take new orders.

</details>

<details>
<summary><strong>Formation flying looks unstable</strong></summary>

- Reset `Aggression`, `Damping`, and `ThrottleGain` to their defaults if you've been tweaking them.
- Make sure you (the leader) aren't outrunning the wingman's performance envelope.
- Turn on `Debug/VerboseLogging` and check the slot error readout before filing a bug report.

</details>

Found a bug or have balance feedback? [Open a GitHub issue](https://github.com/GrabowMar/NuclearOption-WingCommand/issues) and include your game version, the aircraft/order/formation involved, and the relevant bit of `LogOutput.log`.

---

<div align="center">

**[⬆ Back to top](#-wing-command)**

Made for the Nuclear Option community • [MIT Licensed](LICENSE)

</div>
