# Wing Command performance and AI monitoring

Updated: 2026-09-08, Europe/Warsaw. Observation only; no gameplay code or game settings changed by this monitoring task.

## Method and evidence

Applied [Ponytail, full mode](https://github.com/DietrichGebert/ponytail/blob/main/skills/ponytail/SKILL.md): use existing diagnostics, measure first, prefer small fixes to shared causes. A local copy is in `.nomodkit/monitoring/Ponytail-SKILL.md`.

Used nomodkit's `doctor`, `logs stats`, `logs errors`, `bridge health`, `bridge perf`, `bridge ai`, `bridge find`, `bridge inspect`, `bridge type`, `bridge log`, and `bridge patches --owner com.marci.wingcommand --compare wingcommand`.

Raw evidence is local and git-ignored under `.nomodkit/monitoring/`:

- `20260908-215319-026/`: initial single-sample check and preserved current-session log.
- `20260908-215334-319/`: 12 timestamped samples, start/end log snapshots, grouped errors and bridge log. Sample timestamps span 21:53:34–21:55:47 (133.36 seconds); requests within each sample are sequential, not simultaneous.
- `patches.json`, `pilot-type.json`, `pilot-inspect.json`: patch comparison and direct evidence of a living non-player pilot in `WingCommand.FormationFlyState`.
- `Capture.ps1`: reusable bounded collector. Run from this project with `& .\.nomodkit\monitoring\Capture.ps1 -Samples 3 -IntervalSeconds 10`.

The running process was PID 34256, started 21:40:47. Wing Command 0.9.2, Configuration Manager and NOModKit Bridge were loaded; Boscali Summer was disabled. Smart mode and verbose logging were enabled. Scene: GameWorld. No bridge mutations enabled.

The local build and deployed DLL had different SHA-256 hashes when checked. Both files subsequently changed during observation, while the sampled process PID stayed the same. File version 0.9.2 is insufficient to identify the loaded code. Treat source references below as investigation targets, not proof that the running assembly contains that exact source. Future captures record deployed-file hash/mtime, which still do not establish the loaded assembly's identity after a file replacement.

## Performance baseline

| Observation | Result | Interpretation |
|---|---|---|
| Unpaused frame samples | 8; 16.650–16.683 ms, median 16.675 ms (~60 FPS) | Sparse instantaneous samples; not frame-time percentiles or proof of no hitches. |
| Paused samples | 4, time scale 0 | Excluded from the active-play frame summary. |
| Managed heap, unpaused endpoints | 149.61 → 157.45 MiB | Net heap change, not allocation throughput or evidence of a leak. |
| GC counters, unpaused endpoints | Each reported counter 148 → 150 | Report the deltas separately; do not sum identical Mono counters into six collections. |
| Process working set, whole window | 2087.73 → 2143.72 MiB | Includes native/game/bridge memory, scene activity and paused intervals. |
| Process CPU, whole window | 1.64 CPU-seconds per wall second | Average whole-process core usage; not Wing Command's own CPU cost. |
| Log growth between timestamped samples | 240,312 bytes / 133.36 s (~1.8 kB/s) | Includes paused time; larger short bursts occurred during flight. |
| New lines between saved log copies | 649; 596 Formation/FormationControl (~91.8%) | Copies bracket slightly different times than the sample endpoints. |
| BepInEx warnings/errors | None grouped at final query (1,103 records scanned) | Bridge also captured Unity Discord RPC warnings; BepInEx alone is not the complete log surface. |

These measurements do not establish a CPU/GPU bottleneck. No before/after optimisation benchmark has been performed.

## Improvements, ordered by evidence and impact

### 1. Make VT-7 takeoff-to-formation handoff respect flight readiness

Confirmed runtime symptom: new followers enter FormationFlyState around radar altitude 105 m with forward airspeed below the reported fixed-wing minimum:

- End log line 476, frame 49098, id -588944: air 40.2 m/s, minimum 60, stall 50, takeoff 35; Intercept, full throttle.
- Line 553, frame 49784, id -595938: air 35.5 m/s, minimum 60; Station.
- Line 651, frame 50484, id -600954: air 38.6 m/s, minimum 60; Station.

Terrain-abort activates around takeoff handoff before formation resumes. This is evidence of a transition requiring investigation, not proof of a crash or that tiltwing flight is invalid below fixed-wing stall speed. Capture fields also show nozzleAxis=1 and hover=False; the physical nozzle convention must be verified before interpreting them.

Smallest candidate: inspect the common airborne activation path and native VT-7 conversion state; retain safe departure/acceleration control until forward-flight readiness is met. Do not merely raise pitch, bank authority, or gains to chase the slot. Relevant source: `Core/WingMember.cs` (ActivateWhenAirborne), `Flight/FormationFlyState.cs`, `Flight/FixedWingFormation.cs`.

Validation: repeat a VT-7 runway departure with a compatible, steady-speed leader; correlate native state, nozzle/conversion state, airspeed margin, terrain intervention and time to slot capture. Preserve helicopter/tiltwing hover departures where appropriate.

### 2. Distinguish unreachable formation demand from controller failure

Initial preserved log: follower ids -120998, -126060 and -129144 reached slot errors 7,314 / 8,062 / 10,440 m, with negative closing speeds. All 163 parsed formation samples were marked SATURATED. These are diagnostic samples biased toward instability, not a fraction of all flight time.

The later capture explicitly warns at frames 48373 and 49074 that the VT-7's maximum speed is 340 versus the leader airframe's 600. Those are capability maxima, not the leader's instantaneous speed: later capture lines show the leader around 139–177 m/s, so capability mismatch alone does not explain every failed rejoin.

Smallest candidate: preserve the existing warning, measure attainable closure against actual leader motion, and trace energy/heading recovery before changing formation gains. Consider a bounded rejoin/holding response when convergence is impossible rather than indefinitely demanding a moving slot. Shared interception and recovery code should own any eventual fix.

Validation: compare steady straight flight, high-speed separation and turning rejoin separately; report time to capture, time below minimum airspeed, overshoot, separation and whether interrupted orders resume.

### 3. Reduce diagnostic cost without losing transitions and failures

Formation/FormationControl account for 596 of 649 new lines. Current source already gates these behind VerboseLogging and uses burst timers: 0.2-second intervals for 8 seconds, 30-second cooldown (`Pure/WingTuning.cs`, `Pure/FormationRecovery.cs`). Two lines per reporting follower can therefore become roughly ten lines/second during a burst. `FixedWingFormation.Report` also scans wing neighbours to format diagnostics.

Smallest candidate: use existing burst cadence, deduplicate stable repeated snapshots, and lower routine verbosity outside a reproduction. Retain action results, behaviour changes, safety events and exceptions. Measure the same mission with verbose logging on/off before claiming a gain; this pass leaves logging enabled for evidence collection.

For remaining host cost, first compare the existing Smart and Performance modes on matched mission/wing conditions. Performance changes behaviour as well as cadence (geometry every third physics tick, intervals multiplied by 2.5, optional targeting work disabled), so include formation accuracy, reaction latency and target distribution in the comparison. Profile before introducing a new scheduler or cache.

### 4. Correct nomodkit's measurement blind spots

- **Wrong session date/rate:** `src/nomodkit/game/logs.py:stats` subtracts the BepInEx banner date from log mtime. The banner says August 24; the process began September 8. The resulting ~1.3-million-second duration made the reported log rate almost zero. Use measured read offsets and elapsed monotonic time, or process start for a verified current session. Existing archive names also contain the misleading banner date; distinguish archives using file metadata/content, not filename date alone.
- **Empty AI list despite live pilots:** every sampled `bridge ai` response returned zero, but `bridge find Pilot` found 20 objects and `bridge inspect` identified an alive, non-player FormationFlyState pilot. In `game/NOModKit.Bridge/GameOps.cs`, `ExtractPilot` catches exceptions and returns null; `ListAiUnits` returns immediately when pilots exist even if all extraction fails. Investigate the failing member and return partial entries with diagnostic errors; use the existing aircraft fallback when extraction produces no usable entries. Exact throwing member is not established.
- **Observer overhead:** `bridge perf` runs several FindObjectsByType scene scans on Unity's main thread. Keep polling sparse or cache only the census when measured cost warrants it. Its frame time is instantaneous; use a bounded frame recorder for true p95/p99 and per-method profiling for attribution.
- **Heap UI wording:** `BridgeDebugUI` labels the difference in GC.GetTotalMemory readings as allocation rate. That is net live-heap change, affected by collections. Rename it or obtain a true allocation counter before using it to diagnose churn.
- **Build identity:** record loaded assembly MVID/build identity alongside process/mission markers. Deployed-file hash alone is insufficient during concurrent builds or replacement.

### 5. Preserve the safety behaviour that worked

The current log shows missile-break overriding the standing task (frames 51578, 52451, 52527) and standing-task resuming (51972, 53163). This demonstrates transitions, not missile-survival success for every aircraft. Keep urgent missile/terrain reactions independent of any slower optional AI work.

Patch comparison found 25 Wing Command runtime targets and no static-only targets. Four map-related targets were runtime-only; investigate dynamic/static-discovery coverage before calling them missing patches. This check does not prove behavioural correctness.

## Ongoing monitoring rules

Check every five minutes. When the game is absent or nothing actionable changes, stay quiet. During play, collect a short bounded sample and only new log evidence; update this file for new findings, regressions or resolved issues. Use both BepInEx and bridge Unity warnings. Preserve PID/start time, frame/mission boundaries, pause state and deployed-file changes; never combine samples across restarts or log truncation. Reacquire bridge object handles after objects are destroyed.

Do not restart, deploy, change settings, issue orders, spawn aircraft or enable bridge mutations as part of monitoring. Do not duplicate old alerts, infer that an empty AI response means no AI, report a paused FPS result as combat performance, or claim an optimisation gain without a matched comparison.

## Extended movement observation — September 8, from 22:02

At the user's request, extended the immediate capture to 24 samples, approximately five minutes, and increased ongoing five-minute checks to six samples per check. Movement evidence lives in `.nomodkit/monitoring/20260908-220220-065/`. The reusable `movement.py` extracts per-ID movement into `movement.jsonl`, name-only events into `behaviour-events.jsonl`, and per-aircraft extrema into `movement-summary.json`. It retains original log line numbers, parses decimal commas and refuses to merge rotated logs. Its numeric parsing self-check passed. These are logged kinematics, not a reconstructed world-position flight path.

### New movement findings

1. **A steep descent recovered.** VT-7 id -600954 was at radar altitude 220 m, descending 46.8 m/s, with terrain urgency 1.89 at mission time 418.66 (frame 77383; end-log lines 3147–3148). Urgency subsequently reached 5.12. Vertical speed became positive by 420.94; the lowest sampled radar altitude in this segment was 103 m at 422.19, when the aircraft was already climbing 29.2 m/s. It reached 239 m and +57.8 m/s at 425.33. Radar altitude also depends on terrain beneath the moving aircraft. This documents a close terrain recovery, not a terrain collision.

2. **The mode label obscures native terrain avoidance.** The preceding recovery remained labelled Intercept in FormationControl. Current source's ReportCapture chooses TerrainAvoidance when native urgency is positive, but ReportControl keeps its formation mode label. The high-level terrain-abort reflex has a separate 50 m entry threshold in current source. Therefore, absence of a terrain-abort transition at 220 m does not prove absence of avoidance. Smallest improvement: consistently log both high-level behaviour and native safety override, then evaluate recovery margin against descent and terrain ahead before changing thresholds.

3. **Low-speed recovery takes time.** VT-7 id -697602 has 54 below-minimum movement samples between mission times 412.61 and 433.18 in the checkpoint, with airspeed as low as 36.0 m/s against the reported 60 m/s minimum. This is a 20.57-second span between observed low-speed samples, not proof that every intervening frame was below the threshold. Later it reached 209.4 m/s at 522.61. Prioritise VT-7 departure/conversion readiness and acceleration over increasing formation gain.

4. **A takeover changes the reference being tracked.** Frame 78020 records leader-lost/deck-hold. At 78105 the log explicitly records a player copy being spawned and the AI source removed, followed by task resumption. Do not score that aircraft's disappearing track as an AI death, or interpret follower slot-error jumps across the takeover as physical displacement. A later explicit Rejoin request at frame 79950 is a user-command boundary, not an autonomous decision.

5. **Closing is not radial convergence.** `FixedWingFormation` computes the logged closing value as a velocity projection along the leader's horizontal track. Negative values can coexist with decreasing slot distance while geometry or the leader changes. Use actual slot-error evolution within a stable leader/order segment to assess rejoin success; label the projection explicitly in future diagnostics.

6. **Some saturation labels are false positives.** The checkpoint contains 24 rows labelled SATURATED with correction 0/0, around staggered hold after takeover. Current source compares correction >= limit * 0.99, so zero meets the test. Report disabled/zero correction authority separately; only label active limits saturated when the limit is positive. This refines the earlier saturation finding: the label alone is not proof of exhausted useful authority.

For follow-up captures, run `python .nomodkit/monitoring/movement.py <capture-directory>` using nomodkit's existing Python environment. The event file preserves Takeover, Action, Wing and Panic records; do not assign an aircraft ID to name-only events when several aircraft share that name. Split analysis around leader changes, mission starts and command changes even within one process.

### Completed extended window and confirmed losses

Capture endpoints: **22:02:20–22:06:59**, 279.69 seconds between sample timestamps. Saved **694 movement samples, 110 behaviour/action events, and five aircraft-ID tracks**. Of 24 performance samples, 22 were unpaused and two paused. Unpaused instantaneous frame times were 16.650–16.683 ms (median 16.667 ms). Each GC counter rose 154 → 159; managed heap endpoints were 164.98 → 176.89 MiB. BepInEx reported no grouped warnings/errors across 4,472 records; bridge Unity logs again included Discord RPC warnings. The same PID and one deployed-file hash were observed throughout this window. None of this establishes absence of frame hitches or a causal performance bottleneck.

| Aircraft ID | Airframe | Movement samples | Observed slot-error range | Observed forward-airspeed range |
|---|---|---:|---:|---:|
| -588944 | VT-7 Vagrant | 40 | 9,303–13,353 m | 148.9–167.1 m/s |
| -600954 | VT-7 Vagrant | 301 | 1,244–15,111 m | 53.3–191.7 m/s |
| -697602 | VT-7 Vagrant | 263 | 360–14,501 m | 36.0–230.1 m/s |
| -758786 | FS-12 Revoker | 46 | 14,457–18,563 m | 35.5–148.3 m/s |
| -829544 | VT-7 Vagrant | 44 | 10,430–11,553 m | 43.5–83.7 m/s |

These extrema span changing leaders/orders and are not steady-state formation benchmarks. T/A-30 delivery events appeared near the end, without sufficient movement telemetry for an airborne track.

**Highest-priority new sequence: FS-12 defensive exit into unsafe rejoin.** The log records missile-break at frame 84721 and leash-recall/rejoin at 85884. At frame 85897 (mission time 549.91), id -758786 is banked -172 degrees, descending 153.2 m/s, radar altitude 104 m, forward airspeed 58.0 m/s, native terrain urgency 5.93, still labelled Intercept. At 85909 (550.11), altitude is 69 m, bank -170 degrees, descent 143.0 m/s, forward airspeed 35.5 m/s. Terrain-abort is logged at 85919. At 85936, end-log line 4170 explicitly reports **pilot killed**, altitude 4 m, speed 25 m/s, matching id -758786.

This is a confirmed near-ground aircraft loss following the recorded sequence; the log does not establish whether impact, weapon damage, or another cause killed the pilot. The roughly 0.85 seconds from rejoin at frame 85884 to loss at 85936 is estimated from adjacent mission-time/frame samples, not a precise event timestamp. Investigate whether defensive exit should retain safe attitude/energy recovery before a distant slot pursuit, and whether native warning/descent should prevent that handoff. Verify loaded code and native controls before changing it; do not simply raise bank authority or the 50 m threshold without a matched reproduction.

**Second loss:** end-log line 4344, frame 88244, explicitly reports VT-7 id -600954 pilot killed at altitude 0 m, speed 42 m/s, slot error 1,453 m. This is the same aircraft that recovered from the earlier descent, so that earlier recovery is not evidence of lasting safety. Attribute the later loss separately; its cause is not established by the loss message alone.

**Second takeover, not a third recorded AI death:** leader-lost at 88841 is followed by a player copy of a VT-7 and removal of the AI source at 88925. Keep this separate from loss counts. Continue observing the subsequent new VT-7 and T/A-30 departures on future monitoring checks.

## SARH defence follow-up — September 8, after 22:41

The follow-up capture is `.nomodkit/monitoring/20260908-224127-406`; a broader pre-change log is `.nomodkit/sarh-review/before.log`. VT-7 -256494 resumes with slot error 11,594 m at frame 32278, briefly labelled StaggerHold, before Intercept at 32315. Earlier frames 26010–26364 contain repeated missile-break/terrain-abort handoffs for that same aircraft. These observations confirm excessive separation and interrupted evasion; they do not establish the individual missile outcomes. No grouped BepInEx warnings/errors were reported.

Read both requested wiki articles: [How to evade missiles](https://nuclearoption.wiki.gg/wiki/How_to_evade_missiles) and [Semi-Active Radar Homing](https://nuclearoption.wiki.gg/wiki/Category:Semi-Active_Radar_Homing). Their relevant advice is to notch the illuminating radar with ECM, use terrain to break its line of sight, and allow two seconds for SARH tracking loss to become permanent. Fresh decompilation of the installed SARHSeeker confirms the two-second default persistence and its public GetEvasionPoint returning the guiding radar scan position. Native RadarParams uses radial velocity in signal strength. The source text is saved under `.nomodkit/sarh-review/SARHSeeker.txt`.

The bounded fix uses that native evasion point for SARH, removes the outward heading bias, and holds one notch side per tracked threat. When neither side is already established and impact is at least 12 seconds away, formation members prefer the side toward their leader. Already established and urgent notches favour the short turn. ECM and chaff cadence remain unchanged. SARH holds entry altitude with native terrain/collision avoidance instead of commanding the recurring steep descent toward 100 m; terrain emergency recovery still takes priority. This is a deliberate simplification: no speculative terrain-route planner or promise of a maximum separation while under continuous fire.

After warnings clear, defence waits 2.1 seconds (native two-second persistence plus a small margin), reduced from 2.5. Cleared warnings cannot retain an extra minimum hold. Formation re-entry after defence or terrain recovery skips the slot launch stagger, while explicit standing orders and unsafe-flight recovery remain respected. The moving 8 km steering aim is a heading reference, not a required distance to travel before returning.

Validation: **529 tests pass**, including notch geometry, stable side selection, urgent-turn preference and clear-warning lifecycle cases. Release build: **zero warnings/errors**. Static Harmony/reflection verification passes with the previously documented dynamic-map exception. Deployed and restarted at **22:49:11**, PID **15852**; loaded MVID **3a191d39-447a-4680-bde4-9a45885a4f0c** matches the built assembly, SHA-256 **c870f93c564b460b9d82feb52446407c80f6400d1141f55bc8640a9c45d39c10**. Runtime has 25 patched methods and no missing static targets; BepInEx reports no grouped warnings/errors. This build has only been checked through startup, not a matched SARH combat replay. Next monitoring should compare warning duration, peak separation, recovery interruptions and absence of post-defence StaggerHold. Do not label the earlier build's observed handoffs as validation of this newer SARH geometry.

Tooling observation: contrary to the skill's dry-run wording, `nomod deploy --mod wingcommand --json` replaced the on-disk plugin without `--confirm` and reported that the process retained its old assembly. The subsequent confirmed restart loaded the intended new build. Future monitoring must continue distinguishing disk hashes from loaded MVID; `.nomodkit/sarh-review/deployed-before` is explicitly marked as a copy of the new build, not an old-build rollback.

## Implementation and local validation — September 8

The earlier sections preserve the observation baseline. The subsequent implementation applies Ponytail's small shared fixes, with native control signatures and behaviour checked through nomodkit and decompiled game source.

- **Departure:** fixed-wing handoff now requires wind-relative forward airspeed meeting the existing minimum-flight margin. Vertical or sideways motion cannot satisfy readiness. Native AI retains control until ready; rotary departure keeps its separate policy.
- **Terrain and defensive recovery:** native terrain urgency and a five-second descent horizon can trigger recovery before the old altitude floor, including without waiting for Performance-mode decision cadence. After missile defence, unsafe bank, descent or forward airspeed retain recovery before task pursuit. Brief warning gaps recompute flight controls instead of retaining stale roll/pitch or idle throttle. Intentional landing, cargo and native departure retain their task-specific handling.
- **Missile protection:** terrain recovery continues countermeasure and ECM servicing while its own controller flies the escape. Threat-refresh and jammer timing survive defensive/recovery handoffs. The missile all-clear call waits for the warning to stay clear for 2.5 seconds, even if terrain recovery interrupted evasion; flight recovery may continue afterwards.
- **Target coordination:** changing or losing a selection clears its old reservation. Retasking and safety handoffs preserve expiring reservations for shots already fired, avoiding duplicate fire while allowing teammates to reconsider abandoned targets. Candidates whose unpenalised scores cannot win skip capacity calculations and roster scans; scoring weights and firing caps are unchanged.
- **Authority:** control handoffs and missile-warning repair check local simulation and exclude player aircraft.
- **Movement orders:** hover-capable planes use the working fixed-wing autopilot overload and explicit cruise throttle when approaching a landing point. Rotary cargo descent and release require speed below 6 m/s and horizontal error below 30 m, reusing the landing settle rule; drifting aircraft regain the existing settle height. Rotary Move applies the configured altitude once instead of adding it in both the destination and altitude-hold argument. Other waypoint routes and fixed-wing cargo release retain their existing behaviour.
- **Diagnostics:** disabled verbose logging avoids formation-report construction and diagnostic calculations. Neighbour-report scans run on the routine five-second cadence, while control and safety checks retain their cadence. Mode and safety transitions remain visible alongside existing bounded bursts. Reports distinguish formation mode from native terrain avoidance, identify aircraft and leaders, label along-track closing speed, and report zero correction authority as disabled. Startup records the loaded assembly MVID to distinguish builds from subsequently replaced DLL files.

Local validation increased the passing test count from **494 to 524**; the **Release build completed with zero warnings and zero errors**. These checks cover policy regressions and compilation against the installed game APIs. They do not establish improved FPS, fewer collisions or higher survival rates.

Deployed through nomodkit and restarted at **22:32:21**, PID **9100**. The loaded MVID **35e1c9b6-029c-4c32-b1b9-2e5b3f10eb0e** matches the built assembly, and deployment verified SHA-256 **50be6f134152826315fa41f672c61a04972794cfdda54fcbc66ddf02719dc1bb**. Static verification resolved 24/25 patch declarations and all 8 reflection lookups, with no silent skips. The remaining declaration supplies dynamic map targets; runtime verification found all four targets active, 25 patched methods in total, and no static-only targets. Startup reported no grouped BepInEx warnings or errors. The pre-deploy DLL/metadata and log are preserved under `.nomodkit/implementation-baseline/`; original source snapshots are in `C:/Users/marci/dev/nomodkit/scratch/wingcommand-before-ai-20260908` to avoid duplicate static patch discovery.

Post-deploy captures are saved in `.nomodkit/monitoring/20260908-223245-817` (menu through mission entry) and `20260908-223542-595` (live mission, 22:35:42–22:36:07). The latter contains 160 extracted movement samples across three aircraft IDs and 32 behaviour events. The saved start log records VT-7 **-251578** entering missile-break at frame 9714, terrain recovery at 10049, then deck-hold at 10231 (lines 77–83); **-256494** follows the same sequence at frames 10196, 10540 and 10729 (lines 80–88). This confirms the new handoff path runs and relinquishes recovery during actual play. BepInEx reported no grouped warnings/errors. The initial bridge buffer separately reported weapon-mount/dropdown warnings, disabled audio, buffer resizing and Discord RPC warnings; these are not proof of Wing Command regressions and remain preserved for attribution.

Matched live comparison remains open. The initial VT-7 transitions do not quantify survival gains, countermeasure effectiveness or frame-time improvement, and the specific FS-12 loss case has not been replayed. Check landing approaches, cargo centring and rotary move altitude in flight. Compare unpaused frame-time distributions, recovery margins, target distribution and log volume under the same mission, wing and verbosity settings; preserve loaded MVID and command/leader boundaries. The nomodkit measurement blind spots recorded above remain open unless separately verified as fixed. The existing five-minute read-only monitor remains active and reads this updated implementation baseline.
