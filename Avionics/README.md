# Dual-mod rules (Wing Command × Boscali Summer)

This folder is compiled into **both** plugins (`NOAvionics`). It is the only shared
runtime contract they have. There is no third BepInEx plugin and no project reference
either way.

Read this before adding an MFD screen, an armed map click, an AI scoring patch, or
anything that would need the other mod to exist.

## Product split

| | Wing Command | Boscali Summer |
|---|---|---|
| Promise | Flight lead of *your* aircraft | A living battlefield |
| Owns | Recruited wing, orders, formations, ROE, loadouts, shop, squadron pilots, chatter, takeover | Fire/ruins, occupancy, music radio, score perks, support call-ins, theater SA |
| Must not own | World destruction, garrisons, music player, faction-wide aircraft command | Recruited wing, formations, loadouts, squadron shop, standing aircraft orders |

**Only Wing Command may task aircraft, and only recruited wingmen.** Boscali doctrine may
bias *friendly, non-wing* mission AI. It must not retask a unit whose persistent-id hash
is on the presence board.

Each mod is complete alone. Together they should read as flight lead + air command, not
two directors.

**KREW** (`com.marci.wingcommand.krew`) is a Wing Command *companion*: hard BepInEx
dependency on WC, soft detect of KAR/BOTE. It is not this protocol. Do not make Boscali
or WC hard-depend on each other the way KREW depends on WC.

## How they talk

Each plugin compiles its own copy of these types. They agree through
`AppDomain.CurrentDomain.SetData` using **BCL values only** (strings, `int[]`,
`object[]` of primitives, `Hashtable`). Never put a custom type in the domain — the
other assembly cannot cast it.

| Type | Job |
|---|---|
| `BezelRegistry` | Named MFD slot occupancy |
| `MapPicker` | Exclusive armed map gesture |
| `PresenceBoard` | Optional snapshots (wing ids, theater stance) |
| `TheaterScoring` | Pure doctrine bias, capped |

Owner ids, column names, and AppDomain keys are **constants on those types**. Do not
restate them here; change them in C#.

Public façades (`WingCommand.Interop.*`, `BoscaliSummer.Interop.*`) are for
reflection-by-name. Prefer the presence board: it does not need `Type.GetType`.

## Bezels

Vanilla map MFD: six buttons per column, about three filled. Free slots are **null
entries inside the list**, not indices past its end.

| Id | Column | Notes |
|---|---|---|
| `BezelRegistry.Wmc` | left | Wing Command |
| `BezelRegistry.Ops` | left | Boscali perks, support call-ins, career record |
| `BezelRegistry.Str` | left | Boscali strategic layer: theater SA, frontline, tasking, logistics, doctrine |
| `BezelRegistry.Rad` | right | Boscali music radio |
| `BezelRegistry.Set` | right | Boscali saved map settings |

That is four Boscali screens against roughly three free slots per column, so the left
column is full and `TryClaim` will spill to the right. A fifth would evict one of
these; add a tab to an existing screen instead.

OPS and STR are the two command screens. Theater SA used to be a tab *inside* OPS,
mounted through an `ITheaterPage` contract; that contract is gone. They now share only
the shell factory (`AvScreen`), which is a widget, not a seam.

They are separate because they answer different questions. OPS is about the player —
what they have earned and what they may call in. STR is about the battlefield — who
holds what, who is flying, what the theater can still pay for. Neither needs the
other to install, and either may fail to claim a slot without taking the other down.

**A new screen claims with `MfdBezel.TryClaim` / `BezelRegistry.TryClaim`, then
`Bind`.** A private first-null `TryClaimSlot` will race the other plugin in the same
frame. Release the id on scene reset. Re-claiming the same id returns the existing
reservation.

After `SetupButtons()`, re-enable the button (`enabled` + `interactable`) and wire
`PressLeftButton` / `PressRightButton` if the unused `-` slot had no `onClick`.

## Map gestures

One owner. `MapPicker.TryArm(owner, gesture, prompt)` fails if someone else is armed.
`Disarm` is owner-only.

Wing point-orders (`MapPicker.WingPoint`, left click) and Boscali support
(`MapPicker.Support`, right click) already participate. A new armed click that reads
`Input.GetMouseButtonDown` without the picker will fire **and** move a wingman.

Show `MapPicker.Prompt` on the open MFD status strip while busy.

## Scoring

`TheaterScoring.Bias` is the policy. Enemy analyzers and wingmen return `1`. Multipliers
are capped (`Minimum` / `Maximum` on that type). Do not put a 2–4× doctrine knob back
on `AnalyzeTarget`.

Wing Command publishes living wing persistent-id hashes every tick
(`WingInteropPush.Publish`). Boscali reads them; it does not import Wing Command.

## Changing this folder

1. Edit the C# here. On an incompatible shape change, bump the type's `ApiVersion`
   (property or const on that type — a const is baked into the *caller* at compile
   time, so a cross-assembly handshake must be a **property**, as `WingHost.ApiVersion`
   is).
2. Plugin csprojs compile `shared/avionics/*.cs` with `Exclude=*Tests.cs`. Do not
   ship `AvionicsProtocolTests`.
3. Both test projects compile the **whole** folder. `AvionicsProtocolTests.Run` is
   invoked from Wing Command xunit and Boscali `CommandTests`.
4. Release-build **both** plugins. A protocol change that only rebuilt one side is a
   mixed-version handshake.
5. Do not add Unity, Harmony, or `Assembly-CSharp` types to this folder. Tests are
   net8.0 with no game install.

## Harmony

- **Wing Command:** explicit `harmony.PatchAll(typeof(...))` list in `Core/Plugin.cs`.
  `PatchAll(assembly)` is gone. A new patch class that is not on that list is skipped
  in total silence. `asm_verify_harmony` still cannot see a class you never passed to
  Harmony.
- **Boscali:** each feature's `IModFeature.PatchTypes`. A patch file that is not on
  the list is dead (this already happened with stronghold `TakeDamage`). Do not
  register two prefixes that skip the original on the same method without a plan.

## UI

Shared pure tokens live in `shared/avionics/AvionicsTokens.cs` (`NOAvionics.AvTokens`).
The shared Unity widget kit lives in `shared/avionics-ui/` (`NOAvionics.Ui`), linked by
plugin csprojs only.

Live panels are **green-glass** (WMC / OPS / STR / RAD), chamfered SDF (`AvSprites`),
unified at `AvTokens.PanelWidth`. Height is `AvTokens.PanelHeight` at the floor and up to
`AvTokens.PanelHeightMax` where the column measures taller — `AvScreen.ResolveHeight`
measures the slot a panel was parented into, so no screen hard-codes a canvas size.
The width ceiling is the panel dock column, not taste: a wider panel is clipped. Type floor **10px** (`FontMicro`).
`Navigation.Mode.None` on every Unity `Selectable`. Clear `EventSystem` selection after clicks.
Text fields use a Rewired keyboard guard.

Status strip = pinned 56px 2-line footer. Shows hover tooltip, else armed-picker prompt,
else ambient page status. A disabled control states *why* on that strip.

## Do not

- `BepInDependency` or a project reference between WC and Boscali
- A third shared BepInEx plugin for this protocol
- `MessageUI.GameMessage` for wing chatter or doctrine (frameless HUD / OPS terminal)
- Vanilla `PlayerRank` as a Boscali HQ lock (Progression already abandoned that budget)
- Support vehicle airdrops as a second aircraft shop
- Add a fifth Boscali bezel, or copy WMC pages into Boscali
- Commandeer `aircraft.Player != null`
