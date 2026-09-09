<div align="center">

# ✈ WING COMMAND
### Tactical AI wing control for Nuclear Option

[![Release](https://img.shields.io/badge/release-0.9.2.1-blue?style=for-the-badge)](https://github.com/GrabowMar/NuclearOption-WingCommand/releases)
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

Wing Command works independently of Boscali Summer. On its own, WMC fills the available
side area beside its normal bezel button, keeping clear of the screen edges and bottom
controls. With Boscali Summer loaded, WMC registers its panel and lets Boscali own
the layout. Wing Command does not replace stock MFD pages, resize the map, or
rearrange other panels and bezel buttons. SET belongs to Boscali Summer.

The six-sector command wheel uses embedded PNG icons. Aerobatic routines use editable
[JSON phase presets](Assets/README.md); tactical turns retain the native autopilot.

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

<p align="center">
  <img src="Assets/Screenshots/tactical-map.png" width="100%" alt="WMC tactical panel and map showing Hold, Move and Seek and Destroy orders for three wingmen"><br>
  <sub>Tactical map — select wingmen and issue individual orders</sub>
</p>

<table align="center">
  <tr>
    <td align="center" colspan="2"><img src="Assets/Screenshots/wmc-tactical-supply.png" width="100%" alt="WMC Tactical and Supply panels side by side"><br><sub>Tactical + Supply — command the flight and requisition aircraft</sub></td>
  </tr>
  <tr>
    <td align="center" colspan="2"><img src="Assets/Screenshots/wmc-loadout-wing.png" width="100%" alt="WMC Loadout and Wing panels side by side, including a pilot portrait and dossier"><br><sub>Loadout + Wing — configure aircraft and meet the pilots</sub></td>
  </tr>
  <tr>
    <td align="center" width="50%"><img src="Assets/Screenshots/wing-in-combat.png" width="100%" alt="Wing aircraft over the ocean amid missile and countermeasure trails"><br><sub>The wing in combat</sub></td>
    <td align="center" width="50%"><img src="Assets/Screenshots/defensive-break.png" width="100%" alt="FS-20 wingmen breaking away and releasing flares over the ocean"><br><sub>Defensive break — hamsters doing hamster things</sub></td>
  </tr>
</table>

## 📦 Install

**NOMM (recommended):** install [NOMM](https://github.com/Combat787/NOMM), search **WingCommand**, install, launch.

**Manual:**

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option folder and launch the game once.
2. Take `WingCommand.dll` from the [latest release](https://github.com/GrabowMar/NuclearOption-WingCommand/releases) and place it at:

   ```text
   Nuclear Option/BepInEx/plugins/WingCommand/WingCommand.dll
   ```

3. Launch and check `BepInEx/LogOutput.log` for `WingCommand 0.9.2.1 loaded.` — if it's missing, see [Troubleshooting](#-troubleshooting).

> [!WARNING]
> Keep the DLL only in `plugins/WingCommand/`. A stray copy in `plugins/` can load the wrong build.

Settings live in `BepInEx/config/com.marci.wingcommand.cfg`. Edit them in-game with
[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (**F1**).

## 🚀 Quick start

1. **Load a mission** as host or single-player.
2. **Get a wing.** **WMC → Supply**: select friendly AI on the map and press **Assign Selected** (once for the price, again to confirm), or requisition fresh airframes from the catalogue.
3. **Take control.** **WMC → Tactical**: click a wing icon or roster row to select one, Shift-click to add, **Select All** for everyone.
4. **Order.** Try **Hold Here** — the button highlights, then right-click the map.
5. **Kit them out.** Build a template on **WMC → Loadout**, pick it in the **FIT** row on **Supply** before requisitioning.
6. **Or the radial.** Open the normal radial menu → **Wing Command** for instant whole-wing orders.

Fixed-wing and rotary can't share a formation; the shop and recruit lists filter incompatible types automatically.

## 🕹️ Command interfaces

**Radial** — fast, always **whole-wing**. Recruitment/release/requisition are deliberately absent.

```text
Wing Command
├─ Combat: Attack Target · Engage · Splash 'Em · Disengage
├─ Flight & Tasking: Form Up · Hold Position
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
| Left-click Hold Here / Seek & Destroy / Land / Cargo | Arm that order (button stays highlighted) |
| Left-click Attack Target with contacts designated | Attack them immediately; Attack stays armed |
| Left-click Attack Target with no designation | Arm Attack for a map target |
| Right-click the map with an order armed | Issue that order at the point, or at the clicked hostile for Attack |
| Right-click the map with no order armed | Send the selection there (`MOVE` marker) |
| Shift-right-click | Queue that order (Move only appends to an existing Move; otherwise it replaces) |
| H+ / H- / S+ / S- | Change Move altitude and speed |
| Click the armed button again / Escape | Cancel the armed order |

Left-click on hostile, friendly non-wing, ground and naval icons behaves as in stock.
Right-click is the wing order: the armed WMC command, or a move if none is armed. Closing
WMC or switching tabs stops intercepting wing-icon clicks. Map moves are temporary routes —
at the final point every wingman returns to formation. **Seek & Destroy** instead begins
autonomous engagement after reaching its marked area.

Wing icons keep the stock faction fill and gain a thin, unfilled ring with space around
the aircraft silhouette. **Clear Targets** does not remove the ring or change WMC command selection. Selected command recipients
also have brackets while Tactical is open. `UI/Highlight` controls the outlines.

## 📋 Orders

| Order | What it does |
|---|---|
| **Form Up** | Close on assigned slots and hold station on you |
| **Attack Target** | Hit your locked target immediately, or arm the button and right-click a hostile on the map. Radial sends everyone; scoped WMC distributes contacts and may hold surplus back as cover |
| **Splash 'Em** | Every selected wingman pours its whole loadout into one target until it's dead or they're dry. WMC only — not a quick call |
| **Engage** | Hunt within the configured leash, return if they stray. Sets ROE to FREE |
| **Seek & Destroy** | Fly to a marked map area, then begin autonomous engagement under the current ROE |
| **Disengage** | Break on separated headings, countermeasure, egress, then form up |
| **Hold Here** | CAP a point while still applying ROE |
| **Deliver Cargo** | Fly cargo to a chosen point, drop it, report, rejoin |
| **Land Here** | Set compatible helicopters down near the point |
| **Refit** | Return to base, replenish fuel and stores, take off again, then rejoin |
| **Stand Down** | Cancel the current task and loiter near friendly airbases or ships |
| **Roster RTB** | On an individual roster row, dismiss a wingman home; its plane and pilot return to their pools after recovery |
| **Formation dial** | Swap between the six shapes on the fly |

Orders stick through missile defence and leash recall, including new orders issued while
defending. Idle Attack/Engage aircraft regroup temporarily and resume when combat becomes
available again. Automatic bingo/Winchester return, when enabled, ends the current task.
Interrupted manoeuvres and target orders whose target has died finish by returning to formation.

Holding aircraft circle in the same direction on separate radii, spaced by formation slot.

**Roster RTB completes.** Down and shut down at a friendly base, the pilot leaves the cockpit
(not scored as a death), the airframe returns to faction stock, allocation spent on a
purchased aircraft is refunded, and the squadron pilot returns to the pool. Set
`Engagement/RtbReturnsToReserve = false` to park on the apron instead of despawning.

**Splash 'Em vs Attack.** Attack is measured — spread designations, a useful-attacker cap,
surplus held as cover, seconds between launches. Splash 'Em drops all of that: one target,
no cap, sustained volley through missiles → rockets → gun. It keeps weapon/target matching
and shot envelopes, so it empties a loadout on something worth emptying it on. Bingo and
Winchester still recall it.

## 🎯 Rules of engagement

Orders choose the flight task and explicit combat intent. During formation, holding and
temporary recall, ROE controls incidental fire.

| ROE | Weapons policy | Under fire |
|---|---|---|
| **Defend** (`Hold` in config) | Incoming missiles only | Tries to intercept the missile |
| **Escort** | Air threats around the formation | Prioritises the threat to you |
| **Free** | Any valid opportunity target in range | Fires while holding the current task |

No ROE leaves formation. Every wingman still ducks its slot briefly to dodge an inbound
missile regardless of ROE — self-preservation always wins, and the interrupt resumes the
standing order.

An active Attack or Splash order authorizes its designated target. During a temporary
recall, the target is remembered but firing follows ROE until the attack resumes. Missile
interception remains available during Jam and when optional opportunity scans are disabled.

## 🛩️ Formations

| Shape | Best for |
|---|---|
| **Echelon Right** | General-purpose swept line, elements staggered |
| **Line Abreast** | Broad sensor and weapons frontage |
| **Trail** | Spaced column with a bounded step down |
| **Combat Spread** | Wide two-ship elements in an offset box |
| **Finger Four** | Asymmetric four-ship elements, repeated for larger wings |
| **Vic** | Balanced V for compact groups |

Echelon Left, Diamond, Ladder and Wall still parse from old config files but aren't in the
selector. Slots are leader-local and transition gradually. In hard turns, lateral spacing
compresses and trail depth grows so wingmen aren't given impossible speed demands.
Near terrain, the formation limits its shared tilt to keep the low outer slots above the ground.
Separation predicts the closest approach over four seconds, adds vertical deconfliction near
terrain, and bounds corrections. Threat spacing widens the formation only during a missile
warning or with hostiles near, then settles back. You don't babysit spacing.

If you stay below a fixed-wing member's safe flying speed, it announces **holding wide**
and flies a shallow circuit at formation altitude. It automatically rejoins after you
maintain enough speed. A nearby, aligned aircraft that overshoots flies a separated lane
alongside you until you pass it, then eases back into its slot. Distant aircraft keep closing
on a curved rendezvous path, even when ahead of you or on the opposite heading. Closure
speed controls braking as they reach their slots. Distant joins aim farther ahead along your
predicted flight path and use full throttle while the remaining gap allows safe deceleration.
Fitted airbrakes help shed excessive closure, then retract before matching your speed; low
airspeed, hard banks, climbs and terrain warnings restrict their use. The standing formation order is preserved.
Braking and engine-response estimates adapt per wingman from stable flight samples;
terrain and collision avoidance retain priority over formation corrections.

The minimum formation speed uses the airframe's published stall speed with a safety margin,
converted from km/h, rather than its AI landing-approach speed. The VT-7's formation floor
is 216 km/h; its much higher nominal landing parameter does not force it to fly past a
slower leader. Turn authority uses the same airspeed envelope, and native flight-assist
filtering runs once per steering command.

Distance, relative closure, forward airspeed, fuel, damage, pilot experience and ROE jointly
adjust capture effort and damping. Aircraft condition can widen the whole formation while
each pilot retains its own control response. Smooth leader tracking filters small attitude
changes, responds faster to large turns and roll reversals, and keeps slot motion continuous.
Hard maneuvers still respect each airframe's turning and speed limits.

Requisitioned fixed-wing aircraft — VTOL jets included, since the game still taxis them —
are placed on the takeoff threshold of the nearest allowed field that stocks the airframe,
and depart under the game's own taxi and takeoff states. Helicopters and tiltwings come out
of a hangar or helipad exactly as the faction's own do. One departure at a time per field,
so two purchases never occupy the same strip; Supply reports a jammed field before a purchase
spends allocation. Nothing is moved once it exists, and no ground steering is overridden:
WingCommand chooses the pose and then gets out of the way.

Return To Base, dismissal and refit all fly the stock approach home. After landing, RTB
disembarks immediately and returns the airframe through the game's own Returned path, so it
is not scored as a kill. Refit parks on the pad, skips that eject, replenishes, and launches
again from that pose. WingCommand gives back its own runway queue entries whenever a departure
ends without taking off, so an interrupted launch cannot jam the strip for the rest of the
mission.

With verbose logging enabled, instability triggers an eight-second diagnostic burst at
five samples per second, with a thirty-second interval between burst starts. The
`[FormationControl]` records include time, aircraft ID, recovery mode, control inputs,
airspeed margin, terrain-warning urgency, learned response estimates, airbrake demand and
intercept prediction time.

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

**Flying one:** the **FIT** row on Supply picks **STANDARD FIT** — the player's current
default for that airframe this mission, the same loadout the game applies when you start in
it — or a saved template.

- Equipment is fitted at aircraft creation, so one already airborne can't be refitted — the
  **Wing** tab shows what each carries.
- An active mission aircraft assigned from the map flies **as found** and can't be refitted.
- STANDARD is re-read at requisition time, so a change in the aircraft menu applies to the
  next new airframe. A template chosen on Supply applies instead of STANDARD.
- Deleting a template doesn't disturb anything flying it; a purchase order pointing at a
  deleted template falls back to STANDARD.
- Loadouts don't change price. A requisition is list price.

**Cargo:** a transport carries whatever cargo pod is on its cargo pylon. **Deliver Cargo
takes a drop point** — press it, right-click the map, same as Hold/Land; helicopters set the load
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
- Queued requisitions can be cancelled before a hangar accepts them. Once a launch is
  accepted, it stays reserved until the aircraft arrives or the native launch fails;
  a slow door sequence does not trigger a refund while an aircraft is still on its way.
- **Wing Reserve** holds up to three specific airframes across all types. `HOLD` pulls one
  faction airframe out of AI-accessible stock; `RELEASE` returns it. It doesn't create supply.
- **Releasing a wingman sends it home** (`REL` on the Wing roster) — it flies the stock
  pattern back, stops counting against the squadron limit immediately, and on landing the
  airframe returns to faction stock, the pilot returns to the pool, and purchase allocation
  is refunded.
- A paid requisition is refunded on RTB rather than held as a free relaunch. Manual
  **HOLD** / **RELEASE** on Supply still parks faction stock in the three-slot reserve.
- Every purchase and assignment previews its fee. Credits and supply move only after
  recruitment or spawn succeeds. The 25% map-assignment fee is not refunded on RTB.

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

- Pilots belong to the squadron. Successful base recovery returns them to the pool with
  their record. Released RTB pilots remain assigned until recovery; killed pilots don't return.
- Experience comes from kills, completed sorties and engagements survived; rank rises through
  Rookie → Wingman → Veteran → Ace → Legend.
- Rank has a small real effect — at the top, ~12% more weapon reach and off-boresight and
  ~12% faster shot cycling, plus modest formation-control adjustments in Smart mode.
  `Pilot/RankEffect = 0` keeps the record and earned perks but disables their effects.
- Each promotion grants **one random, unique survival perk from a pool of 24**. Promotions crossed in one XP
  award all count; imported experienced pilots receive perks for their existing ranks.
  Rookie / Wingman / Veteran / Ace / Legend require **0 / 120 / 360 / 720 / 1200 XP** and
  hold **0 / 1 / 2 / 3 / 4 perks**. XP awards remain 25 per kill, 40 per recovered sortie,
  and 10 per survived engagement. The 24 dossier badges show actual earned perks;
  hover for the effect and whether it is earned.
- Random recruits draw from 80 surnames, 26 initials and 96 callsigns, with varied service
  backgrounds, personal habits and radio styles. Callsigns stay unique within the roster.
- Experimental portraits retain six generated faces, with separate male/female pools of
  four hairstyles (plus bald) and two uniforms each. Wing and Supply show the same face for
  an identity, including imported pilots.
  The small atlas is embedded in the DLL; portraits are composed once on demand and released
  when the mission resets. No image service, extra runtime dependency or shader is required.

| Perk | Survival effect |
|---|---|
| **LUCK** | 30% chance per incoming missile to bias guidance 350m sideways; proximity blasts still hurt. |
| **FUEL DISCIPLINE** | 30% less engine fuel consumption. Leaks and fires remain dangerous. |
| **TOUGHNESS** | 35% less seated-pilot damage; does not strengthen the airframe. |
| **G TOLERANCE** | 75% less pilot damage from extreme G loads. |
| **COMMANDO** | One 50% escape chance after landing alive; return after 120 seconds if still free and alive. |
| **FIREPROOF** | 75% less fire damage to the seated pilot. Does not extinguish the aircraft. |
| **BLAST SURVIVOR** | 60% less explosion damage to the seated pilot. |
| **BALLISTIC VEST** | 60% less projectile damage to the seated pilot. |
| **CRASH TRAINING** | 60% less impact damage to the seated pilot; aircraft can still be destroyed. |
| **SECOND CHANCE** | Survive one otherwise fatal seated-pilot hit at 1 HP per sortie. Rearmed by assignment or successful base sortie; subsequent damage can kill. |
| **COOL HEAD** | 40% less engine fuel use while a native incoming-missile warning is active. |
| **HIGH ALTITUDE CRUISE** | 40% less engine fuel use above 3000m AGL. |
| **LOW LEVEL EVASION** | 25% guidance-error chance per incoming missile, rolled when first encountered below 300m AGL. |
| **RADAR GHOST** | 30% guidance-error chance per incoming ARH or SARH missile. |
| **HEAT GHOST** | 30% guidance-error chance per incoming IR missile. |
| **NOTCH EXPERT** | 40% guidance-error chance against radar missiles first encountered within 15 degrees of a beam approach, while moving over 30m/s. |
| **CHAFF REFLEX** | Fitted chaff dispensers have 50% shorter burst intervals; uses ammunition faster. |
| **FLARE REFLEX** | Fitted flare dispensers have 50% shorter burst intervals; uses ammunition faster. |
| **ECM SPECIALIST** | Fitted self-protection radar jammer produces 75% stronger ECM at normal power cost. |
| **FAST LEARNER** | 50% more XP from every award, reaching ranks and survival perks sooner. |
| **QUICK DRAW** | 35% shorter wing weapon-command intervals; native reload and lock requirements still apply. |
| **STANDOFF** | 25% larger wing weapon-employment envelope; native weapon readiness still applies. |
| **SURVIVALIST** | 60% less incoming projectile, blast and fire damage to the dismounted pilot before native armour calculations. |
| **PATHFINDER** | 35% independent escape chance with a 60-second return delay; with Commando, 67.5% chance and 60 seconds. One roll per ejection. |

Missile guidance-error chances combine as independent chances, with an 80% cap and one roll per missile/pilot pair at first encounter. Conditional evasion perks use the altitude, seeker and flight angle at that encounter; changing conditions does not grant another roll. Fuel savings multiply, with a floor of 25% normal consumption. Damage resistances multiply. No perk guarantees aircraft survival.

Perk percentages are fixed while effects are enabled; `RankEffect` scales the existing rank
bonuses. `PilotProgression = false` disables XP awards and perk effects. SAR status tracking
continues to distinguish survivors from KIA. Records and earned perks last through aircraft
changes **within the current mission**; this does not add a campaign save system.

**Search and rescue.** Ejection creates a **DOWNED** roster entry once the game's living
survivor is confirmed. That pilot cannot be selected or assigned until rescued. Friendly
native rescue (player or AI) and return at a friendly base restore the same pilot with their
XP, perks and record. Enemy capture shows **CAPTURED**; a confirmed death after ejection
shows **KIA**. A lost survivor signal shows **MIA**, never an automatic rescue. Disabled
aircraft receive a short grace period for the game's delayed ejection sequence.

On **WMC → Wing**, inspect a downed pilot and click **DISPATCH SAR** to send the nearest
eligible idle wing helicopter to their landing position. It must have native capture capacity,
more than 25% fuel, and be in Formation or Hold/Orbit. It uses the existing Land Here approach;
the native game performs the rescue when a suitable friendly unit stops nearby. This is an
explicit order and leaves the helicopter at the landing site afterward. Landing failure,
terrain and enemy fire can still prevent rescue. Player aircraft and native AI can also rescue
normally without this button. **Water rescues use the game's helicopter hoist**; automated
hoist operation is not included.

The implementation audit and manual flight checks are in [SURVIVAL-SYSTEM.md](SURVIVAL-SYSTEM.md).

**Radio.** Calls use frameless subtitles at top centre, not the game-message feed — speaker
as `M. "COBALT" ADEYEMI`, a smaller line for flight position and aircraft. A command to
several aircraft gets one element acknowledgement from its lead; skipped aircraft don't
answer as if they complied. Urgent missile/damage/loss calls jump the queue. Each pilot
carries a persona (professional, aggressive, calm, dry) that picks between lines for the same
event — a small seam for later mission dialogue. Transmissions can be tailored in
settings (`Comms/Radio`: `Off`, `Text`, `TextAndTone`). With `TextAndTone`, each call opens
with the game's native radio click. The mod no longer writes ordinary notices into the
game's message boxes.

Delivered wingmen report taxiing, takeoff and airborne once per phase. Departure reports
use an idle channel with at least five seconds between them across the wing, replace stale
phases with current ones, and yield to urgent calls. An airborne rejoin report also replaces
the ordinary rejoining acknowledgement for that departure.

## 💀 Takeover

Killed or ejected with wingmen still flying, they hold in a safe orbit and a takeover window
opens (number keys work). Pick one and you spawn a fresh copy of that aircraft — same
airframe, loadout, fuel, livery and motion — and take the stick, with the AI source removed
to avoid reusing its AI/network state. Normal respawn and defeat flows still work. Single-player
and host; `Engagement/TakeoverOnDeath`.

## 🗺️ HUD and map

- Wingmen use a thin green map ring; active wing targets have amber rings. Rings keep a
  clear gap around the icon and a one-pixel stroke across map zoom levels.
  Tactical command selection adds brackets. Native faction fills, type and heading remain visible.
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

**Boscali Summer required?** No. Boscali owns MFD layout when installed.
Standalone, the vanilla panels stay intact and
WMC fits beside the native bezel. Wing Command does not control map wallpaper, grid,
or background opacity; appearance stays with vanilla or the mod providing it.

**Do templates cost extra?** No. A requisition is list price whatever you hang on it.

**Where are templates saved?** `com.marci.wingcommand.cfg`, under `Loadout/SavedTemplates`.
Clearing that value deletes them all.

**Why is a pylon `BLOCKED`?** Something else you've fitted rules it out — the airframe's own
exclusion rule. Clear the blocking store and it comes back.

**Helicopters and jets in one formation?** No — they fly too differently. The shop and
recruit lists hide incompatible types.

**Bought an aircraft but the squadron's at its limit — scammed?** No. Missions cap airborne
AI and the cap shrinks with more players. Push past it with **OVER LIMIT** at rank 3.

**Charged twice for the same aircraft?** No — an RTB landing refunds the allocation spent on
that airframe and returns it to faction stock. Requisitioning it again is a new purchase.

**Shaky formation?** Wingmen use filtered leader motion and curved approaches to their
slots. Repeated large corrections after small stick movements are a bug; report the
airframe, formation, ROE, and slot error. A growing gap can also mean the leader exceeds
the follower's speed or turn capability.

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

- Reset `Formation/Spacing` to default (120m) if altered.
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

Settings live in `BepInEx/config/com.marci.wingcommand.cfg`. Edit them in-game with
[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (**F1**).
Flight laws, bank limits, and integration safety floors are fixed in code, while preference,
tactical rules, hotkeys, and appearance can be configured.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| AI | `Mode` | `Smart` | `Smart` (full AI passes) or `Performance` (lean updates for heavy MP hosts) |
| AI | `AiSharpTurns` | `true` | Enable stronger AI turns; changes apply on the next steering update. |
| AI | `AiTargetSpreading` | `true` | Enable AI target spreading on the next target selection; still disabled in Performance mode. |
| AI | `AiMissileWarningRepair` | `true` | Repair warning subscriptions on the next combat entry. Disabling leaves existing subscriptions intact. |
| Formation | `Shape` | `EchelonRight` | Initial formation at mission start |
| Formation | `Spacing` | `120` | Lateral and longitudinal slot spacing in metres (`50`–`300`) |
| Engagement | `DefaultRoe` | `Hold` | Initial ROE (`Hold`, `Tight`, `Free`) |
| Engagement | `AutoReturnOnEmpty` | `true` | Auto RTB on Winchester or bingo |
| Engagement | `BingoFuel` | `0.15` | Auto-return fuel fraction (`0.05`–`0.40`) |
| Engagement | `LeashDistance` | `5000` | Max pursuit distance in metres before breaking off and rejoining (`2000`–`15000`) |
| Engagement | `MaxWingmenPerTarget` | `2` | Maximum wingmen allowed to focus-fire the same target (`1`–`4`) |
| Engagement | `RtbReturnsToReserve` | `true` | RTB despawns, refunds purchase allocation, returns pilot and airframe |
| Engagement | `TakeoverOnDeath` | `true` | Offer a surviving wing aircraft after pilot loss |
| Comms | `Radio` | `TextAndTone` | Squadron radio traffic (`Off`, `Text`, `TextAndTone`) |
| Pilot | `PilotProgression` | `true` | Pilots keep a record, rank and small skill effect |
| Pilot | `RankEffect` | `1.0` | Multiplier on rank shooting bonuses (`0` = cosmetic progression only) |
| Shop | `ShopEnabled` | `true` | Enable purchasing wingmen from faction supply |
| Shop | `RecruitmentCostPercent` | `0.25` | Active-AI reassignment fee, fraction of list value (`0.0` = free) |
| Loadout | `SavedTemplates` | `""` | Your saved per-pylon templates (managed by WMC) |
| Keys | `WingMenu` | `C` | Hold for the command wheel; aim and release to confirm, right-click to cancel. Set `None` to disable. |
| Keys | `QuickRejoin` | `None` | Optional hotkey: whole wing rejoins formation |
| Keys | `QuickEngage` | `None` | Optional hotkey: whole wing engages |
| Keys | `QuickDisengage` | `None` | Optional hotkey: whole wing falls back / disengages |
| Keys | `QuickAttackTarget` | `None` | Optional hotkey: attack player's targeted contact |
| Keys | `CycleRoe` | `None` | Optional hotkey: cycle ROE (Hold → Tight → Free) |
| UI | `ShowWingHud` | `true` | Compact roster docked beside the tactical map |
| UI | `WingHudX` / `WingHudY` | `0` / `0` | Live roster offsets in F1, in scaled HUD units. +X moves right, +Y moves up; negative values move left/down. Reset both to zero to dock beside the map. |
| UI | `UseMfdPanel` | `true` | Cockpit MFD WMC screen alongside BDF/MAP/HUD |
| UI | `MapCommands` | `true` | Tactical wing selection and tasking on maximised map |
| UI | `Highlight` | `WingAndTargets` | Roster and target tinting (`Off`, `Wing`, `WingAndTargets`) |
| UI | `WingMemberColor` | `#39FF65` | Hex colour for wingmen across HUD and map |
| UI | `WingTargetColor` | `#FFB020` | Hex colour for units engaged by wing |
| UI | `TacticalPauseInSingleplayer` | `false` | Slow down game time when tactical command screen is active |
| UI | `TacticalPauseScale` | `0.25` | Simulation time-scale during tactical pause (`0.0` = full pause) |
| UI | `ExternalHitmarkerAudio` | `true` | Hitmarker audio confirmation in 3rd-person/orbit view |
| Debug | `EnableDebugActions` | `false` | Master switch for development cheats (host-only) |
| Debug | `VerboseLogging` | `false` | F1 “Debug action logging” switch, visible without advanced settings. Applies immediately; logs action requests/results, frame numbers, state transitions and flight diagnostics to BepInEx `LogOutput.log`. Independent of cheats. |
| Debug | `DebugSpawnAircraft` | empty | F1 aircraft selector: override the debug spawn type using compatible catalogue aircraft, including modded planes, without faction stock or rank requirements. Empty uses your current aircraft. |
| Debug | `SpawnDebugWing` | — | F1 button: spawn a full wing of the selected debug aircraft in formation; requires flying above 80 m and host authority |
| Debug | `FreePlanePurchases` | `false` | Requisitioned aircraft cost no allocation |
| Debug | `DisableWingSizeLimit` | `false` | Ignore 3-wingman squadron cap |


The Debug cheats are F1-only, off by default, and unsupported. Squadron size is capped at 3 wingmen by design (matching HUD and WMC layout); `DisableWingSizeLimit` bypasses this for testing. Global AI `SkillScale` / `BraveryScale`, player-specific target protection, `WingPriceGrowth`, `RecruitRange`, and `AdditionalWingReservePerType` are retired and ignored.

The former `MFD/BackgroundOpacity`, `CheckeredGrid`, `CustomImageEnabled`, and
`CustomImageFile` settings are also retired and ignored. Existing image files are untouched.

## 🔩 Implementation

WingCommand adds no custom network messages. It drives aircraft through the game's existing
pilot states and autopilot, uses the stock economy and supply calls, and limits Harmony
patches to UI dispatch, marker colour, missile-warning repair and AI target deconfliction.
Loadout options come from the airframe's own `WeaponManager.hardpointSets`; pylon exclusion
is the game's own `HardpointSet.BlockedByOtherHardpoint`. This mod defines no weapons.
Private game members for the native radial integration are resolved through reflection; a
game update that renames one is logged, with the fallback interface left available.

The codebase map and development guidance are in [ARCHITECTURE.md](ARCHITECTURE.md). Much of the mod is
developed with AI coding assistance under maintainer direction, review and live flight
testing. Contributions and test reports welcome.

## 🏗️ Building

Requires the .NET 8 SDK, BepInEx 5, a local Nuclear Option install, and the shared avionics
sources from a `nomodkit` checkout (defaults to `../nomodkit`). Custom paths can be passed
as `-p:GameDir="C:\path\to\Nuclear Option" -p:NomodKitDir="C:\path\to\nomodkit"`.

```powershell
dotnet build WingCommand.csproj -c Release
dotnet test tests/WingCommand.PureTests
```

Release assets:

```bash
nomod package --mod wingcommand
```

→ `dist/WingCommand.dll` and `dist/WingCommand-<version>.zip`. Attach **`WingCommand.dll` first**
to a GitHub release — NOMM installs the bare DLL; the ZIP is for manual installs. The package
script reads the version from the built assembly and prints SHA-256 hashes.

## Licence

[MIT](LICENSE)

---

<div align="center">

**[⬆ Back to top](#-wing-command)** • Made for the Nuclear Option community

</div>
