# Render the dossier compositor outside Unity for visual QA.
# -All renders all 336 face/hair/faction-uniform combinations.
[CmdletBinding()]
param([switch]$All)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$generatorFile = Join-Path $PSScriptRoot '../../Pure/Pilots/PilotPortraitGenerator.cs'
$generator = [IO.File]::ReadAllText((Resolve-Path $generatorFile))
Add-Type -TypeDefinition ($generator + @'
public static class PilotPreview
{
    public static byte[] Compose(int body, int face, int hair, int uniform, int accessory, int backdrop, byte[] atlas)
    {
        return WingCommand.PilotPortraitGenerator.Compose(
            new WingCommand.PortraitSelection((WingCommand.PortraitBody)body, face, hair, uniform, accessory, backdrop), atlas);
    }
    public static byte[] ComposeIdentity(string identity, byte[] atlas)
    {
        return WingCommand.PilotPortraitGenerator.Compose(identity, atlas);
    }
}
'@)

$atlas = [Drawing.Bitmap]::FromFile("$PSScriptRoot/layers.png")
$records = New-Object System.Collections.Generic.List[object]
if ($All) {
    # Every face and hairstyle is compatible with both factions.
    for ($body = 0; $body -lt 2; $body++) {
        $initial = if ($body -eq 0) { 'M' } else { 'F' }
        for ($face = 0; $face -lt 6; $face++) {
            for ($hair = 0; $hair -lt 7; $hair++) {
                for ($uniform = 0; $uniform -lt 4; $uniform++) {
                    $records.Add([pscustomobject]@{ Body=$body; Face=$face; Hair=$hair; Uniform=$uniform; Accessory=0; Backdrop=0; Label="$initial F$face H$hair U$uniform" })
                }
            }
        }
    }
}
else {
    $records.Add([pscustomobject]@{ Identity='WingCommand'; Label='DEFAULT' })
    for ($i = 1; $i -lt 32; $i++) {
        $records.Add([pscustomobject]@{ Identity="PILOT $i"; Label="PILOT $i" })
    }
}

$columns = if ($All) { 10 } else { 8 }
$tileWidth = 148
$tileHeight = 230
$sheet = New-Object Drawing.Bitmap ($columns * $tileWidth), ([int][Math]::Ceiling($records.Count / $columns) * $tileHeight)
$graphics = [Drawing.Graphics]::FromImage($sheet)
$font = New-Object Drawing.Font 'Consolas', 8

try {
    $layers = New-Object byte[] ($atlas.Width * $atlas.Height * 4)
    for ($y = 0; $y -lt $atlas.Height; $y++) {
        for ($x = 0; $x -lt $atlas.Width; $x++) {
            $c = $atlas.GetPixel($x, $atlas.Height - 1 - $y)
            $p = ($y * $atlas.Width + $x) * 4
            $layers[$p] = $c.R; $layers[$p + 1] = $c.G; $layers[$p + 2] = $c.B; $layers[$p + 3] = $c.A
        }
    }
    $graphics.Clear([Drawing.Color]::FromArgb(15,24,32))
    for ($i = 0; $i -lt $records.Count; $i++) {
        $record = $records[$i]
        if ($All) {
            $pixels = [PilotPreview]::Compose($record.Body, $record.Face, $record.Hair, $record.Uniform, $record.Accessory, $record.Backdrop, $layers)
        }
        else {
            $pixels = [PilotPreview]::ComposeIdentity($record.Identity, $layers)
        }
        $portrait = New-Object Drawing.Bitmap 128,192
        try {
            for ($y = 0; $y -lt 192; $y++) {
                for ($x = 0; $x -lt 128; $x++) {
                    $p = ((191 - $y) * 128 + $x) * 4
                    $portrait.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, $pixels[$p], $pixels[$p + 1], $pixels[$p + 2]))
                }
            }
            $left = $i % $columns * $tileWidth + 10
            $top = [int][Math]::Floor($i / $columns) * $tileHeight + 10
            $graphics.DrawImageUnscaled($portrait, $left, $top)
            $graphics.DrawString($record.Label, $font, [Drawing.Brushes]::LightSteelBlue, $left, ($top + 198))
        }
        finally { $portrait.Dispose() }
    }
    $output = if ($All) { 'compatibility.png' } else { 'preview.png' }
    $sheet.Save("$PSScriptRoot/$output", [Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Rendered $($records.Count) portrait checks to $PSScriptRoot/$output"
}
finally {
    $font.Dispose()
    $graphics.Dispose()
    $sheet.Dispose()
    $atlas.Dispose()
}
