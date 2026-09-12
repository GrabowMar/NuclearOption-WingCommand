# New dossier art: one body profile, no per-sprite fitting or corrective masks.
[CmdletBinding()]
param([string]$OutputFile = "$PSScriptRoot/layers.png")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies @([Drawing.Bitmap].Assembly.Location,[Drawing.Rectangle].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
public static class PortraitMatte
{
    // Unmix the deliberately flat magenta production matte BEFORE resampling.
    // This retains soft alpha edges without pink halos or a baked checkerboard.
    public static void Remove(Bitmap image)
    {
        for (int y=0; y<image.Height; y++) for (int x=0; x<image.Width; x++)
        {
            Color c=image.GetPixel(x,y);
            int matte=Math.Max(0,Math.Min(c.R,c.B)-c.G);
            if (matte>120) { image.SetPixel(x,y,Color.Transparent); continue; }
            int a=255-matte;
            if (a<12) { image.SetPixel(x,y,Color.Transparent); continue; }
            int r=Math.Max(0,Math.Min(255,(c.R-matte)*255/a));
            int g=Math.Max(0,Math.Min(255,c.G*255/a));
            int b=Math.Max(0,Math.Min(255,(c.B-matte)*255/a));
            image.SetPixel(x,y,Color.FromArgb(c.A*a/255,r,g,b));
        }
        // Edge RGB is unreliable after unmixing a blurred production matte.
        // Borrow the nearest opaque foreground colour, keeping the soft alpha.
        Color[] clean=new Color[image.Width*image.Height];
        for (int y=0; y<image.Height; y++) for (int x=0; x<image.Width; x++)
            clean[y*image.Width+x]=image.GetPixel(x,y);
        for (int y=0; y<image.Height; y++) for (int x=0; x<image.Width; x++)
        {
            Color c=clean[y*image.Width+x];
            if (c.A==0 || c.A>=254) continue;
            int distance=26;
            Color nearest=c;
            for (int dy=-4; dy<=4; dy++) for (int dx=-4; dx<=4; dx++)
            {
                int d=dx*dx+dy*dy, xx=x+dx, yy=y+dy;
                if (d>=distance || xx<0 || yy<0 || xx>=image.Width || yy>=image.Height) continue;
                Color candidate=clean[yy*image.Width+xx];
                if (candidate.A>=254) { nearest=candidate; distance=d; }
            }
            // Thin wisps may have no opaque neighbour within the search radius.
            int spill=Math.Max(0,Math.Min(nearest.R,nearest.B)-nearest.G);
            image.SetPixel(x,y,Color.FromArgb(c.A,nearest.R-spill,nearest.G,nearest.B-spill));
        }
    }
    public static int Top(Bitmap image, Rectangle cell)
    {
        for (int y=cell.Top; y<cell.Bottom; y++) for (int x=cell.Left; x<cell.Right; x++)
            if (image.GetPixel(x,y).A>192) return y;
        throw new InvalidOperationException("Empty sprite cell: "+cell);
    }
}
'@

# All six pieces of each kind share one isotropic pixel scale per body.
# The uniform sheets have a different source resolution, also shared by all four.
# Top alignment removes sheet padding only; it never stretches the art.
$profiles=@(
    @{ Name='male'; HeadScale=0.80; FaceTop=21; HairScale=0.76; HairTop=12; UniformScale=1.50; UniformTop=106 },
    @{ Name='female'; HeadScale=0.80; FaceTop=18; HairScale=0.78; HairTop=9; UniformScale=1.45; UniformTop=106 }
)
$atlas=New-Object Drawing.Bitmap 512,1536,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g=[Drawing.Graphics]::FromImage($atlas)
try {
    $g.Clear([Drawing.Color]::Transparent)
    $g.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
    $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    for ($body=0; $body -lt 2; $body++) {
        $profile=$profiles[$body]
        foreach ($kind in 'faces','hair','uniforms') {
            $source=[Drawing.Bitmap]::FromFile("$PSScriptRoot/Source/$($profile.Name)-$kind.png")
            $sheet=$source.Clone((New-Object Drawing.Rectangle 0,0,$source.Width,$source.Height),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $source.Dispose()
            try {
                [PortraitMatte]::Remove($sheet)
                $columns=if ($kind -eq 'uniforms') { 2 } else { 3 }
                $count=$columns*2
                $start=switch ($kind) { 'faces' { $body*6 }; 'hair' { 12+$body*6 }; 'uniforms' { 24+$body*4 } }
                $top=switch ($kind) { 'faces' { $profile.FaceTop }; 'hair' { $profile.HairTop }; 'uniforms' { $profile.UniformTop } }
                # Crop the bust at the portrait edges; fitting the entire source
                # width made shoulders and the transparent neck opening too small.
                $factor=switch ($kind) { 'faces' { $profile.HeadScale }; 'hair' { $profile.HairScale }; 'uniforms' { $profile.UniformScale } }
                $scale=128.0*$columns/$sheet.Width*$factor
                for ($i=0; $i -lt $count; $i++) {
                    $x=[int][Math]::Floor(($i%$columns)*$sheet.Width/$columns)
                    $right=[int][Math]::Floor((($i%$columns)+1)*$sheet.Width/$columns)
                    $y=[int][Math]::Floor([Math]::Floor($i/$columns)*$sheet.Height/2)
                    $cell=New-Object Drawing.Rectangle $x,$y,($right-$x),([int]($sheet.Height/2))
                    $visibleTop=[PortraitMatte]::Top($sheet,$cell)
                    $tile=$start+$i
                    $left=($tile%4)*128
                    $row=[int][Math]::Floor($tile/4)*192
                    $g.SetClip((New-Object Drawing.Rectangle $left,$row,128,192))
                    if ($kind -eq 'faces') {
                        # Keep the natural neck, exclude the shoulder stubs that
                        # belong underneath the independently drawn uniform.
                        $neck=New-Object Drawing.Drawing2D.GraphicsPath
                        [Drawing.Point[]]$points=foreach ($p in @(@(0,0),@(128,0),@(128,112),@(90,112),@(86,125),@(86,192),@(42,192),@(42,125),@(38,112),@(0,112))) {
                            New-Object Drawing.Point ($left+$p[0]),($row+$p[1])
                        }
                        $neck.AddPolygon($points)
                        $g.SetClip($neck,[Drawing.Drawing2D.CombineMode]::Intersect)
                        $neck.Dispose()
                    }
                    $dest=New-Object Drawing.RectangleF ([single]($left+(128-$cell.Width*$scale)/2)),([single]($row+$top-($visibleTop-$cell.Y)*$scale)),([single]($cell.Width*$scale)),([single]($cell.Height*$scale))
                    # Resample an isolated cell: GDI otherwise samples the next
                    # sprite across grid edges and draws lines through faces.
                    $sprite=$sheet.Clone($cell,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    $src=New-Object Drawing.RectangleF 0,0,([single]$cell.Width),([single]$cell.Height)
                    try { $g.DrawImage($sprite,$dest,$src,[Drawing.GraphicsUnit]::Pixel) }
                    finally { $sprite.Dispose() }
                    $g.ResetClip()
                }
            }
            finally { $sheet.Dispose() }
        }
    }
    $atlas.Save($OutputFile,[Drawing.Imaging.ImageFormat]::Png)
    # Also deliver independent, already-registered transparent layer sets.
    for ($body=0; $body -lt 2; $body++) {
        foreach ($kind in 'faces','hair','uniforms') {
            $columns=if ($kind -eq 'uniforms') { 2 } else { 3 }
            $start=switch ($kind) { 'faces' { $body*6 }; 'hair' { 12+$body*6 }; 'uniforms' { 24+$body*4 } }
            $set=New-Object Drawing.Bitmap ($columns*128),384,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $draw=[Drawing.Graphics]::FromImage($set)
            try {
                for ($i=0; $i -lt $columns*2; $i++) {
                    $tile=$start+$i
                    $src=New-Object Drawing.Rectangle (($tile%4)*128),([int][Math]::Floor($tile/4)*192),128,192
                    $sprite=$atlas.Clone($src,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    try { $draw.DrawImageUnscaled($sprite,(($i%$columns)*128),([int][Math]::Floor($i/$columns)*192)) }
                    finally { $sprite.Dispose() }
                }
                $set.Save("$PSScriptRoot/$($profiles[$body].Name)-$kind.png",[Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $draw.Dispose(); $set.Dispose() }
        }
    }
    Write-Host "Packed 32 new dossier layers: $OutputFile"
}
finally { $g.Dispose(); $atlas.Dispose() }


