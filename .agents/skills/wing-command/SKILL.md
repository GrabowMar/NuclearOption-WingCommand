---
name: wing-command
description: Maintain, diagnose, optimize, or extend the Wing Command BepInEx/Harmony squadron tactics mod for Nuclear Option. Use for work in this repository.
---

# Wing Command

One BepInEx DLL for Nuclear Option (Unity 2022.3.62f2 Mono, BepInEx 5.4.23.4 under Doorstop 4.4.1), providing deep tactical squadron command, dynamic pilot rosters, radio communications, and flight behavior orchestration.

## Tooling & Nomodkit Integration

The `nomod` CLI and MCP server (`C:\Users\marci\dev\nomodkit`) provide the verified workflow for developing Wing Command:

| Operation | Nomod Command |
|---|---|
| Verify install & toolchain | `nomod doctor` |
| Build mod | `nomod build --mod wingcommand` |
| Run pure tests (570+ tests) | `nomod test --mod wingcommand` |
| Verify all Harmony patches statically | `nomod asm verify --mod wingcommand` |
| Deploy plugin DLL to game | `nomod deploy --mod wingcommand --stop-game --confirm` |
| Live bridge inspection | `nomod bridge patches --compare wingcommand`, `nomod bridge ai-units`, `nomod bridge inspect <type>` |
| Log analysis | `nomod logs tail`, `nomod logs errors`, `nomod crash` |
| Inspect Unity asset bundles | `nomod bundle list <path>`, `nomod bundle extract <path>` |
| Decompile game assemblies / types | `nomod decompile <dll> --type <TypeName>` |
| Asset extraction & reverse engineering | `nomod rip <target>` (AssetRipper), `nomod tools launch uabea` |

## Codebase Architecture

The repository is organized by strict subsystem responsibilities:

- **`Core/`**: Entry point (`Plugin.cs`), frame driver (`WingCommandManager`), pilot roster (`WingRegistry`), configuration, recruitment, recovery, and kill attribution.
- **`Flight/`**: Autopilot state machines (`WingPilotState`, `AttackRunState`, `OrbitState`, etc.) and formation/collision geometry requiring live game components.
- **`Combat/`**: Squadron weapons allocation, ROE enforcement, tactical spread, countermeasures, and radar jamming.
- **`Economy/`**: Squadron procurement shop, aircraft delivery, launch-lane sequencing, and supply logistics.
- **`Comms/`**: Radio voice and subtitle routing, chatter generation, and sound cues.
- **`Ui/`**: Cockpit WMC screen (`WmcScreen.*`), HUD elements, radial command wheel, and map command overlays.
- **`Pure/`**: Pure engine-free C# logic (formation math, chatter formatting, AI arbitration, tuning). **Never import `UnityEngine` or `Assembly-CSharp` here.**
- **`Interop/`**: Cross-mod state publishing via `PresenceBoard` (linked from `nomodkit/shared/avionics`).
- **`Assets/`**: Embedded cockpit icons, pilot portraits, and JSON aerobatic maneuver definitions.

## Key Invariants

1. **Pure Layer Isolation**: Everything in `Pure/` must compile in a clean .NET 8 test runner (`tests/WingCommand.PureTests`) with zero references to Unity or game assemblies.
2. **Version Synchronization**: The `PluginVersion` constant in `Core/Plugin.cs` must always match the `<Version>` element in `WingCommand.csproj`.
3. **Harmony Verification**: Run `nomod asm verify --mod wingcommand` before deploying. Any broken method or unannotated patch class will be detected statically.
4. **Cross-Mod Presence**: Do not introduce a hard dependency on Boscali Summer. Use `NOAvionics.PresenceBoard` to publish wing membership and query shared stance.
5. **Asset Embedding**: New icons or textures must be declared as embedded resources in `WingCommand.csproj` with appropriate logical names.

## Validation Commands

Run before committing:

```powershell
dotnet build .\WingCommand.csproj -c Release
dotnet test .\tests\WingCommand.PureTests\WingCommand.PureTests.csproj
nomod asm verify --mod wingcommand
```
