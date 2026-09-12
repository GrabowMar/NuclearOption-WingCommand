Flight tuning experiment — 2026-09-12

Run from the repository root:
  pwsh -NoProfile -File tests/FlightTuning/Run.ps1

Requires .NET 8 and PowerShell 7. No additional packages. The runner copies current
production guidance into build/flight-tuning and makes only that copy's tuning
fields mutable. It never modifies production code. CSV output is written there.
The adjacent horizontal.csv and vertical.csv record the selection run.

Selected values (WingTuning.cs is the production source of truth):
  bank authority rise: 75 degrees/second, previously 45
  pursuit/intercept bank ceiling: 70 degrees, previously 75
  preview: 3 seconds, minimum 450 metres, previously minimum 550
  rejoin heading limit: 55 degrees, previously 65
  rejoin pitch: +25/-22 degrees, retained
  station pitch: +7/-6 degrees, retained
  station bank/heading: 45/40 degrees, retained

Method
  Two coordinate-search passes over five horizontal parameters, four values per
  parameter: 40 evaluations, including repeated configurations. Each runs 54
  scenarios with speeds 68/120/220 m/s, roll lags 0.4/0.8/1.2 s, three rejoin
  geometries, and straight or sinusoidally turning leaders.
  Separate validation/sensitivity scenarios use speeds 85/175 m/s, roll lags
  0.6/1.5 s, three different geometries and a different turn period/amplitude.
  Each horizontal run lasts 600 s at 0.1 s steps. Production intercept, closure,
  recovery, steering, bank envelope and heading-limiter functions are exercised.
  Objective: average first capture time + 0.5*mean final error + 0.1*worst final
  error + 0.1*mean tracking error. Capture means first entry within 300 m;
  final error is maximum error over the final 30 seconds, penalising oscillation.
  Tracking error is averaged from 100 to 500 seconds, including straight cases.

  Vertical experiment: 36 preview/pitch combinations, each with 18 cases varying
  speed, response lag and initial +/-300 m height offset. Climb/descent steps
  demand +/-15 m/s. Each run lasts 180 s at 0.1 s steps, using production vertical
  correction, aim-rise and pitch-limiter functions. Acceleration is bounded to
  +/-6 m/s^2. Compare mean absolute altitude error and worst final error.

Results (surrogate model, not measured gameplay)
  Horizontal validation average first capture:
    Before initial agility edit: 35.4 s
    Previous agility edit:       31.7 s
    Selected:                    31.6 s
    Horizontal-only winner:     31.3 s
  The initial edit's original intercept ceiling is explicitly restored to 60
  for the historical baseline; its station/pursuit ceilings are 40/58.

  Vertical mean absolute altitude error with retained +25/-22 pitch and 3 s preview:
    Previous 550 m minimum: 35.82 m
    Selected 450 m minimum: 34.58 m (3.46% lower)
    Worst final error: 1.70 m -> 0.95 m

Why this compromise
  The horizontal-only winner uses 3.5 s / 750 m preview and 75-degree bank.
  Its capture advantage is only about 0.3 seconds over the selected set.
  A shorter minimum preview improves vertical correction responsiveness.
  70-degree bank keeps more margin from vertical banking than 75: the inspected
  local native AutoAim reference can multiply level-flight bank allowance by 1.2.
  The experiment itself caps its coordinated-turn plant at 85 degrees; candidates
  that hit this cap get an optimistic result and are not grounds for raising bank.
  55-degree heading retains the old limit because 65 showed no useful advantage.
  Steeper +30/-25 pitch scored better in the vertical-only plant, but it lacks
  coupled energy loss and stall physics, so that increase was deliberately rejected.

Limits
  This is a reduced-order deterministic model, not Unity or the full native
  autopilot. Roll/pitch/engine lags are assumptions swept for sensitivity, not
  calibrated per-airframe measurements. No complete aerodynamic energy model,
  actuator controller, wind, terrain mesh, collision geometry, moving banked slot,
  weapons or multiplayer simulation is included. It cannot establish a global
  optimum, prove real-world stall margins, or predict literal sub-metre gameplay
  accuracy. Validation cases also informed the compromise; they are not a final
  untouched statistical holdout. Native reference was local decompiled source,
  not a fresh verification of the currently installed game version.
  This exercise tunes formation/rejoin, not the separate combat sharp-turn patch.
  Flight safety branches and native filtering are unchanged. Existing automated
  safety tests remain necessary; next evidence should be in-game telemetry across
  several airframes during roll reversals, climbing turns and low-speed rejoins.

Verification
  All 718 PureTests passed after applying the selected tuning and replacing the
  old hardcoded preview/heading/pitch values in the rendezvous regression test
  with production constants. Release build passed with zero warnings/errors.
