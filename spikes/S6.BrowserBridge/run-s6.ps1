# Guided S6 session, about 10 minutes: you click through Chrome when asked. The native-messaging key it adds is removed at the
# end even if a step fails; the extension only ever lives in the separate test profile (E:\AldrenData\browser-profile).
$ErrorActionPreference = 'Stop'
$hostExe = Join-Path $PSScriptRoot 'Host\bin\Release\net10.0-windows\win-x64\S6.Host.exe'
$cdp = Join-Path $PSScriptRoot 'CdpAttach\bin\Release\net10.0-windows\win-x64\CdpAttach.exe'
$chrome = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe' -ErrorAction SilentlyContinue).'(default)'
if (-not $chrome) { $chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe' }

function Wait-ForYou([string]$text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Cyan
    [void](Read-Host 'Press Enter when done')
}

& (Join-Path $PSScriptRoot 'register-host.ps1')
try {
    Start-Process $chrome -ArgumentList '--user-data-dir=E:\AldrenData\browser-profile', '--no-first-run', 'chrome://extensions'
    Wait-ForYou ("1. A Chrome window opened on the test profile. Go to chrome://extensions there, turn on Developer mode (top right), " +
        "click 'Load unpacked' and choose $PSScriptRoot\extension")

    Write-Host "`n2. Measuring the bridge (the test page opens in that window)..." -ForegroundColor Cyan
    & $hostExe probe

    Wait-ForYou '3. Close the test-profile Chrome window (Playwright needs the profile next).'
    & $cdp profile

    Wait-ForYou ("4. In your normal Chrome, open chrome://inspect/#remote-debugging and turn remote debugging on. " +
        "Next, Chrome asks whether to allow the connection: click Allow.")
    & $cdp attach

    Wait-ForYou '5. Turn remote debugging off again at chrome://inspect/#remote-debugging.'
}
finally {
    & (Join-Path $PSScriptRoot 'unregister-host.ps1')
    Write-Host ("Done. Results are in E:\AldrenData\bench\s6\results.md. The test extension exists only in the test profile; " +
        "deleting E:\AldrenData\browser-profile removes it.") -ForegroundColor Green
}
