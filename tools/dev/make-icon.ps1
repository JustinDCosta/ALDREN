<#
.SYNOPSIS
    Draws ALDREN's tray and window icon (a cyan ring on a near-black disc) into src\Aldren.App\Assets\aldren.ico.
    The .ico holds PNG frames at 16, 20, 24, 32, 40, 48, 64 and 256 px.
#>
param([string] $Output = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\Aldren.App\Assets\aldren.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $disc = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 14, 16, 20))
    $g.FillEllipse($disc, 0, 0, $size - 1, $size - 1)

    $width = [Math]::Max(1.5, $size * 0.11)
    $inset = $size * 0.24
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 64, 224, 255)), $width
    $g.DrawEllipse($ring, $inset, $inset, $size - 1 - 2 * $inset, $size - 1 - 2 * $inset)

    $core = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 248, 255))
    $dot = $size * 0.16
    $g.FillEllipse($core, ($size - $dot) / 2, ($size - $dot) / 2, $dot, $dot)

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

New-Item -ItemType Directory -Force (Split-Path -Parent $Output) | Out-Null
$file = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose() }
Write-Output "Wrote $Output"
