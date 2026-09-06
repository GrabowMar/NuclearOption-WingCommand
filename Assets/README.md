# Wing icons

Transparent white PNGs are embedded in WingCommand.dll and tinted by each control.
The existing glyphs were baked once at 96 × 96; there is no runtime drawing code.
Replace a file and rebuild to change its artwork. Keep filenames: callers use the
stem as the icon key, including `shape_<FormationShape>` for formation choices.

PNG works with Unity's built-in ImageConversion.LoadImage and needs no SVG renderer.
Use square, transparent RGBA artwork; 192 × 192 is suitable for higher-resolution
replacements. The cache retains this fixed set of assets across missions.

# Manoeuvre presets

Copy `maneuvers.json` into `BepInEx/config/WingCommand/maneuvers.json` to override
all five aerobatic presets. Changes load at the next mission; remove the override
to restore defaults. Invalid data logs a warning and falls back to embedded presets.
Tactical breaks, notch, terrain mask and waggle continue to use the native autopilot.

Each recipe has one to eight sequential phases. Controls are throttle (0–1), pitch
and roll (−1–1). Roll adds `bankError * bankGain - rollRate * rollDamping`, then clamps
to `rollLimit`. Pitch subtracts `noseY * pitchLevelGain`, then clamps to
`minPitch`/`maxPitch`. Bank targets are degrees; roll rate is radians/second.

`until` selects completion: `pitch` accumulates positive pitch rotation in degrees;
`roll` accumulates absolute roll rotation; `seconds` waits for `amount` seconds;
`bank` waits for the target bank and low roll rate; `recover` additionally requires
a near-level nose. Arc progress resets at each phase. Every recipe must end with
`recover` targeting zero bank. All fields shown in the presets should be retained.

Entry altitude/speed limits, fixed-wing eligibility, the hard deck, stall recovery,
and the 18-second overall timeout are enforced in code and cannot be disabled by
JSON. The simplified presets require flight testing on supported aircraft; they do
not reproduce every control ramp of the former per-manoeuvre implementation.
