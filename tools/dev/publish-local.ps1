<#
.SYNOPSIS
    Publishes the development build of Core, the App, the browser host and the Companion extension into
    E:\AldrenData\dev\aldren. Refuses to run while an ALDREN process from that folder is running.
#>
param(
    [string] $Destination = 'E:\AldrenData\dev\aldren',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$destinationFull = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
$running = Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($destinationFull, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Write-Output "Refusing to publish: ALDREN is running from $Destination ($(($running | ForEach-Object { "$($_.ProcessName) $($_.Id)" }) -join ', ')). Quit it first."
    exit 1
}
New-Item -ItemType Directory -Force $Destination | Out-Null
dotnet publish "$root\src\Aldren.Core\Aldren.Core.csproj" -c $Configuration -r win-x64 --self-contained false -o $Destination -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish "$root\src\Aldren.App\Aldren.App.csproj" -c $Configuration -r win-x64 -p:WindowsAppSDKSelfContained=true -o $Destination -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish "$root\src\Aldren.BrowserHost\Aldren.BrowserHost.csproj" -c $Configuration -r win-x64 --self-contained false -o $Destination -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# The Companion extension, for "Load unpacked" in chrome://extensions.
Remove-Item -Recurse -Force (Join-Path $Destination 'extension') -ErrorAction SilentlyContinue
Copy-Item -Recurse (Join-Path $root 'extensions\chrome') (Join-Path $Destination 'extension')
Write-Output "Published to $Destination"
