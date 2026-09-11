# Wing Command — Agent Guide

One BepInEx DLL for Nuclear Option, providing tactical squadron command, dynamic pilot management, custom flight autopilots, radio chatter, and cockpit avionics.

Canonical plugin GUID is `com.marci.wingcommand`. Never alter the assembly name, plugin GUID, or public interop contracts without an explicit user migration task.

## Repo Layout

```
Core/          Composition root, Plugin.cs, WingCommandManager, WingRegistry, WingMember,
               config, directive dispatch — the shared kernel every module depends on
Personnel/     Pilot lifecycle: recruitment, takeover, loss/kill-credit, search and
               rescue, survival perks, departure chatter, custom pilots, portraits,
               RTB recovery/settlement, inbound taxi/parking/ejection patches
Flight/        Autopilot behaviors (AttackRunState, OrbitState, Formation, etc.),
               surface-unit tasking (WingSurface)
Combat/        Weapons discipline, ROE arbitration, jamming, countermeasures
Economy/       Squadron store, loadout manager, launch queue, supply reserve, hangar
Comms/         Radio dispatch, subtitle generation, audio cue playback
Ui/            Cockpit WMC MFD screen, HUD markers, radial command wheel, map layer
Pure/          Engine-free deterministic domain logic (tested by PureTests), split by
               the module it backs: Pure/Ai (behaviour arbitration engine), Pure/Flight,
               Pure/Combat, Pure/Economy, Pure/Comms, Pure/Pilots, Pure/Ui
Interop/       Cross-mod PresenceBoard integration
Assets/        Embedded PNG icons, pilot layers, and maneuvers.json
tests/WingCommand.PureTests/  Pure xUnit test suite (net8.0, 570+ tests), mirroring
               Pure/'s sub-folders one test file per production file
```

## Start Narrow

1. Classify the request before exploring: `Core`, `Personnel`, `Flight`, `Combat`, `Economy`, `Comms`, `Ui`, or `Pure`.
2. Keep pure math, formatting, and decision tables inside `Pure/`.
3. If changing `Pure/`, ensure no `UnityEngine` or `Assembly-CSharp` references are introduced.
4. Verify changes with `tests/WingCommand.PureTests` and `nomod asm verify --mod wingcommand`.

## Architectural Invariants

- **The Pure Rule**: `Pure/*.cs` must never reference Unity or game assemblies. The xUnit test project (`tests/WingCommand.PureTests`) compiles `Pure/` against net8.0 to enforce this boundary.
- **Harmony Patches**: Every patch class must have an explicit `[HarmonyPatch]` attribute or use explicit method targets. Always verify with `nomod asm verify --mod wingcommand` to catch renamed or missing game methods.
- **Module Facades**: Code outside `Personnel/`/`Combat/` must call them through `Personnel/PersonnelFacade.cs` / `Combat/CombatFacade.cs`, never the internal classes (`WingPilotRoster`, `WingRecruitment`, `WingWeapons`, `RoeRules`, etc.) directly. Each facade is a static class with one nested class per internal subsystem — `PersonnelFacade`: `Roster`, `Recruitment`, `Takeover`, `Recovery`, `SearchAndRescue`, `CustomPilots`, `Portraits`, `KillCredit`, `Departure`, `DepartureChatter`; `CombatFacade`: `Weapons`, `Roe`, `Countermeasures`, `Tactical` — each member forwarding 1:1 to the internal implementation with no renaming and no behavior change. Files inside a module may call each other directly. `Economy/EconomyFacade.cs` is the same pattern, planned as follow-up module-boundary work.
- **Avionics & Shared UI**:
  - The shared avionics layout and styling kit lives in `nomodkit/shared/avionics` and is linked via `WingCommand.csproj`.
  - MFD bezel slot assignments and cross-mod discovery are handled through `NOAvionics.BezelRegistry`.
  - Presence sharing (e.g. wingman IDs) is published to `NOAvionics.PresenceBoard`.
- **Version Alignment**:
  - `PluginVersion` in `Core/Plugin.cs` and `<Version>` in `WingCommand.csproj` must stay strictly synchronized.

## Toolchain & Nomodkit Integration

Use the `nomod` CLI (`C:\Users\marci\dev\nomodkit`):

```powershell
nomod doctor                                            # Check game install and mod config
nomod build --mod wingcommand                           # Build Release DLL
nomod test --mod wingcommand                            # Run pure xUnit tests
nomod asm verify --mod wingcommand                      # Statically verify all Harmony patches
nomod deploy --mod wingcommand --stop-game --confirm    # Deploy DLL to BepInEx/plugins
nomod bridge patches --compare wingcommand              # Cross-check patches against live process
nomod bridge ai-units                                   # Inspect live AI unit states in-game
nomod logs tail                                         # Tail BepInEx/Unity logs
nomod decompile <dll> --type <TypeName>                 # Decompile game C# class
nomod bundle list <path>                                # Inspect Unity asset bundle objects
nomod rip <target>                                      # AssetRipper project reconstruction
```

## Validation

Before submitting any code changes:

```powershell
dotnet build .\WingCommand.csproj -c Release
dotnet test .\tests\WingCommand.PureTests\WingCommand.PureTests.csproj
nomod asm verify --mod wingcommand
```
