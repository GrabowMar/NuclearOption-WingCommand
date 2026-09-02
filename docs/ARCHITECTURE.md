# Architecture

Two paths run the mod: **commanding** (player intent → standing order) and **flying**
(standing order → autopilot each tick). They meet at `WingMember`.

## Commanding

```
WMC / map / radial / hotkey
        │  builds a WingDirective (order + optional target + optional point)
        ▼
WingDirectiveDispatcher   ── the only command entry point
        │  resolves scope, filters live/capable members, distributes targets
        ▼
WingMember.stands  ── the member's complete standing directive
```

- **`WingDirective`** carries the payload beside the order. The old model stored a bare
  `WingOrder` and rebuilt a point from the leader's position; a directive survives
  defensive interruption and scoped tasking because the target/point travels with it.
- **`WingCommandSelection`** (`All` / `Explicit`) is deliberately unrelated to
  `DynamicMap.selectedIcons`, which is also the player's weapon target list. Map and WMC
  obey it; radial and hotkeys always mean the whole wing. An empty explicit selection is
  `NONE`, never silently `ALL`.
- **`WingOrderCatalog`** is the one source of order names, hints, and capability metadata —
  it replaced parallel switch statements in five files.

## Flying

Each tick, for a locally-simulated aircraft, one arbiter picks what the wingman does:

```
WingSituation (sensed state)  ──►  WingArbiter  ──►  WingResolution (behaviour + reflex)
                                       ▲
                    reflexes (WingReflexes) rank themselves by band/score;
                    the standing directive is the baseline they can override
```

- Reflexes (missile defence, terrain, bingo, leash recovery) outrank the standing order
  when their band says so, then hand control back. There is **one** place that decides —
  the arbiter — instead of a toggle per behaviour.
- Smart-vs-Performance differences live in `WingBrain`, not in settings and not in
  `WingTuning`.
- Flight states (`FormationFlyState`, `AttackRunState`, `OrbitState`, `CargoRunState`, …)
  are thin: they translate one resolved behaviour into autopilot calls.

## The pure split

`Pure/` is engine-free and is exactly what the test project compiles. Decision logic
(arbitration, formation geometry, weapon-preference weighting, chatter selection, loadout
codec) lives there; anything touching `UnityEngine` / `Assembly-CSharp` stays out. Private
game members are reached only through `GameAccess`, resolved once at startup so a patch can
stand down when the game layout moves.

## What is reused, not rebuilt

Allocation, faction supply, the AI aircraft cap, airbases, the stock reserve formula
(`FactionHQ.DeployAIAircraft`), pilot states, and stock combat AI remain authoritative. The
mod adds an optional additive holdback to the native reserve field rather than keeping a
second inventory. One narrowly-gated Harmony prefix on `UnitMapIcon.ClickIcon` claims a
wing aircraft only while WMC tactical mode is active; everything else goes through the stock
path.
