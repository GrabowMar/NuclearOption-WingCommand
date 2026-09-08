# Preview the engine-free portrait compositor in PowerShell 7 on Windows.
param([switch]$All)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$generator = [IO.File]::ReadAllText("$PSScriptRoot/../../Pure/PilotPortraitGenerator.cs")
Add-Type -TypeDefinition ($generator + @'
public static class PilotPreview
{
    public static byte[] Compose(string identity, byte[] atlas)
    { return WingCommand.PilotPortraitGenerator.Compose(identity, atlas); }
    public static string Find(int face, int hair, int uniform)
    {
        for (int i = 0; i < 20000; i++)
        {
            string identity = "PREVIEW " + i;
            var parts = WingCommand.PilotPortraitGenerator.Select(identity);
            if (parts.Face == face && parts.Hair == (hair == 4 ? -1 : (face < 3 ? 6 : 10) + hair)
                && parts.Uniform == (face < 3 ? 14 : 16) + uniform) return identity;
        }
        throw new System.InvalidOperationException("Unreachable portrait combination.");
    }
}
'@)
$atlas = [Drawing.Bitmap]::FromFile("$PSScriptRoot/layers.png")
$columns = if ($All) { 10 } else { 8 }
$count = if ($All) { 60 } else { 32 }
$sheet = New-Object Drawing.Bitmap ($columns*148),([int][Math]::Ceiling($count/$columns)*230)
$graphics = [Drawing.Graphics]::FromImage($sheet)
$font = New-Object Drawing.Font 'Consolas',9
try {
    $layers = New-Object byte[] ($atlas.Width * $atlas.Height * 4)
    for ($y = 0; $y -lt $atlas.Height; $y++) {
        for ($x = 0; $x -lt $atlas.Width; $x++) {
            $c = $atlas.GetPixel($x, $atlas.Height - 1 - $y)
            $p = ($y * $atlas.Width + $x) * 4
            $layers[$p] = $c.R; $layers[$p+1] = $c.G; $layers[$p+2] = $c.B; $layers[$p+3] = $c.A
        }
    }
    $graphics.Clear([Drawing.Color]::FromArgb(15,24,32))
    for ($i = 0; $i -lt $count; $i++) {
        $identity = if ($All) { [PilotPreview]::Find([int][Math]::Floor($i/10),($i%5),([int][Math]::Floor(($i%10)/5))) }
                    elseif ($i -eq 0) { 'WingCommand' } else { "PILOT $i" }

        $pixels = [PilotPreview]::Compose($identity, $layers)
        $portrait = New-Object Drawing.Bitmap 128,192
        try {
            for ($y = 0; $y -lt 192; $y++) {
                for ($x = 0; $x -lt 128; $x++) {
                    $p = ((191 - $y) * 128 + $x) * 4
                    $portrait.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,$pixels[$p],$pixels[$p+1],$pixels[$p+2]))
                }
            }
            $left = $i % $columns * 148 + 10
            $top = [int][Math]::Floor($i / $columns) * 230 + 10
            $graphics.DrawImageUnscaled($portrait,$left,$top)
            $label = if ($All) { "F$([int][Math]::Floor($i/10)) H$($i%5) U$([int][Math]::Floor(($i%10)/5))" }
                     elseif ($i -eq 0) { 'DEFAULT' } else { $identity }
            $graphics.DrawString($label,$font,[Drawing.Brushes]::LightSteelBlue,$left,($top+198))
        } finally { $portrait.Dispose() }
    }
    $output = if ($All) { 'compatibility.png' } else { 'preview.png' }
    $sheet.Save("$PSScriptRoot/$output",[Drawing.Imaging.ImageFormat]::Png)
} finally {
    $font.Dispose(); $graphics.Dispose(); $sheet.Dispose(); $atlas.Dispose()
}
