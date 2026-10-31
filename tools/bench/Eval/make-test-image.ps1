<#
.SYNOPSIS
    Draws a synthetic "file not found" dialog used to time screenshot questions (no personal content).
#>
param([string] $Out = 'E:\AldrenData\bench\s1\test-dialog.png')

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null

$bitmap = New-Object System.Drawing.Bitmap 900, 420
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'ClearTypeGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(243, 243, 243))

$text = New-Object System.Drawing.Font 'Segoe UI', 14
$title = New-Object System.Drawing.Font 'Segoe UI Semibold', 18
$black = [System.Drawing.Brushes]::Black

$g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 900, 48)
$g.DrawString('Microsoft Word', $text, $black, 16, 12)
$g.DrawString('File not found', $title, $black, 40, 90)
$g.DrawString("Word can't open report.docx because the file was moved or deleted.", $text, $black, 40, 150)

foreach ($button in @(@('Retry', 520), @('Cancel', 700))) {
    $rect = New-Object System.Drawing.Rectangle $button[1], 330, 160, 50
    $g.FillRectangle([System.Drawing.Brushes]::White, $rect)
    $g.DrawRectangle([System.Drawing.Pens]::Gray, $rect)
    $g.DrawString($button[0], $text, $black, ($button[1] + 45), 341)
}

$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bitmap.Dispose()
Write-Output "Wrote $Out"
