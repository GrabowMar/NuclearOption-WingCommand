# Experimental pilot portraits

`layers.png` is the only shipped portrait asset: a 512 x 960 RGBA atlas (about 270 KiB),
embedded in WingCommand.dll. Its registered 128 x 192 layers contain the six original
faces and separate male/female pools of four hairstyles and two uniforms each.
Bald is the absence of a hair layer. Tiles 0–2 are male faces, 3–5 female faces,
6–9 male hair, 10–13 female hair, 14–15 male uniforms and 16–17 female uniforms.
Tiles 18–19 are unused. The selected face determines the matching hair/uniform pool.
The compositor adds a cool background without baked scanlines, then caches the finished sprite
for the mission. No runtime image generation, external assets, shader, or new dependency.

Scanlines are omitted to keep facial features clear when the UI scales. The Supply picker
zooms the portrait by 20% and clips side margins and headroom into a square headshot;
the Wing dossier retains the full portrait.
The runtime loader extracts explicit RGBA channels via `GetPixels32`, since Unity's
PNG decoder can change the texture's storage format to ARGB32.

`source.png` was created with the built-in imagegen tool using `prompt.txt`.
The matching [hair and uniform source](gender-parts.png) was generated with the same
built-in tool using [this prompt](gender-parts-prompt.txt), with the original sheet as
the style and fit reference. Its green matte is keyed to alpha offline before packing.
The user's Ace Combat examples informed the style; these are original fictional faces.
The source sheet, prompt, scripts and preview are development assets, not embedded resources.

To repack the cutouts and render 32 combinations using the actual C# compositor on Windows:

```powershell
pwsh -File Assets/Pilots/pack.ps1
pwsh -File Assets/Pilots/preview.ps1
pwsh -File Assets/Pilots/preview.ps1 -All
```

The sheet's cutouts are registered once by `pack.ps1`, so runtime composition just blends
three same-size tiles. Keep the atlas RGBA, including transparent space, and preserve tile order.
Portraits are seeded from `Name|Callsign`; rank, XP, selection and flight status never change
the face. Imported pilots use the same path. Renaming an identity changes its portrait.
The empty picker uses the stable `WingCommand` seed.
The `-All` preview renders every one of the 60 legal face/hair/uniform combinations to
`compatibility.png`, including bald. Packing checks that hair leaves the eye opening clear
and uniforms leave an open neckline. The original six face illustrations are retained.

Uniforms are cropped to the upper chest with near-uniform scaling; squeezing the full
torso into the same area distorted the shoulders and made the male jackets look fitted.
The pixie and silver crop sit higher to clear both eyes. Packing rejects distorted
uniform scales and hair overlapping the entire eye region, not just its center.
The compositor skips fully transparent pixels and directly copies opaque pixels;
only antialiased edges need blending. Atlas dimensions and per-portrait memory are unchanged.

Male faces and wigs use a coordinated smaller scale, with broader uniforms cropped at
the portrait edges and collars raised to meet the neck. Packing clips each layer to its
own tile and checks the combined neck/shirt opacity for all six male face/uniform pairs.
Female registration is unchanged by this adjustment.

Male heads sit six pixels higher to separate the jaw from the collar. Only the
lower neck is extended to maintain the shirt join; the face keeps its proportions.
Male wigs are raised with the heads and narrowed to fit the temples. Eye-clearance
checks follow the raised male eye line, and neck-seam checks still cover both uniforms.

![Compositor preview](preview.png)
