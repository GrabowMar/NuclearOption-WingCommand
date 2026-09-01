# WingCommand

BepInEx 5 / Harmony plugin for **Nuclear Option** (Unity 2022.3 **Mono**, Steam appid 2168680).
Turns the game's single-wingman AI into a commandable squadron: tactical map orders, formations,
a pylon-level loadout editor, a squadron shop/economy, and radio chatter.

- Plugin GUID `com.marci.wingcommand`, assembly `WingCommand`, version in
  [WingCommand.csproj](src/WingCommand/WingCommand.csproj) (`<Version>`) **and**
  `Plugin.PluginVersion` in [Plugin.cs](src/WingCommand/Core/Plugin.cs) — keep the two in step.
- Target game version is tracked in [build/meta.json](build/meta.json).

## Layout

| Path | What |
| --- | --- |
| `src/WingCommand/` | the whole plugin, one concern per file, grouped into the seven folders below |
| `src/WingCommand/Pure/` | engine-free logic, and **exactly** what the test project compiles — see below |
| `src/WingCommand/Core/` | entry point, config, reflection, roster, orders, pilots |
| `src/WingCommand/Flight/` | `Pilot` states and formation geometry |
| `src/WingCommand/Combat/` | weapons, ROE, target deconfliction, countermeasures |
| `src/WingCommand/Economy/` | shop, delivery, reserve, loadout templates |
| `src/WingCommand/Ui/` | everything that draws: WMC panel, HUD, map layers, radial |
| `src/WingCommand/Comms/` | radio chatter and its HUD |
| `src/WingCommand/Core/Plugin.cs` | entry point: `harmony.PatchAll`, persistent manager GameObject |
| `src/WingCommand/Core/WingConfig.cs` | every BepInEx binding, one `Bind*` method per section |
| `src/WingCommand/Core/GameAccess.cs` | every reflection accessor into private game members; resolved once at startup |
| `tests/WingCommand.PureTests/` | xunit, net8.0; `<Compile Include>`s the `Pure/` files directly (no project reference) |
| `build/` | `package.ps1` (release assets), `copy-to-game.ps1` (deploy), `meta.json`, `nomnom/` manifest |
| `docs/` | in-flight design plans |
| `.opencode/skills/` | the modding knowledge base (gitignored; also mounted at `.claude/skills/`) |

## Commands

Build the plugin (needs the .NET 8 SDK plus a local game install at the `GameDir` path in the csproj):

```bash
dotnet build src/WingCommand/WingCommand.csproj -c Release
```

Run the tests — this is the only part CI runs, and the only part that works without the game installed:

```bash
dotnet test tests/WingCommand.PureTests/WingCommand.PureTests.csproj -c Release
```

Deploy to the game, then package a release:

```bash
pwsh build/copy-to-game.ps1
```

```bash
pwsh build/package.ps1
```

## Conventions

- **Engine-free logic goes in `Pure/`** so it can be tested. Anything touching
  `UnityEngine`/`Assembly-CSharp` types cannot live there — put it in the folder that fits
  and keep the decision logic pure. `Pure/` and the test project's `<Compile Include>` list
  are the same set: add a file to one and add it to the other, or it is not being tested.
- Namespace is `WingCommand` throughout. The folders group files; they are not namespaces,
  so moving a file between them never touches a `using`.
- **Private game members are reached through `GameAccess`**, never inline reflection: a
  publicizer is blocked by this machine's application-control policy. `GameAccess.Initialise()`
  runs before `PatchAll` so patches can stand down when the game layout moves.
- Types are `internal` by default; only the BepInEx entry point is public.
- Comments explain *why* — especially which decompiled behaviour forced a workaround.
  Match that density; do not narrate the obvious.
- Version bumps touch csproj, `Core/Plugin.cs`, and `build/meta.json`; `package.ps1` reads the
  version back out of the built DLL so the manifest cannot drift.

## Before touching game code

Read the skills in `.claude/skills/` first — they are decompile-derived and cover the traps
that fail silently:

- `nuclear-option-modding` — Autopilot (`AutoAim` overloads, `effort`/`bankAllowed`), the
  `Pilot` state machine, combat AI, economy, Harmony/BepInEx gotchas, UI hooks.
- `nuclear-option-general-modding` — game directory anatomy, mod types, NOMM/NOMNOM packaging.

Two that bite hardest: a Harmony patch class without a **class-level** `[HarmonyPatch]` is
skipped in total silence, and AI pilot states only tick where the aircraft is simulated —
gate on `aircraft.LocalSim`, and gate world-state writes on `leader.IsServer`.

## Don't

- Commit `dist/` or `bin/`/`obj/` (gitignored).
- Commandeer a human-controlled aircraft (`aircraft.Player != null`).
- Hand-edit `build/nomnom/WingCommand.json` version fields; regenerate via `package.ps1`.
