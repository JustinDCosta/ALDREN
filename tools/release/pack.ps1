<#
.SYNOPSIS
    Builds a Velopack release of ALDREN (Setup.exe, full and delta packages) into E:\AldrenData\releases.
    Nothing is installed: run the Setup.exe yourself if you want an installed copy (it installs per user, no admin).
.NOTES
    vpk is installed as a local tool in E:\AldrenData\tools\vpk, pinned to the Velopack library's version.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Output = 'E:\AldrenData\releases',
    [string] $Framework = 'net10.0-x64-desktop'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$vpkVersion = '1.2.161'
$tools = 'E:\AldrenData\tools\vpk'
$vpk = Join-Path $tools 'vpk.exe'

if (-not (Test-Path $vpk)) {
    dotnet tool install vpk --version $vpkVersion --tool-path $tools
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$stage = Join-Path $Output 'stage'
if (Test-Path $stage) {
    Remove-Item -Recurse -Force $stage
}

& (Join-Path $root 'tools\dev\publish-local.ps1') -Destination $stage -Configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $vpk pack --packId Aldren --packTitle ALDREN --packAuthors 'Justin D''COSTA' --packVersion $Version `
    --packDir $stage --mainExe Aldren.Core.exe --runtime win-x64 --shortcuts StartMenuRoot --framework $Framework --outputDir $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Release $Version written to $Output"
