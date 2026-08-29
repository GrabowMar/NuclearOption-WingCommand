# WingCommand Tactical Layer Overhaul

**Target:** `0.9.x` tactical beta, followed by a release candidate  
**Current baseline:** WingCommand `0.8.3`, Nuclear Option `0.34.2`  
**Status:** implementation plan  
**Release posture:** preserve the working flight/AI core; replace the command and logistics experience around it

## 1. Outcome

The update should make WingCommand feel like a native tactical extension to Nuclear Option rather than a large control panel attached to the map.

The finished experience has three deliberately different surfaces:

- The **tactical map** selects individual wingmen, targets, and positions.
- The **WMC page** shows command scope, issues scoped orders, and manages logistics.
- The **native radial menu** gives predictable, whole-wing combat commands without requiring map interaction.

The player should be able to split a three-aircraft wing in a few deliberate actions—for example, send Two to hold over an airbase, order Three onto a selected radar, and keep Four in formation—without acquiring the workload of an RTS or a full mission planner.

This is the largest update to the mod, but it must not become a rewrite of the formation controller, defensive manoeuvres, stock combat AI integration, or takeover system. Those are already the strongest and riskiest parts of the codebase.

## 2. Product principles

1. **One order, one obvious result.** Order names describe what the aircraft will do and when it will stop doing it.
2. **Selection is explicit.** The UI always says `ALL`, `2 OF 3`, or `NONE`; an empty or stale selection must never silently become the whole wing.
3. **Map interaction stays shallow.** Select aircraft, select an order, and—only when needed—select one target or point. No waypoint chains or nested mission planning.
4. **Radial commands remain dependable.** Radial and direct hotkey commands always affect the whole wing, regardless of a map selection the player may have forgotten.
5. **Use Nuclear Option systems.** Allocation, faction supply, AI aircraft limits, airbases, pilot states, and the stock reserve formula remain authoritative.
6. **Server authority is visible.** Controls that cannot work for a non-host client are disabled with a reason rather than appearing to accept the command.
7. **No big-bang replacement.** Build the new command domain first, then migrate the map, orders, WMC, and economy in vertical slices.

## 3. Findings from the current build

### What should be preserved

- `WingMember` already keeps a persistent standing order and queues orders received during a missile-defence interruption.
- `AttackRunState` and `WingRegistry.AttackTargets` already support useful-target counts and multi-target distribution.
- Formation, ROE, and self-preservation are correctly separate concepts.
- Purchases already use player allocation, faction supply, mission restrictions, rank, airframe compatibility, and the mission AI cap.
- Purchase funds and stock are committed only after a successful spawn.
- Takeover converts one surviving AI aircraft into one player aircraft without consuming another airframe or leaving the AI source alive.
- Map and HUD symbology already share a single role model.
- The release build is currently clean: zero build warnings and zero build errors.

### What is holding the tactical layer back

- `WingCommandManager.Execute` dispatches every order to the entire roster.
- `MapCommandLayer` treats right-click on aircraft only as recruitment and has no wing-command selection state.
- The map's `selectedIcons` list doubles as the player's weapon target list, so it cannot safely represent command selection.
- `Orbit Here` captures the leader's current position; it does not use a map point.
- `Land Here` lands below each helicopter's current position; it does not accept a destination.
- The WMC combines formation, ROE, roster, ten action buttons, shop, delivery settings, pagination, and debug actions in one tall panel.
- All action labels use a dim grey treatment that reads as disabled, even when active.
- Repeated failures can flood the stock message feed (`Wing is full` in the supplied screenshot).
- The four generic `Ctrl+1..4` map groups affect ground and naval controls outside the mod's core purpose and conflict conceptually with the new wing selection model.
- Formation selection exposes ten choices, including several near-duplicates, and the radial formation submenu has too many directional slices.
- There are 63 bound configuration entries. Many are low-level controller constants or legacy/fallback settings that should not appear in the normal configuration view.

### Native reserve behavior to reuse

Nuclear Option already protects aircraft from automatic AI deployment. For every aircraft definition, `FactionHQ.DeployAIAircraft` only deploys when:

```text
stock > mission reserveAirframes
        + friendly player count × extraReservesPerPlayer
```

WingCommand should add an optional friendly-faction wing holdback to that native threshold. It should not create a second inventory, remove aircraft from the synchronized supply dictionary, or replace the AI deployment method.

## 4. Release scope

### Must ship

- Separate command selection for individual wingmen on the maximized tactical map.
- The same selection mirrored in WMC roster rows.
- Scoped execution of every relevant order.
- Real point data for map-placed Hold and rotary landing orders.
- A smaller, unambiguous order set with centrally defined names, hints, and capabilities.
- A two-tab WMC page: `TACTICAL` and `SUPPLY`.
- Recruitment cost and a confirmation/preview before commandeering active mission AI.
- Clear faction stock, protected stock, allocation, AI-cap, and price feedback.
- Optional additive AI holdback using the native reserve field.
- Configuration and feature-scope reduction.
- Mouse and controller-safe map behavior.
- A beta and release-candidate playtest pass.

### Ship if stable; cut before compromising the release

- A full transit-and-approach state for landing helicopters at a distant map point.
- Per-order map vectors from selected aircraft to a destination.
- Advanced fast delivery as an explicitly non-standard option.

If distant landing misses the cut, the order must be labelled `LAND BELOW` and must not pretend to be map-positioned.

### Explicitly out of scope

- Multiple player-controlled flights or a squadron hierarchy.
- Patrol polygons, altitude/speed task editors, or an RTS-style mission planner. A lightweight
  Shift-click point queue is permitted for direct tactical movement, but it remains temporary
  and resolves back to the wing's ROE at the final point.
- A new command-points currency, recurring upkeep, or refunds.
- A loadout editor for purchased wingmen.
- Custom network messages or non-host AI control.
- Strategic friendly-AI planning outside the player's wing.
- A rewrite of fixed-wing/rotary formation flight, panic manoeuvres, or target deconfliction.

## 5. Tactical interaction model

### 5.1 Command selection

Command selection must be independent from `DynamicMap.selectedIcons` and the Combat HUD target list.

- Opening the WMC `TACTICAL` tab activates wing-command selection on the map.
- Clicking a wingman selects only that member.
- Shift-click toggles additional wingmen.
- Controller selection selects one member; `ALL` on WMC provides a controller-friendly multi-select path.
- Clicking a hostile or neutral unit continues through the stock target-selection path.
- Clicking a friendly non-wing aircraft continues through the stock path and makes it available to the recruitment preview.
- Closing or switching away from WMC stops intercepting wing-icon clicks. The explicit selection may persist for the next map visit, but radial and hotkey actions ignore it.
- A member that dies or leaves is pruned from selection. If that leaves an explicit selection empty, the scope becomes `NONE`, not `ALL`.
- New recruits join the dynamic `ALL` scope but are not silently inserted into an explicit subset.

Map symbology should distinguish three states without replacing the stock aircraft silhouette:

| State | Presentation |
|---|---|
| Wing member | Existing green tint and caret |
| Command-selected member | Brighter tint plus a second thin selection bracket |
| Wing target | Existing amber target treatment |

Selection must affect the tactical map only. It should not recolor the in-cockpit HUD as though the aircraft were a weapon target.

### 5.2 Targets and points

- `ATTACK` consumes the player's current stock target list and distributes valid targets across the selected scope.
- `HOLD` arms a point cursor. The next click inside the map captures `DynamicMap.GetCursorCoordinates()` and shows a persistent hold marker.
- `LAND` uses the same point-cursor flow for rotary aircraft.
- Escape, right-click, changing WMC tabs, or selecting a different point order cancels an armed cursor.
- Point capture starts only after the command button's click has been released, preventing the button press itself from becoming the map point.
- Invalid clicks leave the command armed and show one concise reason.

The map should show a small native-style glyph and label such as `HOLD · 2/3`. Avoid permanent route lines unless playtesting proves the destination is otherwise hard to understand.

### 5.3 Scope rules by interface

| Interface | Scope |
|---|---|
| Tactical map + WMC | Current explicit command selection |
| Native radial | Whole wing |
| Direct Rejoin/Engage hotkeys | Whole wing |
| Automatic bingo/panic/leash behavior | Individual affected member |

This difference is intentional and must be stated in tooltips and the README.

## 6. Revised orders

Movement orders continue to answer **where the aircraft flies**. ROE continues to answer **what it may shoot**.

### Core order set

| UI name | Internal behavior | Completion |
|---|---|---|
| **FORM UP** | Rejoin the assigned slot on the player. | Persistent until replaced. |
| **ATTACK** | Prosecute the player's selected target(s), using the existing assignment planner. | Return to Form Up when the assigned target is gone or invalid. |
| **ENGAGE** | Use stock combat AI within the existing leash around the player. | Persistent; re-engage after leash recovery. |
| **HOLD** | Transit to and orbit a chosen map point; use current ROE while holding. | Persistent until replaced. |
| **DISENGAGE** | Break away, countermeasure, egress from the nearest known threat, then rejoin. | Automatically changes to Form Up after standoff/timeout. |
| **RTB** | Hand control to the stock landing state. | Persistent; never auto-rejoin. |

### Contextual orders

| UI name | Availability | Behavior |
|---|---|---|
| **DELIVER CARGO** | Only when at least one selected helicopter carries cargo and has a stock transport state. | Send only capable selected members; report skipped members. |
| **LAND** | Rotary selection only. | Transit to the map point, settle, descend, and remain down. |

### Roster actions, not orders

- `ASSIGN SELECTED` / `RECRUIT NEAREST`
- `RELEASE MEMBER`
- `RELEASE ALL`
- Formation choice
- Rules of engagement
- Requisition/purchase

### ROE presentation

Keep the existing enum for compatibility, but use clearer interface labels:

| Existing enum | UI label | Meaning |
|---|---|---|
| `Hold` | **DEFEND** | Hold assigned flight path, intercept urgent missiles, mirror the player's ground attack. |
| `Escort` | **ESCORT** | Weapons free from the assigned flight path, prioritizing threats to the leader. |
| `Free` | **FREE** | Weapons free and permitted to break for the leader-under-attack emergency. |

`HOLD` the order and `Hold` the current ROE must not share the same visible word.

### Required state corrections

- `OrbitState` must receive a stored point rather than deriving it from the leader during `Apply`.
- Holding aircraft must periodically run the same ROE weapon decision used in formation; otherwise Hold is tactically inert.
- `FallBackState` becomes a temporary Disengage flow and calls Form Up after its egress condition instead of orbiting indefinitely at a remote rally.
- A map-positioned rotary landing needs `Transit → Settle → Descend → Down` phases, terrain-aware transit, and a slope/clearance check near the destination.
- Orders issued during panic must queue the entire directive, including target or point—not only the enum.
- Leash recovery for Attack must preserve its target; Hold and Land must preserve their point.

## 7. WMC redesign

Keep the existing `VirtualMFD` installation and stock font/theme borrowing. Replace only the page content.

### Tactical tab

```text
WING COMMAND             [TACTICAL] [SUPPLY]
SCOPE  [ALL]                         2 OF 3

FLIGHT
[x] TWO    VT-7   FORM UP      99%   694
[x] THREE  VT-7   ATTACK       87%   622
[ ] FOUR   VT-7   HOLD         96%   694

ROE       [DEFEND] [ESCORT] [FREE]
FORMATION [<] COMBAT SPREAD [>]

ORDERS
[FORM UP] [ATTACK] [ENGAGE]
[HOLD]    [DISENGAGE] [RTB]
[LAND / DELIVER CARGO when applicable]

STATUS  TWO, THREE: HOLD point armed — click map
```

Design rules:

- Target a panel height near 500–560 px at 1080p rather than the current near-full-height stack.
- Make roster rows clickable and mirror map selection.
- Use a small fixed roster viewport with scrolling when `MaxWingSize` exceeds the visible rows.
- Active buttons use the stock friendly/accent color; only unavailable actions are dim.
- Disabled actions expose a short reason in the status line.
- `X` is replaced by a less accidental `RELEASE` action in the selected row or a detail footer.
- Order confirmation uses one grouped acknowledgement, not one toast per member.
- Identical toast/error text is coalesced for roughly 1.5 seconds.

### Supply tab

```text
SQUADRON SUPPLY
ALLOCATION  42     AI 5/6     WING 2/3
AI HOLDBACK PER TYPE   MISSION 1 + PLAYER 1 + WING [0]

AIRFRAME       STOCK   PROTECTED   NEXT COST
CI-22             4        2            28
VT-7              3        2            71
A-19              1        1            89

SELECTED  VT-7 VAGRANT
BASE DELIVERY · NEAREST FRIENDLY AIRBASE
AIRFRAME 32 × SLOT MULTIPLIER 2.25 = 72

[REQUISITION]
```

Design rules:

- Call this `SUPPLY` or `REQUISITION`, not a shop, in player-facing copy.
- Show stock and protected stock separately. Protected means protected from automatic AI deployment, not owned by the player.
- Keep base delivery as the normal and initially visible path.
- Select an offer first, then confirm it from a detail footer. Do not put an immediate `BUY` button on every row.
- Show why an offer is unavailable before confirmation: allocation, wing capacity, AI cap, stock, rank, restriction, host status, or delivery failure.
- Preserve the current transaction invariant: no funds or supply move until an aircraft exists successfully.
- If advanced fast delivery remains enabled, display its surcharge and non-standard status explicitly.
- Supply rows may scroll; the whole WMC page must not grow with catalogue size.

### Responsive and input requirements

- Verify at 1280×720, 1920×1080, 2560×1440, 3440×1440, and at least one UI-scale setting above 100%.
- No control may render outside the map canvas or under a bezel button.
- Mouse, keyboard, and controller must all reach selection, `ALL`, core orders, tab switching, and cancel.
- WMC should refresh text at the existing throttled rate; do not allocate formatted strings every frame.

## 8. Radial menu redesign

The radial remains a combat shortcut, not a second full WMC.

Recommended commander page, six slices maximum:

- Form Up
- Attack My Target
- Engage
- Disengage
- Cycle ROE
- Tasking

`Tasking` contains Hold at Current Position, RTB, and a dedicated Formation dial with the six
core shapes. Cargo/Land may appear only for a compatible rotary wing.

Remove recruitment, release/disband, and the ten-entry formation picker from the radial. Those are deliberate management actions and are better on WMC/map. The standalone fallback radial remains available for compatibility but is advanced and unbound by default.

## 9. Logistics and balance

### 9.1 Active-AI recruitment cost

Recruiting an already active mission aircraft currently transfers a strategic asset into direct player control for free. Introduce a one-time reassignment cost paid from normal player allocation.

Recommended initial formula:

```text
recruit cost = aircraft definition value
             × 0.25
             × WingPriceGrowth ^ current wing size
```

Rules:

- Preview the exact total before a map or WMC bulk assignment.
- Validate faction, pilot, local simulation, airframe class, range, wing capacity, and allocation before changing pilot state.
- Deduct only after `WingRegistry.Add` succeeds.
- Track the aircraft persistent ID for the mission; releasing and re-recruiting the same aircraft does not charge twice.
- Do not refund the fee on release, loss, RTB, or takeover.
- No charge for a failed or skipped aircraft.
- `Recruit Nearest` must show/confirm the price in WMC. Remove the surprise-cost version from the quick radial.

The 25% coefficient is a starting point, not a release constant. Tune it with ordinary mission allocations, not the 9,899-credit debug situation shown in the supplied captures. The target is for a first recruit to be a meaningful choice while still remaining clearly cheaper than creating a new airframe.

### 9.2 Purchased aircraft

- Keep full airframe price and the current compounding wing-size multiplier for the first beta.
- Keep the strict mission AI cap.
- Keep base delivery as default.
- Disable fast delivery in the standard profile; if retained, make it advanced and off by default.
- Disable invented undeclared-airframe stock in the standard profile. Retain it only as an advanced compatibility option for workshop aircraft.
- Never refund a purchased aircraft through Release; it remains a live faction AI asset.
- Show a price breakdown so the multiplier is not perceived as a bug.

### 9.3 Wing stock holdback

Add `AdditionalWingReserve`, default `0`, normal range `0–2`. It is additive to the mission-authored and per-player reserve, and it applies per aircraft type because that is how the native system works.

```text
effective AI holdback per type = mission reserveAirframes
                               + friendly players × extraReservesPerPlayer
                               + AdditionalWingReserve
```

Implementation rules:

- Apply only to the local player's friendly HQ and only on the server.
- Capture the mission-authored `reserveAirframes` as the baseline after mission load.
- Set the live field to `baseline + additional`, without patching or replacing `DeployAIAircraft`.
- Restore or forget the delta on HQ/mission reset.
- Detect and log another mod changing the same baseline instead of repeatedly stacking the delta.
- UI copy must say `PER TYPE`; `+1` can protect one of every stocked type and is therefore not a small global reserve.
- A held aircraft remains faction stock and may still be requisitioned by the player. The holdback only stops automatic AI replacement from consuming the protected tail of stock.
- The normal AI cap still applies to the newly purchased wingman. Typically an AI loss creates the free active slot while the holdback prevents an immediate replacement from taking the protected airframe.

Defaulting the additional value to zero is important: Nuclear Option missions already provide `reserveAirframes` and usually `extraReservesPerPlayer`. The feature should expose and optionally extend that balance, not silently double it.

### 9.4 Remove player-specific combat protection

Keep generic target-saturation coordination, which benefits target distribution for every side. Remove or default to zero the extra `PlayerConcentrationPenalty`; specially discouraging AI from attacking the player is a balance advantage unrelated to wing command.

Also remove the optional global skill/bravery scaling patch from the normal release. It affects aircraft outside the wing and is unnecessary scope.

## 10. Scope reduction

### Formations

Expose six core formations in WMC and the radial cycle:

- Echelon Right
- Line Abreast
- Trail
- Combat Spread
- Finger Four
- Vic

Keep the existing solver support and enum values for Echelon Left, Diamond, Ladder, and Wall so old configuration files do not break, but remove them from the normal selector. A legacy selected shape may remain active until the player chooses a core shape.

This reduces choice without risking formation-controller regressions immediately before release.

### Features to remove from the standard release surface

- Generic ground/naval `Ctrl+1..4` map groups.
- Recruitment and Disband on the quick radial.
- Immediate per-row Buy buttons.
- Default fast delivery.
- Default undeclared-airframe allowances.
- Global AI skill/bravery modification.
- Player-specific anti-concentration protection.
- WMC debug actions in release builds; compile or expose them only in development builds.
- The legacy HUD corner setting from the visible configuration surface; it is already inert.

### Features to retain, but mark advanced

- Fallback radial binding and direct order hotkeys.
- Low-level formation control gains and bank/throttle constants.
- AI target-deconfliction tuning.
- Color overrides.
- Fast-delivery distance/surcharge if the advanced mode remains.
- Verbose logging.

## 11. Configuration strategy

The default ConfigurationManager view should contain roughly ten settings, not 63.

Recommended normal settings:

- Max wing size
- Initial formation
- Default ROE
- Recruitment cost percentage
- Additional wing reserve
- Auto RTB
- Bingo fuel
- Takeover on death
- Radio chatter
- Compact HUD

Everything else is advanced, hidden, or retired. Extend `ConfigurationManagerAttributes` with the supported visibility field and use it consistently.

Do not delete or rewrite the user's configuration file. Old keys may remain bound for compatibility but hidden. When a feature becomes inert, log it once at startup if its old key was enabled and document the change in release notes.

Add a hidden configuration schema version only if actual value migration becomes necessary. Do not add a schema version merely to rename visible UI copy.

## 12. Architecture

### New command-domain types

#### `WingDirective`

A small value object containing:

- `WingOrder Order`
- optional `Unit Target`
- optional `GlobalPosition Point`
- `bool HasPoint`
- issue timestamp for diagnostics

`WingMember` stores the complete standing directive. `Apply(WingOrder)` becomes a compatibility wrapper that creates a directive without target/point data.

#### `WingCommandSelection`

Owns `All`, `Explicit`, and `None` modes, selected member references, pruning, and snapshots. It never touches `DynamicMap.selectedIcons`.

#### `WingDirectiveDispatcher`

The only public command entry point for WMC, map, radial, and hotkeys. Responsibilities:

- Resolve interface scope.
- Filter live/capable members.
- Validate target/point requirements.
- Distribute targets.
- Apply directives.
- Produce one structured result: applied, skipped, reason, and display text.

#### `WingOrderCatalog`

Central metadata for order label, short label, hint, icon key, category, point/target requirement, and airframe capability. This replaces duplicated switches and labels across `WingCommandManager`, `WingRadialMenu`, `WmcScreen`, `WingHud`, and `IconFactory` callers.

### Map layer

Split the current `MapCommandLayer` responsibilities:

- `MapCommandLayer`: input mode, point arming/cancel, recruitment selection, and stock-target interoperability.
- `TacticalMapOverlay`: pooled selection brackets and point/order markers under the map's existing UI layers.

Add one narrowly gated Harmony prefix to `UnitMapIcon.ClickIcon`:

- Claim only a live wing aircraft.
- Claim only while the WMC tactical command mode is active.
- Preserve the original for every other icon and mode.

Avoid patching `FactionHQ.DeployAIAircraft`, replacing `DynamicMap.MapControls`, or adding custom network traffic.

### Logistics layer

Refactor, do not duplicate, `WingShop`:

- Pure price/reserve/availability calculations in `WingEconomy`.
- Assignment transaction in `WingRecruitment`.
- Purchase transaction remains in `WingShop`/`WingShopDelivery`.
- Native holdback lifecycle in `WingSupplyReserve`.

Both recruitment and purchase return structured failure reasons for the WMC status line.

### Existing files to keep largely stable

- `FixedWingFormation.cs`
- `RotaryFormation.cs`
- `FormationSolver.cs`
- `FormationFlyState.cs`, except directive access and shared ROE firing helper
- `DefensiveManeuverState.cs`, except complete-directive resume
- `TacticalCoordinator.cs`, except removal of the player-specific penalty
- `WingTakeover.cs`, except selection reset and input-coordination hooks

### Existing files with major changes

| File | Planned change |
|---|---|
| `WingCommandManager.cs` | Stop owning order semantics; route through dispatcher; whole-wing radial/hotkey scope. |
| `WingRegistry.cs` | Capability validation with reasons; scoped helpers; selection lifecycle events. |
| `WingMember.cs` | Store/apply a full directive; preserve target/point through panic and leash recovery. |
| `MapCommandLayer.cs` | Replace generic groups and right-click recruitment with command-mode selection and point capture. |
| `WmcScreen.cs` | Rebuild content as responsive Tactical/Supply tabs using shared catalog/results. |
| `WingRadialMenu.cs` | Six-slice quick command page; remove management and full formation list. |
| `OrbitState.cs` | Consume directive point and fire according to ROE. |
| `FallBackState.cs` | Become temporary Disengage and return to formation. |
| `LandInPlaceState.cs` | Accept a point and add transit/approach phases, or be relabelled Land Below. |
| `WingShop.cs` | Structured offers/reasons, transparent pricing, reserve display. |
| `Plugin.cs` | Smaller visible configuration, holdback/recruit settings, expected patch audit. |
| `WingMapTint.cs` / `WingMarkerBadge.cs` | Command-selection presentation without altering stock target selection. |

## 13. Implementation sequence

Each phase should be independently buildable and reviewable. Do not start the WMC visual rewrite until the command API underneath it is stable.

### Phase 0 — Freeze the behavioral baseline

**Risk:** low  
**Purpose:** make regressions visible before changing interfaces.

- Record current order transitions, purchase invariants, reserve formula, patch list, and takeover behavior.
- Add a manual smoke-test checklist and a small test scene matrix to `docs/`.
- Extract pure formation list/display helpers where needed.
- Keep the release build at zero warnings.

**Exit:** all existing core orders, purchase, recruit, panic resume, and takeover pass the baseline checklist.

### Phase 1 — Command domain and selection model

**Risk:** medium

- Add `WingDirective`, `WingCommandSelection`, `WingOrderCatalog`, and `WingDirectiveDispatcher`.
- Migrate current whole-wing actions through the dispatcher without changing UI behavior.
- Make panic resume and leash recovery use the full directive.
- Add pure tests for selection modes, pruning, capability filtering, and structured results.

**Exit:** current UI behaves the same, but no UI directly loops over `WingRegistry.Members` to issue orders.

### Phase 2 — Map selection vertical slice

**Risk:** high because it touches stock map input.

- Gate wing-icon interception behind active WMC tactical mode.
- Mirror selection in roster rows and map brackets.
- Remove generic `Ctrl+1..4` groups.
- Preserve hostile target selection, friendly non-wing selection, ground/naval map orders, map panning, zoom, and controller select.
- Add `ALL` and explicit `NONE` handling.

**Exit:** one, several, or all wingmen can be selected without changing the player's target list or issuing any unintended stock command.

### Phase 3 — Scoped and point orders

**Risk:** high.

- Apply Form Up, Attack, Engage, Disengage, and RTB to a selection snapshot.
- Add armed point capture and persistent Hold markers.
- Move Orbit to a directive point and enable ROE firing while holding.
- Change Fall Back semantics to temporary Disengage.
- Add rotary point landing if it meets terrain-safety tests; otherwise retain Land Below with accurate copy.
- Ensure target/point directives survive panic interruption.

**Exit:** different live members can hold different standing directives simultaneously and accurately report them in WMC/HUD.

### Phase 4 — Logistics and balance

**Risk:** high because funds, supply, and spawns are irreversible during a mission.

- Add recruitment preview, one-time cost, persistent-ID tracking, and transactional commit.
- Add reserve calculations and `WingSupplyReserve` lifecycle.
- Show AI cap and protected stock.
- Keep purchase commit-after-spawn invariant.
- Disable standard fast delivery and undeclared stock.
- Remove player-specific concentration protection and global skill/bravery scaling from the standard release.

**Exit:** every success and failure path preserves funds, supply, active counts, and reserve baseline exactly.

### Phase 5 — WMC and radial redesign

**Risk:** medium-high.

- Replace the monolithic WMC content with Tactical/Supply tabs.
- Add scrollable bounded roster/catalogue regions.
- Add selected offer detail and confirm action.
- Replace grey-active button treatment and add disabled reasons.
- Coalesce duplicate feedback.
- Reduce the radial and formation selector.

**Exit:** no overflow at supported resolutions and every common action is reachable with mouse and controller.

### Phase 6 — Configuration and code cleanup

**Risk:** medium.

- Mark controller tuning/fallbacks advanced or hidden.
- Remove release debug UI and retired feature paths.
- Keep legacy formation enum values and config parsing.
- Update README, NOMM metadata, configuration reference, and screenshots.
- Audit all Harmony patches and expected-patch logging.

**Exit:** normal ConfigurationManager view is near ten settings; no dead user-facing controls remain.

### Phase 7 — Beta, tuning, and release candidate

**Risk:** release-wide.

- Ship `0.9.0-beta.1` to a small host/single-player test group.
- Freeze features after beta 1; accept only correctness, UX clarity, balance, and compatibility fixes.
- Tune recruitment coefficient, reserve range, and order wording from ordinary missions.
- Ship an RC only after the full matrix below passes twice without a code change between runs.

**Exit:** release candidate meets the definition of done and has no known high- or medium-severity tactical, economy, or takeover defect.

## 14. Verification matrix

### Build and static checks

- Release build succeeds with zero warnings.
- Runtime formation geometry validation succeeds at wing sizes 1, 3, and 8.
- Startup log contains every expected Harmony patch and no unexpected missing private member.
- No new patch claims input or AI behavior outside its exact gated case.

### Selection and stock-map interoperability

- Select one wingman; Shift-select two; deselect the last; select All.
- Kill, release, RTB, and take over a selected member.
- Verify an empty explicit selection disables orders and never becomes All.
- Select hostile air, ground, ship, missile, and friendly non-wing units normally.
- Issue vanilla ground/naval waypoints with WMC open and closed.
- With one or more explicitly selected wingmen, right-click a map point to move; Shift-right-click
  additional points and verify the final point returns Defend/Escort to formation and Free to Engage.
- Pan, zoom, center, minimize, maximize, switch WMC tabs, and cancel an armed point.
- Repeat with controller selection and no keyboard modifiers.

### Orders

- Run each core order on one, several, and all members.
- Hold three aircraft at one point without collision stacking.
- Hold selected members at different points.
- Attack one target and several targets with fewer/equal/more attackers.
- Destroy or despawn a target during attack and leash recovery.
- Issue each order while a member is panicking; verify complete directive resume.
- Trigger bingo/Winchester during Form Up, Attack, Engage, and Hold.
- Disengage under radar, IR, and unknown warning; verify countermeasure shutdown and eventual Form Up.
- RTB never re-enters a combat or formation order automatically.
- Rotary Land tests cover flat ground, slope, water, buildings, near-airbase terrain, low altitude, and long transit.

### Economy and reserves

- Recruit first-time active AI, release it, and re-recruit it without a second fee.
- Bulk recruit with insufficient funds, one invalid aircraft, a full wing, mixed class, dead pilot, non-local sim, and target disappearing during confirmation.
- Fail purchase for funds, stock, rank, restriction, AI cap, host status, and spawn failure; verify no debit.
- Complete purchase and verify one allocation debit, one stock debit, one aircraft, and one queued recruitment.
- Compare UI reserve math to the native formula for zero/one/multiple players.
- Increase/decrease additional holdback and verify AI replacement stops/resumes at the correct per-type threshold.
- Leave/reload missions and change factions; verify no accumulated reserve delta.
- Test with another mod changing `reserveAirframes`; verify detection and no repeated stacking.

### Takeover and mission lifecycle

- Player death and ejection with zero, one, and several candidates.
- Number-key takeover while map command selection previously existed.
- Normal respawn instead of takeover.
- Candidate death while the recovery panel is open.
- Takeover preserves airframe/loadout/fuel/livery/motion and removes exactly one AI source.
- Mission end clears selection, markers, recruitment fee history, undeclared stock history, and reserve baseline.

### UI and performance

- All supported resolutions and UI scales.
- Wing sizes 0, 1, 3, and 8; catalogue sizes 0, 1, 6, 7, and large modded lists.
- Long localized/modded aircraft names are truncated without covering stock/price/action.
- No repeated identical toast spam.
- No per-frame catalogue rebuild, hierarchy creation, marker creation, or formatted roster allocation.
- Thirty-minute combat session with verbose logging off produces no exception and no persistent stale marker.

### Multiplayer

- Single-player.
- Multiplayer host.
- Non-host client sees informative disabled logistics/control state and cannot appear to issue server AI orders.
- Join/leave player changes update native protected-stock calculation.

## 15. Release gates

The overhaul is ready for release only when all of the following are true:

- A player can assign different orders to different wingmen solely from the map/WMC flow.
- No scoped order can silently affect an unselected member.
- Radial/hotkey whole-wing behavior is consistent and documented.
- Target and point orders survive defensive interruption correctly.
- Funds, supply, AI capacity, and reserve holdback are correct on every failure path.
- The current takeover system still works after map-selection and number-key changes.
- The WMC fits 720p and works with controller input.
- The normal config view is substantially smaller and has no inert control.
- Generic Nuclear Option map targeting and ground/naval orders are unchanged outside WMC command mode.
- Fixed-wing and rotary playtests complete without a new formation, terrain, gear, or autopilot regression.
- README and release metadata describe host-only limits, costs, reserve meaning, controls, and advanced balance-breaking options plainly.
- Release build and package complete with zero warnings and reproducible artifact hashes.

## 16. Recommended default release profile

| Setting/behavior | Default |
|---|---|
| Maximum wingmen | 3 |
| Command scope on opening WMC | All |
| ROE | Defend (existing `Hold`) |
| Recruitment fee | 25% of next-slot compounded airframe price |
| Additional wing reserve | 0 per type |
| Purchase multiplier | Existing 1.5 per current wingman, subject to beta tuning |
| Delivery | Base only |
| Fast delivery | Advanced, off |
| Undeclared stock | Advanced, off |
| Takeover | On |
| Generic target deconfliction | On |
| Player-specific target penalty | Off/removed |
| Global AI skill/bravery scaling | Removed from standard release |
| Visible formations | 6 core shapes |
| Generic map groups | Removed |

This profile keeps the mod powerful but makes each additional aircraft consume stock, active AI capacity, and allocation while avoiding direct player immunity or instant behind-the-player reinforcement.
