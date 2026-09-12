# Check the shipped atlas, rather than synthetic pixels, for paper-doll fit regressions.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$atlas = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot '../Assets/Pilots/layers.png'))
function Pixel([int]$tile, [int]$x, [int]$y) {
    $atlas.GetPixel(($tile % 4 * 128 + $x), ([int][Math]::Floor($tile / 4) * 192 + $y))
}
try {
    if ($atlas.Width -ne 512 -or $atlas.Height -ne 1536) { throw 'Expected 32 layers with no equipment tiles.' }
    for ($body = 0; $body -lt 2; $body++) {
        for ($face = 0; $face -lt 6; $face++) {
            # The source bust flares must not emerge as skin tabs above collars.
            for ($y = 120; $y -lt 150; $y++) {
                foreach ($x in 32,33,34,94,95,96) {
                    if ((Pixel ($body * 6 + $face) $x $y).A -gt 32) {
                        throw "Source bust protrudes outside the neck: body $body face $face at $x,$y."
                    }
                }
            }
            for ($uniform = 0; $uniform -lt 4; $uniform++) {
                for ($y = 105; $y -lt 192; $y++) {
                    for ($x = 48; $x -le 80; $x++) {
                        $skin = (Pixel ($body * 6 + $face) $x $y).A
                        $cloth = (Pixel (24 + $body * 4 + $uniform) $x $y).A
                        $coverage = $skin + $cloth * (255 - $skin) / 255
                        if ($coverage -lt 240) { throw "Neck/chest gap: body $body face $face uniform $uniform at $x,$y." }
                    }
                }
            }
        }
        for ($uniform = 0; $uniform -lt 4; $uniform++) {
            # A head-and-shoulders crop must fill the frame below the shoulders;
            # fitting a whole bust inside the cell makes the clothing too small.
            for ($y = 155; $y -lt 192; $y++) {
                foreach ($x in 0,127) {
                    if ((Pixel (24 + $body * 4 + $uniform) $x $y).A -lt 240) {
                        throw "Undersized shoulders: uniform $body/$uniform at $x,$y."
                    }
                }
            }
            if ((Pixel (24 + $body * 4 + $uniform) 64 112).A -gt 10) {
                throw "Uniform $body/$uniform covers the neck with an opaque collar interior."
            }
            # Collar tips begin at 106, with a one-pixel filtering fringe.
            for ($y = 0; $y -lt 104; $y++) {
                for ($x = 0; $x -lt 128; $x++) {
                    if ((Pixel (24 + $body * 4 + $uniform) $x $y).A -gt 8) {
                        throw "Uniform resampling bled across a grid edge at $body/$uniform $x,$y."
                    }
                }
            }
        }
    }
    for ($hair = 12; $hair -lt 18; $hair++) {
        for ($y = 0; $y -lt 192; $y++) {
            for ($x = 0; $x -lt 128; $x++) {
                if ((Pixel $hair $x $y).A -gt 32 -and ($x -lt 15 -or $x -gt 112 -or $y -gt 92)) {
                    throw "Oversized male hair tile $hair at $x,$y."
                }
            }
        }
    }
    for ($y = 0; $y -lt $atlas.Height; $y++) {
        for ($x = 0; $x -lt $atlas.Width; $x++) {
            $c=$atlas.GetPixel($x,$y)
            if ($c.A -gt 32 -and $c.R -gt $c.G+20 -and $c.B -gt $c.G+20) {
                throw "Magenta production matte remains at $x,$y."
            }
        }
    }
    Write-Host 'Portrait registration passed: 48 neck fits, hair bounds, clean matte and no grid-edge bleed.'
}
finally { $atlas.Dispose() }

