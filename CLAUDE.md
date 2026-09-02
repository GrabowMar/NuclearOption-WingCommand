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
| `src/WingCommand/Pure/WingTuning.cs` | every tuned number that is **not** a setting — see below |
| `src/WingCommand/Core/GameAccess.cs` | every reflection accessor into private game members; resolved once at startup |
| `tests/WingCommand.PureTests/` | xunit, net8.0; `<Compile Include>`s the `Pure/` files directly (no project reference) |
| `build/` | `meta.json` (id, category, target game version), `nomnom/` manifest |
| `docs/` | **shipped** design history, not a work list — see the banner at the top of each |
| `.opencode/skills/` | the modding knowledge base (gitignored; the copy Claude loads is installed at `~/.claude/skills/`) |

## Commands

Build, test, deploy and release all go through **nomodkit** (`C:\Users\marci\dev\nomodkit`),
which replaced `build/copy-to-game.ps1` and `build/package.ps1`. It also exposes the same
operations as MCP tools, so Claude can run them and get structured results.

```bash
nomod build --mod wingcommand
```

```bash
nomod test --mod wingcommand
```

Check every Harmony patch and `AccessTools` string against the installed game assembly
**before** deploying. This catches the two failures that are silent at runtime — a target
that no longer exists, and a patch class missing its class-level `[HarmonyPatch]`:

```bash
nomod asm verify --mod wingcommand
```

Deploy, then package a release:

```bash
nomod deploy --mod wingcommand
```

```bash
nomod package --mod wingcommand
```

The raw .NET commands still work if nomodkit is unavailable — building needs the .NET 8
SDK plus a local game install at the `GameDir` path in the csproj, and the tests are the
only part CI runs and the only part that works without the game installed:

```bash
dotnet build src/WingCommand/WingCommand.csproj -c Release
```

```bash
dotnet test tests/WingCommand.PureTests/WingCommand.PureTests.csproj -c Release
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
- **A new number is a constant in `Pure/WingTuning.cs` until someone asks for it.** A setting
  has to survive one question: *could a player know what to set it to?* Bank authorities,
  engagement ranges, XP awards and the helicopter power constant could not — they are derived
  from the game's own arithmetic or from flight testing, and fifty-three of them made the
  dozen real preferences impossible to find. `WingConfig` holds preferences; `WingTuning`
  holds numbers, each with the reasoning that fixed it. Anything that varies by Smart vs
  Performance belongs in `WingBrain` instead of either.
- **Don't add a switch that `Mode` already gates.** Jamming and aerobatics each had their own
  toggle *and* a `WingBrain` gate, so there were two ways to turn one thing off.
- Version bumps touch csproj `<Version>`, `Core/Plugin.cs` **and** `build/meta.json`. The
  build **fails** if any of the three disagree (`CheckPluginVersionMatchesCsproj` and
  `CheckMetaVersionMatchesCsproj` in the csproj), so none of them can drift. This used to be
  a note here saying to update `build/meta.json` by hand, which is precisely what did not
  happen — the game folder spent a release advertising the previous version.
- **Never hand-write a hash.** A hash describes one exact binary and builds are not
  deterministic: the assembly MVID changes on every rebuild, so a hash typed into a file is
  wrong the next time anyone builds. `nomod deploy` derives the deployed `meta.json`
  from the DLL it is deploying, and `nomod package` prints the release hashes ready to paste
  into `build/nomnom/WingCommand.json`. `build/meta.json` therefore holds `"hash": null` —
  deployment fills it in, and a number typed there could only ever be wrong.
- **Version numbers only ever go up, and only to a number nothing has shipped as.** Check
  `git tag` before choosing one: the tree once carried 0.9.5.x while the newest published
  tag was v0.9.1.2, so four versions' worth of numbering described releases that never
  existed and the next real release had nowhere to go.

## Guardrails

The build is kept at **zero warnings**, so any warning is a real finding. Two tripwires
enforce things that were previously only written down:

- **Dead code**: `IDE0051`/`IDE0052` are warnings (see `.editorconfig`). Unused private
  members and write-only fields fail the build. Three rounds of manual cleanup each missed
  members that this caught immediately — grep cannot distinguish `WmcScreen.Grey` from
  `WingUi.Grey`, and the compiler can. The seven files holding Unity or Harmony entry points
  disable `IDE0051` at the top with a reason, because those are called by reflection;
  **do not add the pragma anywhere else** — a new one means real dead code.
- **Untested pure code**: the test project globs `Pure/*.cs` rather than listing files, so a
  new pure file is compiled and tested the moment it lands, and a file that is *not* pure
  breaks that build immediately.

Neither tripwire replaces reading the code. `internal` members unused across the whole
assembly are still invisible to the compiler — that is how a whole preset subsystem stayed
in the tree after the UI that used it was replaced.

## Before touching game code

Read the skills first — they are decompile-derived and cover the traps that fail silently.
They are installed at `~/.claude/skills/` (source of truth: `.opencode/skills/` here, and
the nomodkit repo, which installs them with `nomod skills --confirm`):

- `nuclear-option-modding` — Autopilot (`AutoAim` overloads, `effort`/`bankAllowed`), the
  `Pilot` state machine, combat AI, economy, Harmony/BepInEx gotchas, UI hooks.
- `nuclear-option-general-modding` — game directory anatomy, mod types, NOMM/NOMNOM packaging.
- `nuclear-option-modkit` — the nomodkit tooling and the debugging workflows.

Two that bite hardest: a Harmony patch class without a **class-level** `[HarmonyPatch]` is
skipped in total silence, and AI pilot states only tick where the aircraft is simulated —
gate on `aircraft.LocalSim`, and gate world-state writes on `leader.IsServer`.

`nomod asm verify --mod wingcommand` now catches the first of those mechanically, along
with a patch target or `AccessTools` string that a game update renamed. Run it before
deploying; it does not need the game running.

## Don't

- Commit `dist/` or `bin/`/`obj/` (gitignored).
- Commandeer a human-controlled aircraft (`aircraft.Player != null`).
- Hand-edit `build/nomnom/WingCommand.json` version fields; regenerate via `nomod package`.
