# Repack the original generated sheet into registered 128 x 192 paper-doll layers.
param([string]$SourceFile = "$PSScriptRoot/source.png")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::FromFile($SourceFile)
$partsInput = [Drawing.Bitmap]::FromFile("$PSScriptRoot/gender-parts.png")
$parts = New-Object Drawing.Bitmap $partsInput.Width,$partsInput.Height
$copy = [Drawing.Graphics]::FromImage($parts)
$copy.DrawImageUnscaled($partsInput,0,0)
$copy.Dispose()
$partsInput.Dispose()
$atlas = New-Object Drawing.Bitmap 512,960
$graphics = [Drawing.Graphics]::FromImage($atlas)
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy

$pieces = @(
    # Male heads and wigs share a smaller registration, leaving room for adult shoulders.
    @(64,48,200,307, 32,24,68,106),
    @(368,48,210,307, 30,24,72,106),
    @(677,48,211,307, 30,24,72,106),
    @(995,48,200,307, 28,24,76,118),
    @(63,377,208,306, 26,24,80,118),
    @(367,377,209,306, 26,24,80,118)

)
$genderPieces = @(
    @(43,48,282,230, 29,18,74,61),
    @(407,53,273,228, 30,19,72,60),
    @(756,45,282,230, 29,18,74,61),
    @(1108,44,309,237, 27,18,78,59),
    @(43,373,278,262, 22,14,88,78),
    @(386,389,315,298, 17,18,98,93),
    @(770,382,273,299, 22,18,86,94),
    @(1129,375,277,258, 22,14,88,78),
    # Crop at the chest instead of squashing the entire torso into the portrait.
    # Near-uniform scaling preserves shoulder, lapel and chest proportions.
    @(5,728,355,232, -11,94,150,98),
    @(365,729,358,234, -11,94,150,98),
    @(728,735,354,238, 3,110,122,82),
    @(1090,741,352,237, 3,110,122,82)
)
try {
    # Key the generated matte before resizing; promote RGB to RGBA above to retain alpha.
    for ($y = 0; $y -lt $parts.Height; $y++) {
        for ($x = 0; $x -lt $parts.Width; $x++) {
            $c = $parts.GetPixel($x,$y)
            $other = [Math]::Max($c.R,$c.B)
            $excess = $c.G - $other
            if ($excess -gt 30) {
                $alpha = 255 - [Math]::Min(255,[int]($excess * 255 / 150))
                $parts.SetPixel($x,$y,[Drawing.Color]::FromArgb($alpha,$c.R,$other,$c.B))
            }
        }
    }
    for ($i = 0; $i -lt $pieces.Count; $i++) {
        $v = $pieces[$i]
        $dest = New-Object Drawing.Rectangle (($i % 4) * 128 + $v[4]),([int][Math]::Floor($i / 4) * 192 + $v[5]),$v[6],$v[7]
        if ($i -lt 3) {
            # Raise the jaw above the collar, retaining neck coverage down to the shirt.
            $dest.Height = 90
            $graphics.DrawImage($source, $dest, $v[0], $v[1], $v[2], 260, [Drawing.GraphicsUnit]::Pixel)
            $dest.Y += 90
            $dest.Height = 28
            $graphics.DrawImage($source, $dest, $v[0], ($v[1]+260), $v[2], ($v[3]-260), [Drawing.GraphicsUnit]::Pixel)
        } else {
            $graphics.DrawImage($source, $dest, $v[0], $v[1], $v[2], $v[3], [Drawing.GraphicsUnit]::Pixel)
        }
    }
    for ($i = 0; $i -lt $genderPieces.Count; $i++) {
        $v = $genderPieces[$i]
        $tile = 6 + $i
        if ($tile -ge 14 -and [Math]::Abs(($v[6]/$v[2])/($v[7]/$v[3])-1) -gt 0.02) {
            throw "Distorted uniform proportions in tile $tile"
        }
        $dest = New-Object Drawing.Rectangle (($tile % 4) * 128 + $v[4]),([int][Math]::Floor($tile / 4) * 192 + $v[5]),$v[6],$v[7]
        # Broad shoulders extend past the portrait, but must never bleed into adjacent tiles.
        $graphics.SetClip((New-Object Drawing.Rectangle (($tile%4)*128),([int][Math]::Floor($tile/4)*192),128,192))
        $graphics.DrawImage($parts, $dest, $v[0], $v[1], $v[2], $v[3], [Drawing.GraphicsUnit]::Pixel)
        $graphics.ResetClip()
    }
    # Check both eyes, not just the bridge of the nose (a fringe can miss the center).
    for ($tile = 6; $tile -lt 14; $tile++) {
        $eyeTop = if ($tile -lt 10) { 66 } else { 72 }
        for ($y = $eyeTop; $y -le $eyeTop+7; $y++) {
            for ($x = 45; $x -le 85; $x++) {
                if ($atlas.GetPixel(($tile%4*128+$x),([int][Math]::Floor($tile/4)*192+$y)).A -gt 8) {
                    throw "Hair overlaps eye area in tile $tile at $x,$y"
                }
            }
        }
    }
    # Every wig must leave the eyes transparent, and every uniform an open neckline.
    for ($tile = 6; $tile -lt 18; $tile++) {
        $py = if ($tile -lt 14) { 76 } else { 122 }
        $px = ($tile % 4)*128 + 64
        if ($atlas.GetPixel($px,([int][Math]::Floor($tile/4)*192+$py)).A -ne 0) { throw "Blocked opening in tile $tile" }
    }
    # Validate actual paired artwork: no background seam between neck and shirt.
    for ($face = 0; $face -lt 3; $face++) {
        $firstUniform = 14
        for ($uniform = $firstUniform; $uniform -lt $firstUniform+2; $uniform++) {
            for ($y = 118; $y -le 148; $y++) {
                for ($x = 58; $x -le 72; $x++) {
                    $a = $atlas.GetPixel(($face%4*128+$x),([int][Math]::Floor($face/4)*192+$y)).A
                    $b = $atlas.GetPixel(($uniform%4*128+$x),([int][Math]::Floor($uniform/4)*192+$y)).A
                    if ((255-$a)*(255-$b)/255 -gt 8) { throw "Neck seam: face $face, uniform $uniform at $x,$y" }
                }
            }
        }
    }
    $atlas.Save("$PSScriptRoot/layers.png", [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $atlas.Dispose()
    $source.Dispose()
    $parts.Dispose()
}

