# Registers the S6 probe's native-messaging host with Chrome, for the current user only, allowing only the test extension.
# Writes one key, HKCU\Software\Google\Chrome\NativeMessagingHosts\com.aldren.bridge; unregister-host.ps1 removes it.
$ErrorActionPreference = 'Stop'
$extensionId = 'mhipkigoofdhdiigilekjfpkhemnjajm' # fixed by the "key" in extension\manifest.json
$exe = Join-Path $PSScriptRoot 'Host\bin\Release\net10.0-windows\win-x64\S6.Host.exe'
if (-not (Test-Path $exe)) { throw "Build the host first: dotnet build `"$PSScriptRoot\Host`" -c Release" }

$manifest = Join-Path (Split-Path $exe) 'com.aldren.bridge.json'
$json = [ordered]@{
    name            = 'com.aldren.bridge'
    description     = 'ALDREN browser bridge probe (Phase 0)'
    path            = $exe
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
} | ConvertTo-Json
[IO.File]::WriteAllText($manifest, $json, (New-Object Text.UTF8Encoding($false)))

$key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.aldren.bridge'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value $manifest
Write-Host "Registered com.aldren.bridge for extension $extensionId"
